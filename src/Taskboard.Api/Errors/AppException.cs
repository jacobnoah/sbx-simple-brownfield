// The one exception type features throw.
//
// SHARED FILE - read-only for feature agents.
//
// Features throw AppException, or one of its static factories. Never throw a
// bare Exception, and never return an ad-hoc error body from a handler. The
// registered IExceptionHandler turns an AppException into an RFC 9457
// ProblemDetails response with the right status code. See AGENTS.md.

namespace Taskboard.Api.Errors;

/// <summary>An error that carries the HTTP status and machine-readable code to report.</summary>
public class AppException : Exception
{
    /// <summary>Code reported when none is supplied.</summary>
    public const string DefaultCode = "INTERNAL_ERROR";

    /// <summary>HTTP status code to respond with.</summary>
    public int StatusCode { get; } = StatusCodes.Status500InternalServerError;

    /// <summary>Stable machine-readable code, SCREAMING_SNAKE_CASE.</summary>
    public string Code { get; } = DefaultCode;

    /// <summary>Optional structured detail, serialised into the problem document.</summary>
    public object? Details { get; }

    /// <summary>Creates a 500 INTERNAL_ERROR.</summary>
    public AppException()
        : this("Internal server error")
    {
    }

    /// <summary>Creates a 500 INTERNAL_ERROR with the given message.</summary>
    public AppException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a 500 INTERNAL_ERROR wrapping an underlying exception.</summary>
    public AppException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with an explicit status, code and detail.</summary>
    public AppException(
        string message,
        int statusCode,
        string code,
        object? details = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        Details = details;
    }

    /// <summary>400 - the request was malformed or failed validation.</summary>
    public static AppException BadRequest(string message, object? details = null) =>
        new(message, StatusCodes.Status400BadRequest, "BAD_REQUEST", details);

    /// <summary>401 - the caller is not authenticated.</summary>
    public static AppException Unauthorized(string message = "Unauthorized") =>
        new(message, StatusCodes.Status401Unauthorized, "UNAUTHORIZED");

    /// <summary>403 - the caller is authenticated but not permitted.</summary>
    public static AppException Forbidden(string message = "Forbidden") =>
        new(message, StatusCodes.Status403Forbidden, "FORBIDDEN");

    /// <summary>404 - the addressed resource does not exist.</summary>
    public static AppException NotFound(string message = "Not found", object? details = null) =>
        new(message, StatusCodes.Status404NotFound, "NOT_FOUND", details);

    /// <summary>409 - the request conflicts with current state.</summary>
    public static AppException Conflict(string message, object? details = null) =>
        new(message, StatusCodes.Status409Conflict, "CONFLICT", details);

    /// <summary>422 - well-formed but semantically unprocessable.</summary>
    public static AppException Unprocessable(string message, object? details = null) =>
        new(message, StatusCodes.Status422UnprocessableEntity, "UNPROCESSABLE_ENTITY", details);

    /// <summary>500 - an unexpected failure.</summary>
    public static AppException Internal(string message = "Internal server error", Exception? innerException = null) =>
        new(message, StatusCodes.Status500InternalServerError, DefaultCode, details: null, innerException);
}
