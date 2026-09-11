using Linkbelli.Application.Common;
using Linkbelli.Core.Entities;

namespace Linkbelli.Application.Sources;

/// <summary>
/// Links handed to a webhook source by the request that is running it.
/// </summary>
/// <remarks>
/// Scoped: one push, one buffer. This exists so a pushed source travels exactly the same road as
/// a polled one — same quota, same filter, same run row, same dedup — instead of growing a second
/// ingestion path that quietly behaves differently.
/// </remarks>
public sealed class PushedLinks
{
    public IReadOnlyList<DiscoveredLink> Links { get; private set; } = [];

    public void Set(IReadOnlyList<DiscoveredLink> links) => Links = links;
}

/// <summary>
/// A source that is pushed to rather than polled: n8n, a Zap, a GitHub Action, a shell script.
/// </summary>
/// <remarks>
/// Every other source type asks a schedule to go and look. This one waits, which means new links
/// arrive when they happen rather than up to an hour later, and a feed nobody can subscribe to is
/// still reachable.
/// </remarks>
public sealed class WebhookSourceInterpreter(PushedLinks pushed) : ISourceInterpreter
{
    /// <summary>The config key holding the secret that stands in for the URL.</summary>
    public const string TokenKey = "token";

    public SourceType Type => SourceType.Webhook;

    public void ValidateConfig(IReadOnlyDictionary<string, string> config)
    {
        if (!config.TryGetValue(TokenKey, out var token) || string.IsNullOrWhiteSpace(token))
        {
            throw new ValidationException($"config.{TokenKey}", "A webhook source needs a token.");
        }
    }

    /// <summary>Secret, so the endpoint that receives a push needs no other credential.</summary>
    public bool IsSecretConfigKey(string key) => key == TokenKey;

    public Task<SourceFetchResult> FetchAsync(
        IReadOnlyDictionary<string, string> config, string? state, CancellationToken cancellationToken = default) =>
        // Nothing to fetch: whatever was pushed is already here, and a scheduled run of a webhook
        // source correctly finds nothing.
        Task.FromResult(new SourceFetchResult(pushed.Links));
}
