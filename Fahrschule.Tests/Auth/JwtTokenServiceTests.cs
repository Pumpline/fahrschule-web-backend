using System.IdentityModel.Tokens.Jwt;
using Fahrschule.Application.Auth;
using Fahrschule.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Fahrschule.Tests.Auth;

/// <summary>Tests for JWT creation: are content (claims) and lifetime correct?</summary>
public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options = new()
    {
        Issuer = "Test.Api",
        Audience = "Test.Frontend",
        SecretKey = "test-key-with-at-least-32-characters!!",
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

        // Parse the token back to inspect it (without signature validation -
        // only the content matters here).
        return new JwtSecurityTokenHandler().ReadJwtToken(token);
    }

    [Fact]
    public void Token_contains_user_and_roles()
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
    public void Token_carries_issuer_audience_and_lifetime()
    {
        var jwt = CreateAndParse(User, []);

        Assert.Equal("Test.Api", jwt.Issuer);
        Assert.Contains("Test.Frontend", jwt.Audiences);

        // Lifetime ≈ 15 minutes (with some tolerance for test execution).
        var lifetime = jwt.ValidTo - DateTime.UtcNow;
        Assert.InRange(lifetime, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(16));
    }

    [Fact]
    public void Temporary_password_shows_up_as_claim_in_the_token()
    {
        var userWithTempPassword = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = "new@fahrschule.local",
            DisplayName = "Neuer Benutzer",
            MustChangePassword = true,
        };

        var jwt = CreateAndParse(userWithTempPassword, []);

        // The MustChangePasswordMiddleware relies on this claim to block
        // everything except /api/auth until an own password is set.
        Assert.Contains(jwt.Claims, c =>
            c.Type == JwtTokenService.MustChangePasswordClaim && c.Value == "true");
    }
}
