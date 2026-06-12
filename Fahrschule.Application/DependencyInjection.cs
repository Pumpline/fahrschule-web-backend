using Fahrschule.Application.Audit;
using Fahrschule.Application.Auth;
using Fahrschule.Application.Curriculum;
using Fahrschule.Application.LicenseClasses;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fahrschule.Application;

/// <summary>Registers the business logic services in the DI container.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        // Binds the "Jwt" section from appsettings to the JwtOptions class
        // ("options pattern" - typed configuration instead of loose strings).
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        // "Scoped" = one instance per HTTP request (matches the DbContext).
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<ILicenseClassService, LicenseClassService>();
        services.AddScoped<ICurriculumItemService, CurriculumItemService>();

        // "Singleton" = one instance for the whole runtime (stateless, config only).
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
