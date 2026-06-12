using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fahrschule.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fahrschule.Application.Auth;

/// <summary>Erzeugt signierte Zugriffstokens (JWT) für angemeldete Benutzer.</summary>
public interface IJwtTokenService
{
    /// <summary>Erstellt ein Zugriffstoken samt Ablaufzeitpunkt (UTC).</summary>
    (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IList<string> roles);
}

/// <summary>
/// Web-typisches Konzept "JWT" (JSON Web Token): ein kleines, signiertes
/// Datenpaket, das der Server beim Anmelden ausstellt. Es enthält "Claims"
/// (Aussagen über den Benutzer: wer er ist, welche Rollen er hat). Bei jeder
/// Anfrage prüft der Server nur die Signatur – kein Datenbankzugriff nötig.
///
/// Wichtig: Ein JWT ist signiert, aber NICHT verschlüsselt – jeder, der es
/// besitzt, kann den Inhalt lesen. Deshalb stehen darin keine Geheimnisse,
/// und es wandert bei uns in ein httpOnly-Cookie (für Schad-Skripte unlesbar).
/// </summary>
public class JwtTokenService(IOptions<JwtOptions> options) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    /// <summary>Eigener Claim: Benutzer muss erst ein eigenes Passwort setzen.</summary>
    public const string MustChangePasswordClaim = "must_change_pwd";

    public (string Token, DateTime ExpiresAtUtc) CreateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            // "sub" (Subject) = wessen Token ist das. Standard-Claim aus dem JWT-Standard.
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.DisplayName),
            // Eindeutige Token-ID – nützlich für Protokolle und Fehlersuche.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(MustChangePasswordClaim, user.MustChangePassword ? "true" : "false"),
        };

        // Rollen als einzelne Claims – daraus macht ASP.NET Core die [Authorize(Roles=…)]-Prüfung.
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
