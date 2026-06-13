using Linkbelli.Application.Common;
using Microsoft.AspNetCore.Diagnostics;

namespace Linkbelli.Api.Common;

/// <summary>Maps Application-layer exceptions to ProblemDetails responses.</summary>
public sealed class AppExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        IResult? result = exception switch
        {
            NotFoundException => Results.Problem(exception.Message, statusCode: StatusCodes.Status404NotFound),
            ConflictException => Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict),
            ValidationException validation => Results.ValidationProblem(validation.Errors),
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
