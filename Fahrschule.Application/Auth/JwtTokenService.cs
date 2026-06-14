using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fahrschule.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fahrschule.Application.Auth;

/// <summary>Creates signed access tokens (JWT) for signed-in users.</summary>
public interface IJwtTokenService
{
    /// <summary>Creates an access token together with its expiry time (UTC).</summary>
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IList<string> roles);
}

/// <summary>
/// Common web concept "JWT" (JSON Web Token): a small, signed data package
/// the server issues at sign-in. It contains "claims" (statements about the
/// user: who they are, which roles they have). On every request the server
/// only verifies the signature - no database access needed.
///
/// Important: A JWT is signed but NOT encrypted - anyone holding it can read
/// its content. That is why it contains no secrets, and why we put it into an
/// httpOnly cookie (unreadable for malicious scripts).
/// </summary>
public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Custom claim: the user still has to set their own password.</summary>
    public const string MustChangePasswordClaim = "must_change_pwd";

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            // "sub" (subject) = whose token this is. Standard claim from the JWT spec.
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            // Unique token id - useful for logs and debugging.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(MustChangePasswordClaim, user.MustChangePassword ? "true" : "false"),
        };

        // Roles as individual claims - ASP.NET Core turns these into the
        // [Authorize(Roles=...)] check.
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
