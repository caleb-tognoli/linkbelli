using System.Security.Cryptography;
using System.Text;
using Linkbelli.Api.Auth;
using Linkbelli.Application.Data;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Api.Common;

/// <summary>
/// Makes a POST safe to retry when the caller supplies an <c>Idempotency-Key</c>.
/// </summary>
/// <remarks>
/// A scripted client whose request times out cannot tell "never arrived" from "arrived, and the
/// reply was lost". Without this, retrying was a coin flip between a duplicate and a missing row.
/// Opt-in by header: requests without one behave exactly as they always did.
/// </remarks>
public sealed class IdempotencyFilter : IEndpointFilter
{
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Longest key accepted. A UUID is 36; anything much longer is not a key.</summary>
    private const int MaxKeyLength = 200;

    /// <summary>Bodies larger than this are not replayed — only the status is.</summary>
    private const int MaxStoredResponse = 64 * 1024;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (!HttpMethods.IsPost(http.Request.Method)
            || !http.Request.Headers.TryGetValue(HeaderName, out var header))
        {
            return await next(context);
        }

        var key = header.ToString().Trim();
        if (key.Length is 0 or > MaxKeyLength)
        {
            return Results.Problem(
                $"{HeaderName} must be between 1 and {MaxKeyLength} characters.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Keys are per caller: two clients picking the same UUID must not collide, and an
        // anonymous request has nobody to scope a key to.
        if (http.User.Identity?.IsAuthenticated != true)
        {
            return await next(context);
        }

        var userId = http.User.GetUserId();
        var db = http.RequestServices.GetRequiredService<IAppDbContext>();
        var endpoint = $"{http.Request.Method} {http.Request.Path}";
        var hash = await HashBodyAsync(http);

        var existing = await db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.UserId == userId && r.Key == key, http.RequestAborted);

        if (existing is not null)
        {
            if (existing.Endpoint != endpoint || existing.RequestHash != hash)
            {
                // The same key with a different request is a client bug. Replaying the first
                // answer would hide it and silently drop the second request.
                return Results.Problem(
                    $"This {HeaderName} was already used for a different request.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            if (existing.StatusCode is null)
            {
                // The first attempt is still running. Answering now would race it.
                return Results.Problem(
                    "The original request is still being processed. Try again shortly.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            http.Response.Headers["Idempotent-Replay"] = "true";
            return existing.Response is null
                ? Results.StatusCode(existing.StatusCode.Value)
                : Results.Content(existing.Response, "application/json", statusCode: existing.StatusCode.Value);
        }

        var record = new IdempotencyRecord
        {
            Key = key,
            UserId = userId,
            Endpoint = endpoint,
            RequestHash = hash,
        };

        db.IdempotencyRecords.Add(record);

        try
        {
            await db.SaveChangesAsync(http.RequestAborted);
        }
        catch (DbUpdateException)
        {
            // Two retries arrived at once and the unique index caught the second. That is the
            // same situation as finding one in flight above.
            return Results.Problem(
                "The original request is still being processed. Try again shortly.",
                statusCode: StatusCodes.Status409Conflict);
        }

        // The response is captured by running the endpoint's own result into a buffer, rather
        // than by reaching into each result type by hand and getting it wrong for the next one
        // somebody adds. Only requests carrying a key are buffered at all.
        var original = http.Response.Body;
        using var buffer = new MemoryStream();
        http.Response.Body = buffer;

        string body;
        int status;
        try
        {
            if (await next(context) is IResult produced)
            {
                await produced.ExecuteAsync(http);
            }

            status = http.Response.StatusCode;
            buffer.Position = 0;
            body = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();
        }
        catch
        {
            // The request threw, so the exception handler upstream decides what the caller sees
            // and this filter never learns what that was. Release the key rather than leave it
            // claimed forever: nothing was completed, so a retry should be free to try again.
            http.Response.Body = original;
            await ReleaseAsync(db, record);
            throw;
        }
        finally
        {
            http.Response.Body = original;
        }

        if (body.Length > 0)
        {
            await original.WriteAsync(Encoding.UTF8.GetBytes(body), http.RequestAborted);
        }

        if (IsWorthRetrying(status))
        {
            // Nothing was settled, so the key goes back rather than being bound to this answer.
            // The same reasoning as the catch above, which already releases on a thrown request.
            await ReleaseAsync(db, record);
        }
        else
        {
            await StoreAsync(db, record, status, body, http);
        }

        // Already written. Returning the endpoint's result here would send the whole thing twice.
        return Results.Empty;
    }

    /// <summary>
    /// Gives a key back after a request that never finished.
    /// </summary>
    /// <remarks>
    /// Deleted with a statement rather than through the change tracker: every other delete in
    /// this application is soft, and a soft-deleted placeholder is still found by the lookup
    /// above — which would leave the key claimed forever, the exact thing this exists to avoid.
    /// </remarks>
    private static async Task ReleaseAsync(IAppDbContext db, IdempotencyRecord record)
    {
        try
        {
            await db.IdempotencyRecords
                .Where(r => r.Id == record.Id)
                .ExecuteDeleteAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Nothing useful to do here, and the request's own failure is what matters. The key
            // expires on its own.
        }
    }

    /// <summary>
    /// Whether this answer means "not now" rather than "no".
    /// </summary>
    /// <remarks>
    /// The header exists so a client whose request timed out can send it again safely. Storing a
    /// transient failure against the key defeated exactly that: the first attempt hit a quota, a
    /// restart or a bad minute, and every retry for the next day was handed the same error back
    /// without the endpoint ever running again. A well-behaved client — one that reuses the key,
    /// which is the whole contract — could never succeed.
    ///
    /// 409 is deliberately not here. A conflict is a real answer about the state of the world,
    /// and replaying it is correct.
    /// </remarks>
    private static bool IsWorthRetrying(int status) =>
        status is StatusCodes.Status408RequestTimeout
            or StatusCodes.Status429TooManyRequests
            or >= StatusCodes.Status500InternalServerError;

    /// <summary>
    /// Records what the endpoint answered. A failure to record must not fail the request — the
    /// work is already done, and the caller would then retry something that had succeeded.
    /// </summary>
    private static async Task StoreAsync(
        IAppDbContext db, IdempotencyRecord record, int status, string body, HttpContext http)
    {
        try
        {
            record.StatusCode = status;
            record.Response = body.Length is > 0 and <= MaxStoredResponse ? body : null;
            record.CompletedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            http.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger<IdempotencyFilter>()
                .LogWarning(ex, "Could not record the result of an idempotent request.");
        }
    }

    /// <summary>
    /// Hashes the body, leaving the stream where it was found.
    /// </summary>
    /// <remarks>
    /// Reads a stream that <see cref="EnableBuffering"/> has already made rewindable. Parameter
    /// binding runs before endpoint filters do, so a body that was not buffered up front has
    /// already been consumed by the time this is reached — and every request would hash
    /// identically, which quietly turns off the mismatch check.
    /// </remarks>
    private static async Task<string> HashBodyAsync(HttpContext http)
    {
        if (!http.Request.Body.CanSeek)
        {
            return "unbuffered";
        }

        var position = http.Request.Body.Position;
        http.Request.Body.Position = 0;

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(http.Request.Body, http.RequestAborted);

        http.Request.Body.Position = position;

        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Makes the body re-readable for requests that will need it hashed. Must run before routing,
    /// because parameter binding consumes the stream before any filter sees it.
    /// </summary>
    public static Task EnableBuffering(HttpContext context, RequestDelegate next)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Headers.ContainsKey(HeaderName))
        {
            context.Request.EnableBuffering();
        }

        return next(context);
    }
}
