using Linkbelli.Application.Common;
using Linkbelli.Application.Observability;
using Microsoft.AspNetCore.Diagnostics;

namespace Linkbelli.Api.Common;

/// <summary>Maps Application-layer exceptions to ProblemDetails responses.</summary>
public sealed class AppExceptionHandler(AppMetrics metrics) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is QuotaExceededException)
        {
            // Every quota refusal passes through here, which makes it the one place worth
            // counting them: a quota set too low is otherwise invisible until somebody complains.
            metrics.QuotaRejected();
        }

        IResult? result = exception switch
        {
            NotFoundException => Results.Problem(exception.Message, statusCode: StatusCodes.Status404NotFound),
            ConflictException => Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict),
            QuotaExceededException => Results.Problem(exception.Message, statusCode: StatusCodes.Status429TooManyRequests),
            BlockedHostException => Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden),
            ValidationException validation => Results.ValidationProblem(validation.Errors),

            // A body that could not be bound is the caller's mistake, not ours. Without this the
            // framework's own exception carries a 400 and then gets reported as a 500, which is
            // wrong on the wire and turns every malformed request into a server error in the log.
            BadHttpRequestException bad => Results.Problem(bad.Message, statusCode: bad.StatusCode),

            // Two writers raced for the same unique value. Whoever lost should be told to look
            // again rather than shown a 500: the row they wanted exists, it just isn't theirs.
            // Services that can resolve the race themselves catch it first; this is the backstop
            // for the ones that cannot, so a unique index can never surface as a server error.
            UniqueConstraintException => Results.Problem(
                "That conflicts with something that already exists. Reload and try again.",
                statusCode: StatusCodes.Status409Conflict),

            _ => null,
        };

        if (result is null)
        {
            return false; // not ours — let the default handler produce a 500
        }

        await result.ExecuteAsync(httpContext);
        return true;
    }
}
