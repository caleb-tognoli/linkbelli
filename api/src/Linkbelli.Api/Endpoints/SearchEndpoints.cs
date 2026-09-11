using System.Security.Claims;
using Linkbelli.Api.Auth;
using Linkbelli.Api.Common;
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
            string? q, string? host, string[]? tag, string[]? itemTag, string? status, int? minScore,
            DateTimeOffset? finishedSince, bool? broken, string? sort, int? limit, string? cursor,
            CancellationToken ct) =>
            Results.Ok(await svc.SearchAsync(
                user.GetUserId(),
                new SearchQuery(q, host, tag, itemTag, status, minScore, finishedSince, broken, sort, limit, cursor),
                ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("Search");

        // Searches worth coming back to. What they match is whatever matches now, so a saved
        // search keeps up with the collection instead of freezing a list of ids.
        group.MapGet("/saved", async (ClaimsPrincipal user, ISearchService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListSavedAsync(user.GetUserId(), ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ListSavedSearches");

        group.MapPost("/saved", async (
            SaveSearchRequest req, ClaimsPrincipal user, ISearchService svc, CancellationToken ct) =>
        {
            var saved = await svc.SaveAsync(user.GetUserId(), req, ct);
            return Results.Created($"{ApiRoutes.V1}/search/saved/{saved.Id}", saved);
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("SaveSearch");

        group.MapGet("/saved/{id:guid}", async (
            Guid id, ClaimsPrincipal user, ISearchService svc, int? limit, string? cursor, CancellationToken ct) =>
            Results.Ok(await svc.RunSavedAsync(user.GetUserId(), id, limit, cursor, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("RunSavedSearch");

        group.MapDelete("/saved/{id:guid}", async (
            Guid id, ClaimsPrincipal user, ISearchService svc, CancellationToken ct) =>
        {
            await svc.DeleteSavedAsync(user.GetUserId(), id, ct);
            return Results.NoContent();
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsWrite))
            .WithName("DeleteSavedSearch");

        // Facets for a host filter: the sites the caller actually saves from.
        group.MapGet("/hosts", async (
            ClaimsPrincipal user, ISearchService svc, string? q, CancellationToken ct) =>
            Results.Ok(await svc.ListHostsAsync(user.GetUserId(), q, ct)))
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("SearchHosts");
    }
}
