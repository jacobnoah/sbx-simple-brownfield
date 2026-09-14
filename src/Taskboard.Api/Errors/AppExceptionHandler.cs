// Terminal exception handling.
//
// SHARED FILE - read-only for feature agents.
//
// Turns anything thrown out of an endpoint into an RFC 9457 ProblemDetails
// response. An AppException reports its own status and code; anything else
// becomes an opaque 500 so internal detail never reaches the caller.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Taskboard.Api.Errors;

/// <summary>Writes a ProblemDetails body for every unhandled exception.</summary>
internal sealed class AppExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>Extension member carrying the stable machine-readable error code.</summary>
    public const string CodeExtension = "code";

    /// <summary>Extension member carrying optional structured detail.</summary>
    public const string DetailsExtension = "details";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var appException = exception as AppException;
        var statusCode = appException?.StatusCode ?? StatusCodes.Status500InternalServerError;
        var code = appException?.Code ?? AppException.DefaultCode;

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled {Code} on {Method} {Path}",
                code, httpContext.Request.Method, httpContext.Request.Path);
        }

        // An unexpected exception must not leak its message to the caller.
        var detail = appException is not null
            ? appException.Message
            : "An unexpected error occurred.";

        httpContext.Response.StatusCode = statusCode;

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrase(statusCode),
            Detail = detail,
            Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        };

        problemDetails.Extensions[CodeExtension] = code;

        if (appException?.Details is { } details)
        {
            problemDetails.Extensions[DetailsExtension] = details;
        }

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problemDetails,
            Exception = exception,
            AdditionalMetadata = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint?.Metadata,
        }).ConfigureAwait(false);
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        _ => "Internal Server Error",
    };
}
