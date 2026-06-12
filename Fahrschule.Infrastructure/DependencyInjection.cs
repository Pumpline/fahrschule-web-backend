using Fahrschule.Infrastructure.Identity;
using Fahrschule.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fahrschule.Infrastructure;

/// <summary>
/// Registers all infrastructure services in the dependency injection container.
///
/// Common web concept "Dependency Injection" (DI): classes do not create their
/// dependencies themselves (no "new DbContext()") - the framework hands them
/// into the constructor. Here we tell the container HOW to build the services.
/// Benefit: replaceable and easy to test.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Verbindungszeichenfolge 'ConnectionStrings:Default' fehlt (appsettings oder Umgebungsvariable).");

        services.AddDbContext<FahrschuleDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Identity = the ready-made user/password system of ASP.NET Core.
        // AddIdentityCore instead of AddIdentity because we do NOT want the
        // Identity cookies/pages - our sign-in runs through the JSON API with
        // our own JWT cookie.
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Enforce strong passwords (project rule 7) - length matters
                // more than mandatory special characters (friendlier for the
                // older users of this app).
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireNonAlphanumeric = false;

                // Account lockout against password guessing (brute force):
                // locked for 15 minutes after 5 failed attempts.
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<FahrschuleDbContext>();

        return services;
    }
}
