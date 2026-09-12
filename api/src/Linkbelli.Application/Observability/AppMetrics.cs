using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Linkbelli.Application.Observability;

/// <summary>
/// The things worth counting about what this instance actually does.
/// </summary>
/// <remarks>
/// Built on the BCL's own metrics types rather than on any particular exporter, so the layer that
/// records a number does not have to know where it eventually goes. Observability here was one
/// health check and Hangfire's dashboard: enough to tell whether the process was alive, and
/// nothing about whether it was doing its job.
/// </remarks>
public sealed class AppMetrics : IDisposable
{
    /// <summary>The meter name to subscribe an exporter to.</summary>
    public const string MeterName = "Linkbelli";

    /// <summary>The activity source name, for tracing.</summary>
    public const string ActivitySourceName = "Linkbelli";

    private readonly Meter _meter = new(MeterName);

    public static readonly ActivitySource Activity = new(ActivitySourceName);

    private readonly Counter<long> _enrichment;
    private readonly Counter<long> _sourceRuns;
    private readonly Counter<long> _itemsDiscovered;
    private readonly Counter<long> _archiveAttempts;
    private readonly Histogram<double> _enrichmentDuration;
    private readonly Counter<long> _mail;
    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _quotaRejections;

    public AppMetrics()
    {
        _enrichment = _meter.CreateCounter<long>(
            "linkbelli.enrichment.attempts",
            unit: "{attempt}",
            description: "Pages fetched for metadata, by how it went.");

        _enrichmentDuration = _meter.CreateHistogram<double>(
            "linkbelli.enrichment.duration",
            unit: "ms",
            description: "How long fetching a page took, including the per-host wait.");

        _sourceRuns = _meter.CreateCounter<long>(
            "linkbelli.source.runs",
            unit: "{run}",
            description: "Source runs, by outcome.");

        _itemsDiscovered = _meter.CreateCounter<long>(
            "linkbelli.source.links",
            unit: "{link}",
            description: "Links a source run found or added.");

        _archiveAttempts = _meter.CreateCounter<long>(
            "linkbelli.archive.attempts",
            unit: "{attempt}",
            description: "Snapshots asked for, by whether one came back.");

        _mail = _meter.CreateCounter<long>(
            "linkbelli.mail.sent",
            unit: "{message}",
            description: "Messages handed to the provider, by kind and by whether it took them.");

        _rateLimited = _meter.CreateCounter<long>(
            "linkbelli.ratelimit.rejections",
            unit: "{request}",
            description: "Requests refused by the limiter, by which policy refused them.");

        _quotaRejections = _meter.CreateCounter<long>(
            "linkbelli.quota.rejections",
            unit: "{request}",
            description: "Requests refused for exceeding a per-user quota.");
    }

    /// <summary>
    /// One message. Mail is optional in this product, which is exactly why its failures need
    /// counting: an instance where sending quietly stopped working looks identical to one where
    /// nobody has subscribed to anything.
    /// </summary>
    public void Mail(string kind, bool sent) =>
        _mail.Add(1,
            new KeyValuePair<string, object?>("kind", kind),
            new KeyValuePair<string, object?>("outcome", sent ? "sent" : "failed"));

    /// <summary>
    /// One request the limiter refused.
    /// </summary>
    /// <remarks>
    /// Tagged by policy and not by partition: the partition is a user id or an address, which is
    /// unbounded and is the caller's identity rather than a property of the system. The policy is
    /// what you change when this number is wrong — which is how the thumbnail limit went unnoticed
    /// until somebody looked at a page and saw the pictures missing.
    /// </remarks>
    public void RateLimited(string policy) =>
        _rateLimited.Add(1, new KeyValuePair<string, object?>("policy", policy));

    /// <summary>One request refused for exceeding a quota.</summary>
    public void QuotaRejected() => _quotaRejections.Add(1);

    /// <summary>One page fetch. <paramref name="outcome"/> is "succeeded", "failed" or "broken".</summary>
    /// <remarks>
    /// The host is deliberately not a dimension on either instrument. It was on the counter, with
    /// a comment explaining why it could not be on the histogram — the same reasoning applies to
    /// both: this is an application for collecting links from everywhere, so the host set grows
    /// without bound and every distinct site became a permanent time series.
    ///
    /// Which site is failing is a real question, and it is answered from the Links table, where
    /// EnrichmentStatus and EnrichmentError are already stored per link and already surfaced on
    /// the admin overview. That answer stays correct without retaining a series per hostname.
    /// </remarks>
    public void Enrichment(string outcome, double milliseconds)
    {
        _enrichment.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _enrichmentDuration.Record(milliseconds, new KeyValuePair<string, object?>("outcome", outcome));
    }

    /// <summary>One source run, with what it came back with.</summary>
    public void SourceRun(string outcome, string type, int found, int added, int skipped)
    {
        _sourceRuns.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("type", type));

        if (found > 0) _itemsDiscovered.Add(found, new KeyValuePair<string, object?>("disposition", "found"));
        if (added > 0) _itemsDiscovered.Add(added, new KeyValuePair<string, object?>("disposition", "added"));
        if (skipped > 0) _itemsDiscovered.Add(skipped, new KeyValuePair<string, object?>("disposition", "skipped"));
    }

    /// <summary>One archive attempt. "archived", "refused" or "deferred".</summary>
    public void Archive(string outcome) =>
        _archiveAttempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}
