using Fahrschule.Application.Audit;
using Fahrschule.Application.Auth;
using Fahrschule.Application.Curriculum;
using Fahrschule.Application.Documents;
using Fahrschule.Application.LicenseClasses;
using Fahrschule.Application.Settings;
using Fahrschule.Application.Students;
using Fahrschule.Application.Users;
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
        services.AddScoped<IDocumentCatalogService, DocumentCatalogService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IStudentService, StudentService>();

        // "Singleton" = one instance for the whole runtime (stateless, config only).
        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
