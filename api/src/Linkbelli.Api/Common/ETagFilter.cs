using System.Security.Cryptography;
using Microsoft.Net.Http.Headers;

namespace Linkbelli.Api.Common;

/// <summary>
/// Answers an unchanged GET with <c>304 Not Modified</c> instead of the same body again.
/// </summary>
/// <remarks>
/// Everything that polls this API — the extension, the sync client, a feed reader — re-downloaded
/// an identical payload every time it looked. The tag is derived from the response itself, so it
/// is right by construction rather than by remembering to bump a version somewhere.
/// </remarks>
public sealed class ETagFilter : IEndpointFilter
{
    /// <summary>
    /// Responses larger than this are sent without a tag, and without being buffered to find out.
    /// </summary>
    /// <remarks>
    /// It was two megabytes, which is not a size anybody re-validates — it is a size you download
    /// once. Every export, backup and long search page was therefore copied into memory in full,
    /// hashed, and copied again by <c>ToArray</c>, to produce a header that was then thrown away
    /// because the body was too big to tag. A page of JSON is tens of kilobytes; this is well
    /// clear of that and far below the point where buffering costs more than it saves.
    /// </remarks>
    private const int MaxHashedBytes = 512 * 1024;

    /// <summary>
    /// What is worth tagging.
    /// </summary>
    /// <remarks>
    /// JSON is what clients poll — the extension, the sync client, a feed reader — and what is
    /// cheap to re-render on a 304. Exports, backups, thumbnails and feeds each have their own
    /// caching story and are large; buffering them to tag them is pure cost.
    /// </remarks>
    private static bool IsTaggable(HttpResponse response)
    {
        // A download is fetched once and saved, not polled. A JSON export is still JSON, so the
        // content type alone would buffer and hash the whole of somebody's library to produce a
        // tag nothing will ever send back.
        if (response.Headers.ContainsKey(HeaderNames.ContentDisposition))
        {
            return false;
        }

        var contentType = response.ContentType;
        return contentType is not null
            && (contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)
                || contentType.StartsWith("application/problem+json", StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!HttpMethods.IsGet(http.Request.Method))
        {
            return await next(context);
        }

        var original = http.Response.Body;
        using var buffer = new ETagBufferStream(
            original, MaxHashedBytes, () => IsTaggable(http.Response));
        http.Response.Body = buffer;

        object? result;
        try
        {
            result = await next(context);
            if (result is IResult produced)
            {
                await produced.ExecuteAsync(http);
            }
        }
        finally
        {
            http.Response.Body = original;
        }

        // Already on the wire: either it was never a candidate, or it outgrew the cap part way
        // through. Nothing left to decide.
        if (buffer.PassedThrough)
        {
            return Results.Empty;
        }

        var body = buffer.Buffered;

        // Only successful reads are tagged. A 404 or a 500 is not a representation of anything,
        // and caching one under an entity tag is how a transient failure becomes a sticky one.
        if (http.Response.StatusCode is not (>= 200 and < 300) || body.Length == 0)
        {
            await original.WriteAsync(body, http.RequestAborted);
            return Results.Empty;
        }

        // Weak, because this is a byte-for-byte comparison of one serialization of the resource
        // rather than a claim about the resource itself.
        var etag = $"W/\"{Convert.ToHexStringLower(SHA256.HashData(body.Span))[..32]}\"";
        http.Response.Headers.ETag = etag;

        // A validator on a response with no cacheability directives is an invitation: RFC 9111
        // lets a shared cache store it heuristically and hand it to the next caller. Endpoints
        // that set their own Cache-Control (thumbnails, which are deliberately public) keep it.
        if (!http.Response.Headers.ContainsKey(HeaderNames.CacheControl))
        {
            http.Response.Headers.CacheControl = http.User.Identity?.IsAuthenticated == true
                ? "private, no-cache"
                : "no-cache";
        }

        // Whichever credential was used changes the answer, so anything caching this has to key
        // on it rather than on the URL alone.
        http.Response.Headers.Vary = "Authorization, X-Api-Key, Cookie";

        if (Matches(http.Request.Headers[HeaderNames.IfNoneMatch], etag))
        {
            // A 304 carries no body, and must not claim one.
            http.Response.StatusCode = StatusCodes.Status304NotModified;
            http.Response.ContentLength = null;
            http.Response.Headers.Remove(HeaderNames.ContentType);
            return Results.Empty;
        }

        http.Response.ContentLength = body.Length;
        await original.WriteAsync(body, http.RequestAborted);

        return Results.Empty;
    }

    /// <summary>
    /// Whether the client already holds this. <c>If-None-Match</c> may list several tags, and
    /// <c>*</c> means "anything you have".
    /// </summary>
    private static bool Matches(string? header, string etag)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        foreach (var candidate in header.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (candidate == "*" || candidate == etag)
            {
                return true;
            }

            // A proxy or client that stripped the weakness marker is still talking about the same
            // representation, and refusing to recognise it would just resend the body.
            if (candidate.TrimStart('W', '/') == etag.TrimStart('W', '/'))
            {
                return true;
            }
        }

        return false;
    }
}
