using Fahrschule.Application.Audit;
using Fahrschule.Application.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fahrschule.Application;

/// <summary>Registriert die Fachlogik-Dienste (Services) im DI-Container.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Bindet den Abschnitt "Jwt" aus appsettings an die JwtOptions-Klasse
        // ("Options Pattern" – typisierte Konfiguration statt loser Strings).
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        // "Scoped" = eine Instanz pro HTTP-Anfrage (passend zum DbContext).
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditWriter, AuditWriter>();

        // "Singleton" = eine Instanz für die gesamte Laufzeit (zustandslos, nur Konfiguration).
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
