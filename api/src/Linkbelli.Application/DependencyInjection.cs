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

        // --- Link enrichment --- (ILinkEnrichmentQueue is implemented in Infrastructure via Hangfire)
        services.AddSingleton<LinkMetadataExtractor>();
        services.AddSingleton<LinkMetadataFetcher>();
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
