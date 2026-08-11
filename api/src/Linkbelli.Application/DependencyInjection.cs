using System.Net;
using System.Net.Http;
using Linkbelli.Application.Auth;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Http;
using Linkbelli.Application.Services;
using Linkbelli.Application.Sources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Linkbelli.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<QuotaOptions>(configuration.GetSection("Quota"));
        services.AddScoped<ILinkService, LinkService>();
        services.AddScoped<IPlaylistService, PlaylistService>();
        services.AddScoped<IPlaylistItemService, PlaylistItemService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();
        services.AddScoped<IUserQuotaService, UserQuotaService>();
        services.AddScoped<IUserPreferenceService, UserPreferenceService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IImportService, ImportService>();

        // --- Link enrichment --- (ILinkEnrichmentQueue is implemented in Infrastructure via Hangfire)
        services.AddSingleton<LinkMetadataExtractor>();
        services.AddSingleton<LinkMetadataFetcher>();
        services.AddSingleton<IHostThrottle, HostThrottle>();
        services.AddScoped<ILinkEnricher, LinkEnricher>();

        // --- Sources --- (ISourceScheduler / ISecretProtector are implemented in Infrastructure)
        services.AddScoped<ISourceService, SourceService>();
        services.AddScoped<ISourceRunner, SourceRunner>();
        services.AddScoped<SourceConfigSecrets>();
        services.AddScoped<ISourceInterpreter, RssSourceInterpreter>();
        services.AddScoped<ISourceInterpreter, ScraperSourceInterpreter>();
        services.AddScoped<ISourceInterpreter, JsonApiSourceInterpreter>();

        // SSRF-protected outbound client: connects only to public IPs (validated per hop).
        services.AddHttpClient(EnrichmentHttpClient.Name, client =>
            {
                client.Timeout = EnrichmentHttpClient.Timeout;
                client.MaxResponseContentBufferSize = EnrichmentHttpClient.MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(EnrichmentHttpClient.UserAgent);
                // Force HTTP/1.1 — Cloudflare fingerprints .NET's HTTP/2 handshake as bot-like
                // and 429s the request even before it sees the User-Agent. HTTP/1.1 looks like a
                // plain curl/browser and gets through.
                client.DefaultRequestVersion = HttpVersion.Version11;
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
                // Ask locale-aware sites (e.g. themoviedb.org) for English HTML so scraped OG tags
                // don't come back in whatever language the origin geo-guesses for our server.
                client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
                // Browser-typical headers so Cloudflare/edge fingerprinting doesn't flag the
                // request as automated based on their absence.
                client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8");
                client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
                client.DefaultRequestHeaders.Connection.ParseAdd("keep-alive");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = EnrichmentHttpClient.MaxRedirects,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = EnrichmentHttpClient.ConnectTimeout,
                ConnectCallback = SsrfProtection.ConnectCallback,
            });

        return services;
    }
}
