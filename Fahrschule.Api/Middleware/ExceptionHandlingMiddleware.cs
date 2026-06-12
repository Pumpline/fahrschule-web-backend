using Fahrschule.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Middleware;

/// <summary>
/// Central error handling: catches ALL exceptions and turns them into a
/// consistent, understandable JSON response in the "ProblemDetails" format
/// (a web standard, RFC 9457, for API error responses).
///
/// Why central? No controller has to write try/catch, all errors look the
/// same to the frontend, and technical details stay in the server log instead
/// of reaching the user (project rule 2: understandable messages - the
/// user-facing texts are German).
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // Optimistic concurrency (project rule 7): someone else saved the
            // record in the meantime. Do not overwrite - tell the user what to do.
            await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                "Diese Daten wurden zwischenzeitlich von jemand anderem geändert. " +
                "Bitte laden Sie die Liste neu und tragen Sie Ihre Änderung dann noch einmal ein.");
        }
        catch (AppException ex)
        {
            // Expected business error: the message is meant for the user.
            var statusCode = ex switch
            {
                AppValidationException => StatusCodes.Status400BadRequest,
                AuthenticationFailedException => StatusCodes.Status401Unauthorized,
                ForbiddenException => StatusCodes.Status403Forbidden,
                NotFoundException => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest,
            };

            await WriteProblemAsync(context, statusCode, ex.Message);
        }
        catch (Exception ex)
        {
            // Unexpected error: details go to the log ONLY (with stack trace
            // for debugging) - the user receives a neutral message.
            logger.LogError(ex, "Unbehandelter Fehler bei {Method} {Path}",
                context.Request.Method, context.Request.Path);

            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError,
                "Es ist ein unerwarteter Fehler aufgetreten. Bitte versuchen Sie es noch einmal.");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
    {
        if (context.Response.HasStarted)
        {
            return; // The response is already on its way - nothing we can change.
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = statusCode >= 500 ? "Unerwarteter Fehler" : "Aktion nicht möglich",
            Detail = detail,
        };

        await context.Response.WriteAsJsonAsync(problem);
    }
}
