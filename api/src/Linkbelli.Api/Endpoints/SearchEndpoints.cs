using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Services;
using Linkbelli.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Search across everything the caller owns. The per-playlist search answers "where in this
/// list is it"; this answers "where did I put it".
/// </summary>
public static class SearchEndpoints
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/search")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Search");

        group.MapGet("/", async (
            ClaimsPrincipal user, ISearchService svc,
            string? q, string? host, string[]? tag, string? status, int? minScore,
            DateTimeOffset? finishedSince, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.SearchAsync(
                user.GetUserId(),
                new SearchQuery(q, host, tag, status, minScore, finishedSince, limit, cursor),
                ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("Search");

        // Facets for a host filter: the sites the caller actually saves from.
        group.MapGet("/hosts", async (
            ClaimsPrincipal user, ISearchService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListHostsAsync(user.GetUserId(), q, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("SearchHosts");
    }
}
