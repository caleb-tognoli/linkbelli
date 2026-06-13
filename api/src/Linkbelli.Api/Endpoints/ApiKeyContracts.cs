namespace Linkbelli.Api.Endpoints;

public record CreateApiKeyRequest(string Name, string[]? Scopes, DateTimeOffset? ExpiresAt);

/// <summary>Returned once at creation — the only time the full Token is ever exposed.</summary>
public record CreateApiKeyResponse(
    Guid Id, string Name, string Prefix, string Token, string[] Scopes, DateTimeOffset? ExpiresAt);

public record ApiKeyResponse(
    Guid Id, string Name, string Prefix, string[] Scopes,
    DateTimeOffset CreationTime, DateTimeOffset? LastUsedAt, DateTimeOffset? ExpiresAt);
