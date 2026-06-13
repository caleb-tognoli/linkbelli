namespace Linkbelli.Application.Http;

/// <summary>Shared name + limits for the SSRF-protected outbound HttpClient.</summary>
public static class EnrichmentHttpClient
{
    public const string Name = "enrichment";
    public const int MaxResponseBytes = 5 * 1024 * 1024; // 5 MB
    public const string UserAgent = "LinkbelliBot/1.0 (+https://linkbelli)";
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    public const int MaxRedirects = 5;
}
