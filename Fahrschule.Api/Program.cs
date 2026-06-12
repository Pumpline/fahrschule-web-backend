using System.Text;
using Fahrschule.Api;
using Fahrschule.Api.Middleware;
using Fahrschule.Application;
using Fahrschule.Application.Auth;
using Fahrschule.Infrastructure;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

// Program.cs ist der Startpunkt der API – vergleichbar mit der Hauptszene in
// Unity: Hier wird alles zusammengesteckt (Dienste registrieren) und dann die
// "Pipeline" definiert (welche Stationen jede HTTP-Anfrage durchläuft).

var builder = WebApplication.CreateBuilder(args);

// Lokale Überschreibungen (z. B. echte Datenbank-Zugangsdaten dieses Rechners).
// Die Datei ist per .gitignore vom Repository ausgeschlossen – so landen
// niemals echte Passwörter im Code (Projektregel 1: keine Secrets im Repo).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ----------------------------------------------------------------------------
// 1. Dienste registrieren (Dependency Injection)
// ----------------------------------------------------------------------------

builder.Services.AddInfrastructure(builder.Configuration); // Datenbank + Identity
builder.Services.AddApplication(builder.Configuration);    // Fachlogik (Services)

builder.Services.AddControllers();
builder.Services.AddOpenApi(); // API-Beschreibung unter /openapi/v1.json (nur Entwicklung)

// Automatische Eingabe-Prüfung ([Required] usw.) soll deutsch antworten.
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    var defaultFactory = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        var result = defaultFactory(context);
        if (result is ObjectResult { Value: ValidationProblemDetails problem })
        {
            problem.Title = "Bitte prüfen Sie Ihre Eingaben.";
        }
        return result;
    };
});

// --- Anmeldung: JWT aus dem httpOnly-Cookie lesen und prüfen ----------------
var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Konfigurationsabschnitt 'Jwt' fehlt.");

if (string.IsNullOrWhiteSpace(jwtOptions.SecretKey) || jwtOptions.SecretKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey fehlt oder ist zu kurz (mindestens 32 Zeichen). " +
        "In Produktion über Umgebungsvariable setzen – nie ins Repository!");
}

if (!builder.Environment.IsDevelopment() && jwtOptions.SecretKey.Contains("NUR-FUER-ENTWICKLUNG"))
{
    // Sicherheitsnetz: der eingecheckte Entwicklungs-Schlüssel darf nie produktiv laufen.
    throw new InvalidOperationException(
        "Der Entwicklungs-JWT-Schlüssel darf in Produktion nicht verwendet werden. " +
        "Bitte Jwt:SecretKey als Umgebungsvariable setzen.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Diese Regeln prüft das Framework bei JEDER Anfrage automatisch:
        // Ist die Signatur echt? Ist das Token noch gültig? Passt Aussteller/Empfänger?
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ValidateLifetime = true,
            // Standardmäßig erlaubt das Framework 5 Minuten "Uhren-Toleranz" –
            // wir bleiben streng, damit 15 Minuten wirklich 15 Minuten sind.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // Üblich ist das JWT im "Authorization"-Header. Wir legen es stattdessen
        // in ein httpOnly-Cookie (für Schad-Skripte unlesbar → XSS-Schutz) und
        // bringen dem Framework hier bei, es dort zu suchen.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (context.Request.Cookies.TryGetValue(AuthCookies.AccessTokenName, out var token))
                {
                    context.Token = token;
                }
                return Task.CompletedTask;
            },
        };
    });

// "Sicher per Voreinstellung": JEDER Endpunkt verlangt Anmeldung, außer er ist
// ausdrücklich mit [AllowAnonymous] freigegeben (z. B. /api/auth/login).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// CORS nur nötig, wenn das Frontend NICHT über den Dev-Proxy/gleichen Host läuft.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // nötig, damit Cookies mitgeschickt werden dürfen
}

var app = builder.Build();

// ----------------------------------------------------------------------------
// 2. Die Anfrage-Pipeline – jede HTTP-Anfrage durchläuft diese Stationen
//    in genau dieser Reihenfolge (Middleware-Konzept).
// ----------------------------------------------------------------------------

// Ganz außen: fängt alle Fehler und macht daraus verständliche JSON-Antworten.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Sichere HTTP-Header (Projektregel 1: Sicherheit).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff"; // kein Inhalts-Raten
    context.Response.Headers["X-Frame-Options"] = "DENY";           // nicht in fremde Seiten einbettbar
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseHsts();             // Browser anweisen: nur noch HTTPS für diese Domain
    app.UseHttpsRedirection(); // HTTP → HTTPS umleiten (HTTPS-Pflicht)
}

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication(); // Wer bist du? (Cookie/JWT prüfen)
app.UseAuthorization();  // Darfst du das? (Rollen/Policies prüfen)

// Hat der Benutzer noch ein temporäres Passwort, ist alles außer /api/auth gesperrt.
app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapControllers();

// Einfacher Lebenszeichen-Endpunkt (z. B. für Docker-Healthchecks).
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// ----------------------------------------------------------------------------
// 3. Datenbank vorbereiten (Migrationen + Rollen + erster Admin)
// ----------------------------------------------------------------------------
if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    await DatabaseInitializer.InitializeAsync(app.Services);
}

app.Run();
