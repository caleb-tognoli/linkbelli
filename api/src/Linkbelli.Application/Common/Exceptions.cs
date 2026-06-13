namespace Linkbelli.Application.Common;

/// <summary>The requested resource does not exist (or isn't visible to the caller). Maps to 404.</summary>
public class NotFoundException(string message = "Resource not found.") : Exception(message);

/// <summary>The request conflicts with current state (duplicate, concurrent edit). Maps to 409.</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>A per-user quota would be exceeded. Maps to 429.</summary>
public class QuotaExceededException(string message) : Exception(message);

/// <summary>Input validation failed. Maps to 400 ProblemDetails with field errors.</summary>
public class ValidationException(IDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public ValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { [field] = [error] })
    {
    }
}
