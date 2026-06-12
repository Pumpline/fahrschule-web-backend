namespace Fahrschule.Application.Common;

/// <summary>
/// Eigene Fehlerklassen für erwartbare Fachfehler.
///
/// Warum: Die Services werfen diese Fehler mit einer VERSTÄNDLICHEN deutschen
/// Meldung; eine zentrale Stelle in der API (ExceptionHandlingMiddleware)
/// übersetzt sie in die passende HTTP-Antwort (400/401/403/404). So gibt es
/// einheitliche, lösungsorientierte Fehlermeldungen statt Technik-Kauderwelsch
/// (Projektregel 2) – und die Controller bleiben dünn.
/// </summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>Eingabe fachlich ungültig → HTTP 400 (Bad Request).</summary>
public class AppValidationException(string message) : AppException(message);

/// <summary>Anmeldung fehlgeschlagen oder Sitzung abgelaufen → HTTP 401 (Unauthorized).</summary>
public class AuthenticationFailedException(string message) : AppException(message);

/// <summary>Angemeldet, aber keine Berechtigung → HTTP 403 (Forbidden).</summary>
public class ForbiddenException(string message) : AppException(message);

/// <summary>Datensatz existiert nicht (oder ist gelöscht) → HTTP 404 (Not Found).</summary>
public class NotFoundException(string message) : AppException(message);
