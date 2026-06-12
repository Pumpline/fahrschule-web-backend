using System.Security.Claims;
using Fahrschule.Application.Auth;
using Fahrschule.Application.Common;
using Fahrschule.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Controllers;

/// <summary>
/// The authentication endpoints of the API.
///
/// Our controllers are deliberately THIN (project rule 5): they accept the
/// request, call the matching service and wrap the result as an HTTP
/// response. The actual business logic lives in the AuthService. Cookies are
/// an HTTP detail - the controller handles them, not the service.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    /// <summary>Sign in with e-mail + password. On success sets the two
    /// httpOnly cookies and returns the user details for the frontend.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<CurrentUserDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var result = await authService.LoginAsync(request.Email, request.Password, ct);
        AuthCookies.Write(HttpContext, result);
        return Ok(result.User);
    }

    /// <summary>Exchanges the refresh token (from the cookie) for a fresh
    /// token pair. The frontend calls this automatically when the access
    /// token expires.</summary>
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

    /// <summary>Sign out: revoke the refresh token and clear both cookies.
    /// Deliberately [AllowAnonymous] - signing out must also work with an
    /// expired session.</summary>
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

    /// <summary>Change the password - also the forced set-password step after
    /// the first sign-in with a temporary password. For security this signs
    /// out all other devices and issues fresh tokens for this one.</summary>
    [HttpPost("change-password")]
    public async Task<ActionResult<CurrentUserDto>> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await authService.ChangePasswordAsync(
            GetUserId(), request.CurrentPassword, request.NewPassword, ct);
        AuthCookies.Write(HttpContext, result);
        return Ok(result.User);
    }

    /// <summary>Returns the currently signed-in user ("who am I?").
    /// The frontend calls this at startup to detect an existing session.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
    {
        return Ok(await authService.GetCurrentUserAsync(GetUserId(), ct));
    }

    /// <summary>Reads the user id from the verified token (claim "sub" -
    /// the framework renames it to NameIdentifier while parsing).</summary>
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
