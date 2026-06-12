using Fahrschule.Application.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Middleware;

/// <summary>
/// Enforces the rule "temporary password → set your own password first"
/// on the backend side as well (KONZEPT 3.7a).
///
/// The frontend redirects such users to the set-password page, but that
/// alone must never be relied on: anyone could call the API directly.
/// Security rules therefore ALWAYS belong (also) in the backend.
/// Only the /api/auth endpoints stay accessible (change password, logout ...).
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
