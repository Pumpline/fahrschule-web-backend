using Fahrschule.Application.Auth;

namespace Fahrschule.Api;

/// <summary>
/// Writes and clears the two authentication cookies.
///
/// Why cookies instead of localStorage? An httpOnly cookie cannot be read by
/// JavaScript - even if a malicious script sneaks into the page (XSS), it
/// cannot reach the tokens.
///
/// CSRF protection (forged requests from foreign sites): SameSite=Strict
/// makes the browser send the cookies ONLY when the request originates from
/// our own site.
/// </summary>
public static class AuthCookies
{
    public const string AccessTokenName = "fs_access";
    public const string RefreshTokenName = "fs_refresh";

    /// <summary>The refresh cookie is only sent to the auth endpoints - the
    /// less it travels, the smaller the attack surface.</summary>
    public const string RefreshTokenPath = "/api/auth";

    public static void Write(HttpContext context, AuthResult auth)
    {
        // "Secure" means: transmit over HTTPS only. Local development runs the
        // API over HTTP, so we follow the actual connection.
        var secure = context.Request.IsHttps;

        context.Response.Cookies.Append(AccessTokenName, auth.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = auth.AccessTokenExpiresAtUtc,
        });

        context.Response.Cookies.Append(RefreshTokenName, auth.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = RefreshTokenPath,
            Expires = auth.RefreshTokenExpiresAtUtc,
        });
    }

    public static void Clear(HttpContext context)
    {
        // Deleting = setting the same cookie with an expiry in the past.
        context.Response.Cookies.Delete(AccessTokenName, new CookieOptions { Path = "/" });
        context.Response.Cookies.Delete(RefreshTokenName, new CookieOptions { Path = RefreshTokenPath });
    }
}
