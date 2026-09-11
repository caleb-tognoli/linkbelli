namespace Linkbelli.Core.Entities;

/// <summary>
/// What happened the first time a client sent this request, so sending it again returns the same
/// answer instead of doing it twice.
/// </summary>
/// <remarks>
/// A scripted client that times out has no way to tell "the server never got it" from "the server
/// did it and the reply was lost". Without this, retrying a save was a coin flip between a
/// duplicate and a missing row.
/// </remarks>
public class IdempotencyRecord : BaseEntity<Guid>
{
    /// <summary>The client's own key, unique per caller.</summary>
    public required string Key { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Method and path, so one key cannot be reused across different requests.</summary>
    public required string Endpoint { get; set; }

    /// <summary>
    /// Hash of the body. The same key with a different body is a client bug, and answering it
    /// with the first request's result would hide the bug and lose the second request.
    /// </summary>
    public required string RequestHash { get; set; }

    /// <summary>Null while the first request is still running.</summary>
    public int? StatusCode { get; set; }

    /// <summary>The response body to replay, verbatim.</summary>
    public string? Response { get; set; }

    /// <summary>When the reply was recorded. Null while in flight.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// How long a key is remembered. Long enough to cover a retry loop and a person noticing a
    /// failure; short enough that the table stays small.
    /// </summary>
    public const int RetentionHours = 24;
}
