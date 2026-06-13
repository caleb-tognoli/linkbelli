using System.Net;
using System.Net.Http;
using Linkbelli.Application.Auth;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Http;
using Linkbelli.Application.Services;
using Linkbelli.Application.Sources;
using Microsoft.Extensions.DependencyInjection;

namespace Linkbelli.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ILinkService, LinkService>();
        services.AddScoped<IPlaylistService, PlaylistService>();
        services.AddScoped<IPlaylistItemService, PlaylistItemService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IApiKeyAuthenticator, ApiKeyAuthenticator>();

        // --- Link enrichment --- (ILinkEnrichmentQueue is implemented in Infrastructure via Hangfire)
        services.AddSingleton<LinkMetadataExtractor>();
        services.AddScoped<ILinkEnricher, LinkEnricher>();

        // --- Sources --- (ISourceScheduler is implemented in Infrastructure via Hangfire)
        services.AddScoped<ISourceService, SourceService>();
        services.AddScoped<ISourceRunner, SourceRunner>();
        services.AddScoped<ISourceInterpreter, RssSourceInterpreter>();

        // SSRF-protected outbound client: connects only to public IPs (validated per hop).
        services.AddHttpClient(EnrichmentHttpClient.Name, client =>
            {
                client.Timeout = EnrichmentHttpClient.Timeout;
                client.MaxResponseContentBufferSize = EnrichmentHttpClient.MaxResponseBytes;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(EnrichmentHttpClient.UserAgent);
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
