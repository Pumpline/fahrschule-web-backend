using Fahrschule.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Middleware;

/// <summary>
/// Setzt die Regel "temporäres Passwort → erst eigenes Passwort festlegen"
/// auch im Backend durch (KONZEPT 3.7a).
///
/// Das Frontend leitet solche Benutzer zwar zur Passwort-festlegen-Seite,
/// aber darauf allein darf man sich nie verlassen: Jeder könnte die API auch
/// direkt aufrufen. Sicherheitsregeln gehören deshalb IMMER (auch) ins Backend.
/// Erlaubt bleiben nur die /api/auth-Endpunkte (Passwort ändern, Abmelden …).
/// </summary>
public class MustChangePasswordMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var isAuthenticated = context.User.Identity?.IsAuthenticated == true;
        var mustChange = context.User.FindFirst(JwtTokenService.MustChangePasswordClaim)?.Value == "true";
        var isAuthEndpoint = context.Request.Path.StartsWithSegments("/api/auth");

        if (isAuthenticated && mustChange && !isAuthEndpoint)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Passwort festlegen erforderlich",
                Detail = "Bitte legen Sie zuerst ein eigenes Passwort fest. Danach können Sie die Anwendung nutzen.",
            });
            return;
        }

        await next(context);
    }
}
