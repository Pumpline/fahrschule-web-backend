using System.IdentityModel.Tokens.Jwt;
using Fahrschule.Application.Auth;
using Fahrschule.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Fahrschule.Tests.Auth;

/// <summary>Tests für die JWT-Erzeugung: stimmen Inhalt (Claims) und Laufzeit?</summary>
public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "Test.Api",
        Audience = "Test.Frontend",
        SecretKey = "test-schluessel-mindestens-32-zeichen-lang!",
        AccessTokenMinutes = 15,
    };

    private static readonly ApplicationUser User = new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Email = "helga@fahrschule.local",
        DisplayName = "Helga Muster",
        MustChangePassword = false,
    };

    private static JwtSecurityToken CreateAndParse(ApplicationUser user, IList<string> roles)
    {
        var service = new JwtTokenService(Microsoft.Extensions.Options.Options.Create(Options));
        var (token, _) = service.CreateAccessToken(user, roles);

        // Zum Prüfen lesen wir das Token wieder ein (ohne Signaturprüfung –
        // hier interessiert nur der Inhalt).
        return new JwtSecurityTokenHandler().ReadJwtToken(token);
    }

    [Fact]
    public void Token_enthaelt_Benutzer_und_Rollen()
    {
        var jwt = CreateAndParse(User, [Roles.Admin, Roles.Fahrlehrer]);

        Assert.Equal(User.Id.ToString(), jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == "email" && c.Value == "helga@fahrschule.local");

        var roleClaims = jwt.Claims
            .Where(c => c.Type is "role" or System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray();
        Assert.Contains(Roles.Admin, roleClaims);
        Assert.Contains(Roles.Fahrlehrer, roleClaims);
    }

    [Fact]
    public void Token_traegt_Aussteller_Empfaenger_und_Laufzeit()
    {
        var jwt = CreateAndParse(User, []);

        Assert.Equal("Test.Api", jwt.Issuer);
        Assert.Contains("Test.Frontend", jwt.Audiences);

        // Laufzeit ≈ 15 Minuten (mit etwas Toleranz für die Testausführung).
        var lifetime = jwt.ValidTo - DateTime.UtcNow;
        Assert.InRange(lifetime, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public void Temporaeres_Passwort_steht_als_Claim_im_Token()
    {
        var userWithTempPassword = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "neu@fahrschule.local",
            DisplayName = "Neuer Benutzer",
            MustChangePassword = true,
        };

        var jwt = CreateAndParse(userWithTempPassword, []);

        // Auf diesen Claim stützt sich die MustChangePasswordMiddleware,
        // die alles außer /api/auth sperrt, bis ein eigenes Passwort gesetzt ist.
        Assert.Contains(jwt.Claims, c =>
            c.Type == JwtTokenService.MustChangePasswordClaim && c.Value == "true");
    }
}
