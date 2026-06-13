using Linkbelli.Application.Auth;
using Linkbelli.Application.Services;
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
        return services;
    }
}
