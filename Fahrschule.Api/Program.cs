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

// Program.cs is the entry point of the API: services are wired up here
// (dependency injection) and the request "pipeline" is defined (which
// stations every HTTP request passes through).

var builder = WebApplication.CreateBuilder(args);

// Local overrides (e.g. the real database credentials of this machine).
// The file is excluded via .gitignore - real passwords never end up in the
// repository (project rule 1: no secrets in the repo).
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// ----------------------------------------------------------------------------
// 1. Register services (dependency injection)
// ----------------------------------------------------------------------------

builder.Services.AddInfrastructure(builder.Configuration); // database + Identity
builder.Services.AddApplication(builder.Configuration);    // business logic (services)

builder.Services.AddControllers();
builder.Services.AddOpenApi(); // API description at /openapi/v1.json (development only)

// The retention job: a daily background task that permanently removes students
// whose retention period has elapsed (KONZEPT rule 7).
builder.Services.AddHostedService<Fahrschule.Api.BackgroundJobs.RetentionBackgroundService>();

// Automatic input validation ([Required] etc.) must answer in German (user-facing).
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

// --- Authentication: read and verify the JWT from the httpOnly cookie -------
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
    // Safety net: the checked-in development key must never run in production.
    throw new InvalidOperationException(
        "Der Entwicklungs-JWT-Schlüssel darf in Produktion nicht verwendet werden. " +
        "Bitte Jwt:SecretKey als Umgebungsvariable setzen.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // The framework checks these rules on EVERY request automatically:
        // Is the signature genuine? Is the token still valid? Issuer/audience ok?
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SecretKey)),
            ValidateLifetime = true,
            // By default the framework allows 5 minutes of "clock tolerance" -
            // we stay strict so 15 minutes really mean 15 minutes.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        // The usual place for a JWT is the "Authorization" header. We put it
        // into an httpOnly cookie instead (unreadable for malicious scripts →
        // XSS protection) and teach the framework to look for it there.
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

// "Secure by default": EVERY endpoint requires authentication unless it is
// explicitly opened up with [AllowAnonymous] (e.g. /api/auth/login).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// CORS is only needed when the frontend does NOT run behind the dev proxy /
// the same host.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials())); // required so cookies may be sent along
}

var app = builder.Build();

// ----------------------------------------------------------------------------
// 2. The request pipeline - every HTTP request passes these stations
//    in exactly this order (middleware concept).
// ----------------------------------------------------------------------------

// Outermost: catches all errors and turns them into understandable JSON responses.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Secure HTTP headers (project rule 1: security).
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff"; // no content sniffing
    context.Response.Headers["X-Frame-Options"] = "DENY";           // not embeddable in foreign pages
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseHsts();             // tell browsers: HTTPS only for this domain
    app.UseHttpsRedirection(); // redirect HTTP → HTTPS (HTTPS is mandatory)
}

if (allowedOrigins.Length > 0)
{
    app.UseCors();
}

app.UseAuthentication(); // Who are you? (verify cookie/JWT)
app.UseAuthorization();  // Are you allowed? (verify roles/policies)

// While the user still has a temporary password, everything except /api/auth is blocked.
app.UseMiddleware<MustChangePasswordMiddleware>();

app.MapControllers();

// Simple liveness endpoint (e.g. for Docker health checks).
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// ----------------------------------------------------------------------------
// 3. Prepare the database (migrations + roles + seed data + first admin)
// ----------------------------------------------------------------------------
if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    await DatabaseInitializer.InitializeAsync(app.Services);

    // Seed the operational settings with their defaults (the settings service
    // lives in the Application layer, which the Infrastructure initializer
    // cannot reach - so we trigger it here from the API layer).
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<Fahrschule.Application.Settings.ISettingsService>()
        .SeedDefaultsAsync();
}

app.Run();
