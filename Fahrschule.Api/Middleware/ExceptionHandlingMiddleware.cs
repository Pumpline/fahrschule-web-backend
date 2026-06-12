using Fahrschule.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace Fahrschule.Api.Middleware;

/// <summary>
/// Zentrales Fehler-Handling: fängt ALLE Ausnahmen und macht daraus eine
/// einheitliche, verständliche JSON-Antwort im "ProblemDetails"-Format
/// (ein Web-Standard, RFC 9457, für Fehlerantworten von APIs).
///
/// Warum zentral? Kein Controller muss try/catch schreiben, alle Fehler sehen
/// für das Frontend gleich aus, und technische Details bleiben im Server-Log
/// statt beim Benutzer zu landen (Projektregel 2: verständliche Meldungen).
///
/// Web-typisches Konzept "Middleware": eine Station in der Anfrage-Pipeline.
/// Diese hier legt sich wie ein Schutzmantel um alle folgenden Stationen.
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
            // Optimistische Nebenläufigkeit (Projektregel 7): Jemand anderes hat
            // den Datensatz zwischenzeitlich gespeichert. Nicht überschreiben,
            // sondern dem Benutzer verständlich sagen, was zu tun ist.
            await WriteProblemAsync(context, StatusCodes.Status409Conflict,
                "Diese Daten wurden zwischenzeitlich von jemand anderem geändert. " +
                "Bitte laden Sie die Liste neu und tragen Sie Ihre Änderung dann noch einmal ein.");
        }
        catch (AppException ex)
        {
            // Erwartbarer Fachfehler: die Meldung ist für den Benutzer gedacht.
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
            // Unerwarteter Fehler: Details NUR ins Log (mit Stacktrace für die
            // Fehlersuche) – der Benutzer bekommt eine neutrale Meldung.
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
            return; // Antwort schon unterwegs – da können wir nichts mehr ändern.
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
