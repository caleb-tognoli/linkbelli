using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Common;

/// <summary>The requested resource does not exist (or isn't visible to the caller). Maps to 404.</summary>
public class NotFoundException(string message = "Resource not found.") : Exception(message);

/// <summary>The request conflicts with current state (duplicate, concurrent edit). Maps to 409.</summary>
public class ConflictException(string message) : Exception(message);

/// <summary>
/// A write lost a race for a unique value.
/// </summary>
/// <remarks>
/// Deliberately still a <see cref="DbUpdateException"/>: several services already catch that to
/// settle a race themselves (get-or-create on Host, Link and Tag), and they should keep working
/// without knowing this type exists. What it adds is the constraint's name, so a caller that can
/// only settle one kind of collision can tell whether this is that kind — and so the API can
/// answer 409 rather than 500 for every other kind.
///
/// Translated in Infrastructure, the only layer that knows what a Postgres error code is. The
/// name is carried as a string so Application stays free of the provider.
/// </remarks>
public class UniqueConstraintException(string? constraintName, Exception? inner)
    : DbUpdateException("That conflicts with something that already exists.", inner)
{
    public string? ConstraintName { get; } = constraintName;

    /// <summary>Whether the violated index looks like the one the caller means.</summary>
    public bool Involves(string fragment) =>
        ConstraintName?.Contains(fragment, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>A per-user quota would be exceeded. Maps to 429.</summary>
public class QuotaExceededException(string message) : Exception(message);

/// <summary>The link's host is on the moderation blocklist. Maps to 403.</summary>
public class BlockedHostException(string message) : Exception(message);

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
