using System.Security.Claims;
using Fahrschule.Application.Auth;
using Fahrschule.Application.Common;
using Fahrschule.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// Die Anmelde-Endpunkte der API.
///
/// Controller sind bei uns bewusst DÜNN (Projektregel 5): Sie nehmen die
/// Anfrage entgegen, rufen den passenden Service auf und verpacken das
/// Ergebnis als HTTP-Antwort. Die eigentliche Fachlogik steckt im AuthService.
/// Cookies sind ein HTTP-Detail – darum kümmert sich der Controller, nicht der Service.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Anmelden mit E-Mail + Passwort. Setzt bei Erfolg die beiden
    /// httpOnly-Cookies und liefert die Benutzerdaten fürs Frontend.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request.Email, request.Password, ct);
        AuthCookies.Write(HttpContext, result);
        return Ok(result.User);
    }

    /// <summary>Tauscht das Refresh-Token (aus dem Cookie) gegen ein frisches
    /// Token-Paar. Ruft das Frontend automatisch auf, wenn das Zugriffstoken abläuft.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserDto>> Refresh(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(AuthCookies.RefreshTokenName, out var refreshToken)
            || string.IsNullOrEmpty(refreshToken))
        {
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }

        var result = await authService.RefreshAsync(refreshToken, ct);
        AuthCookies.Write(HttpContext, result);
        return Ok(result.User);
    }

    /// <summary>Abmelden: Refresh-Token entwerten und beide Cookies löschen.
    /// Bewusst [AllowAnonymous] – Abmelden soll auch mit abgelaufener Sitzung klappen.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (Request.Cookies.TryGetValue(AuthCookies.RefreshTokenName, out var refreshToken)
            && !string.IsNullOrEmpty(refreshToken))
        {
            await authService.LogoutAsync(refreshToken, ct);
        }

        AuthCookies.Clear(HttpContext);
        return NoContent();
    }

    /// <summary>Passwort ändern – auch das erzwungene Festlegen nach der ersten
    /// Anmeldung mit temporärem Passwort. Meldet aus Sicherheitsgründen alle
    /// anderen Geräte ab und stellt für dieses Gerät neue Tokens aus.</summary>
    [HttpPost("change-password")]
    public async Task<ActionResult<CurrentUserDto>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await authService.ChangePasswordAsync(
            GetUserId(), request.CurrentPassword, request.NewPassword, ct);
        AuthCookies.Write(HttpContext, result);
        return Ok(result.User);
    }

    /// <summary>Liefert den aktuell angemeldeten Benutzer ("wer bin ich?").
    /// Das Frontend ruft das beim Start auf, um eine bestehende Sitzung zu erkennen.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        return Ok(await authService.GetCurrentUserAsync(GetUserId(), ct));
    }

    /// <summary>Liest die Benutzer-ID aus dem geprüften Token (Claim "sub" –
    /// das Framework benennt ihn beim Einlesen in NameIdentifier um).</summary>
    private Guid GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
        {
            throw new AuthenticationFailedException("Die Sitzung ist abgelaufen. Bitte melden Sie sich neu an.");
        }
        return userId;
    }
}
