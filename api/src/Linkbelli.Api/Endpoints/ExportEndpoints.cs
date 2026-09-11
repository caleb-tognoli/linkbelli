using System.Security.Claims;
using System.Text;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Export;
using Microsoft.AspNetCore.Authorization;

namespace Linkbelli.Api.Endpoints;

/// <summary>
/// Data portability: hand a user their own links back. Import has existed since the CSV
/// importer; this is the way out, in the formats other tools actually read.
/// </summary>
public static class ExportEndpoints
{
    public static void MapExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/export")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = AuthSchemes.BearerOrApiKey })
            .WithTags("Export");

        group.MapGet("/", async (
            string? format, ClaimsPrincipal user, IExportService svc, CancellationToken ct) =>
        {
            if (ExportFormats.Parse(format) is not { } parsed)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["format"] = ["Use one of: json, csv, html, opml."],
                });
            }

            var bundle = await svc.ExportAllAsync(user.GetUserId(), ct);
            return Download(bundle, parsed, $"linkbelli-{bundle.Username}");
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ExportEverything");

        group.MapGet("/playlists/{id:guid}", async (
            Guid id, string? format, ClaimsPrincipal user, IExportService svc, CancellationToken ct) =>
        {
            if (ExportFormats.Parse(format) is not { } parsed)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["format"] = ["Use one of: json, csv, html, opml."],
                });
            }

            var bundle = await svc.ExportPlaylistAsync(user.GetUserId(), id, ct);
            var name = bundle.Playlists.FirstOrDefault()?.Slug ?? "playlist";
            return Download(bundle, parsed, $"linkbelli-{name}");
        })
            .RequireAuthorization(Scopes.Policy(Scopes.PlaylistsRead))
            .WithName("ExportPlaylist");
    }

    /// <summary>
    /// Served as an attachment with a dated filename — an export is a file someone saves, and a
    /// browser opening a 40 000-line JSON dump in a tab helps nobody.
    /// </summary>
    private static IResult Download(ExportBundle bundle, ExportFormat format, string baseName)
    {
        var body = ExportSerializer.Serialize(bundle, format);
        var filename = $"{Sanitize(baseName)}-{bundle.ExportedAt:yyyy-MM-dd}.{ExportFormats.FileExtension(format)}";

        return Results.File(
            Encoding.UTF8.GetBytes(body),
            ExportFormats.ContentType(format),
            filename);
    }

    /// <summary>Keeps a username or slug safe to put in a Content-Disposition filename.</summary>
    private static string Sanitize(string value)
    {
        var cleaned = new string(value.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray())
            .Trim('-');

        return cleaned.Length == 0 ? "linkbelli" : cleaned;
    }
}
