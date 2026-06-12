namespace Fahrschule.Application.Common;

/// <summary>
/// Custom exception types for expected business errors.
///
/// Why: Services throw these with an UNDERSTANDABLE German message (user-
/// facing!); a central place in the API (ExceptionHandlingMiddleware)
/// translates them into the matching HTTP response (400/401/403/404).
/// This yields consistent, solution-oriented error messages instead of
/// technical jargon (project rule 2) - and keeps controllers thin.
/// </summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>Input invalid for business reasons → HTTP 400 (Bad Request).</summary>
public class AppValidationException(string message) : AppException(message);

/// <summary>Sign-in failed or session expired → HTTP 401 (Unauthorized).</summary>
public class AuthenticationFailedException(string message) : AppException(message);

/// <summary>Signed in, but lacking permission → HTTP 403 (Forbidden).</summary>
public class ForbiddenException(string message) : AppException(message);

/// <summary>Record does not exist (or is deleted) → HTTP 404 (Not Found).</summary>
public class NotFoundException(string message) : AppException(message);
