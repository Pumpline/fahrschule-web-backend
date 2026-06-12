using Fahrschule.Application.Auth;

namespace Fahrschule.Api;

/// <summary>
/// Schreibt und löscht die beiden Anmelde-Cookies.
///
/// Warum Cookies statt localStorage? Ein httpOnly-Cookie kann von JavaScript
/// nicht ausgelesen werden – selbst wenn sich ein Schad-Skript in die Seite
/// mogelt (XSS), kommt es nicht an die Tokens heran.
///
/// Schutz vor CSRF (gefälschte Anfragen von fremden Seiten): SameSite=Strict
/// sorgt dafür, dass der Browser die Cookies NUR mitschickt, wenn die Anfrage
/// von unserer eigenen Seite kommt.
/// </summary>
public static class AuthCookies
{
    public const string AccessTokenName = "fs_access";
    public const string RefreshTokenName = "fs_refresh";

    /// <summary>Der Refresh-Cookie wird nur an die Auth-Endpunkte geschickt –
    /// je seltener er unterwegs ist, desto kleiner die Angriffsfläche.</summary>
    public const string RefreshTokenPath = "/api/auth";

    public static void Write(HttpContext context, AuthResult auth)
    {
        // "Secure" heißt: nur über HTTPS übertragen. In der lokalen Entwicklung
        // läuft die API über HTTP, deshalb richten wir uns nach der Verbindung.
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
        // Löschen = gleiches Cookie mit Ablaufdatum in der Vergangenheit setzen.
        context.Response.Cookies.Delete(AccessTokenName, new CookieOptions { Path = "/" });
        context.Response.Cookies.Delete(RefreshTokenName, new CookieOptions { Path = RefreshTokenPath });
    }
}
