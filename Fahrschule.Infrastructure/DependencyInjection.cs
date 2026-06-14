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

                // No per-ACCOUNT lockout: brute force is handled per CLIENT IP
                // instead (see LoginThrottle - escalating cooldown from the 3rd
                // failed attempt). Locking the account would let an attacker who
                // knows a user name lock a legitimate user out ("denial of
                // service"); throttling the IP avoids that.
                options.Lockout.AllowedForNewUsers = false;

                // Accounts log in by user name, not e-mail - so no e-mail is
                // stored or required (RequireUniqueEmail would otherwise reject
                // the empty e-mail with "Email '' is invalid").
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<FahrschuleDbContext>();

        return services;
    }
}
