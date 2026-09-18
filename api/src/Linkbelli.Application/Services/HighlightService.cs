using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Webhooks;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

/// <summary>Marking passages in the articles this app already keeps.</summary>
public interface IHighlightService
{
    /// <summary>Everything the caller has marked in one article, in reading order.</summary>
    Task<IReadOnlyList<HighlightResponse>> ListForLinkAsync(
        Guid ownerId, Guid linkId, CancellationToken ct = default);

    /// <summary>Everything the caller has ever marked, newest first.</summary>
    Task<PagedResult<HighlightWithSourceResponse>> ListAsync(
        Guid ownerId, string? cursor, int? limit, CancellationToken ct = default);

    Task<HighlightResponse> CreateAsync(
        Guid ownerId, Guid linkId, CreateHighlightRequest request, CancellationToken ct = default);

    Task<HighlightResponse> UpdateAsync(
        Guid ownerId, Guid id, UpdateHighlightRequest request, CancellationToken ct = default);

    Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default);
}

/// <inheritdoc />
public class HighlightService(IAppDbContext db, IWebhookEvents webhooks) : IHighlightService
{
    /// <summary>
    /// The most passages one article will hold.
    /// </summary>
    /// <remarks>
    /// Not a taste — the reader draws every one of these as a mark over the text on open, and a
    /// document with a thousand marks in it is not a document anybody is reading. Well past any
    /// honest use, low enough that a runaway client cannot fill the table through one article.
    /// </remarks>
    public const int MaxPerLink = 500;

    public async Task<IReadOnlyList<HighlightResponse>> ListForLinkAsync(
        Guid ownerId, Guid linkId, CancellationToken ct = default)
    {
        var paragraphs = await ParagraphsAsync(ownerId, linkId, ct);

        var rows = await db.Highlights
            .AsNoTracking()
            .Where(h => h.OwnerId == ownerId && h.LinkId == linkId)
            .OrderBy(h => h.ParagraphIndex).ThenBy(h => h.Start)
            .ToListAsync(ct);

        return [.. rows.Select(h => Describe(h, paragraphs))];
    }

    public async Task<PagedResult<HighlightWithSourceResponse>> ListAsync(
        Guid ownerId, string? cursor, int? limit, CancellationToken ct = default)
    {
        var take = Paging.Take(limit);
        var after = Cursor.DecodeTimeKey(cursor);

        var query = db.Highlights
            .AsNoTracking()
            .Where(h => h.OwnerId == ownerId);

        if (after is { } key)
        {
            query = query.Where(h =>
                h.CreationTime < key.At || (h.CreationTime == key.At && h.Id.CompareTo(key.Id) < 0));
        }

        var rows = await query
            .OrderByDescending(h => h.CreationTime).ThenByDescending(h => h.Id)
            .Take(take + 1)
            .Select(h => new KeyedRow<HighlightWithSourceResponse>(
                h.CreationTime,
                h.Id,
                new HighlightWithSourceResponse(
                    h.Id,
                    h.LinkId,
                    h.Link!.CanonicalUrl,
                    h.Link.Title,
                    h.Link.SiteName,
                    h.Link.Host!.Hostname,
                    h.Text,
                    h.Note,
                    h.ParagraphIndex,
                    h.CreationTime)))
            .ToListAsync(ct);

        return rows.ToPage(take);
    }

    public async Task<HighlightResponse> CreateAsync(
        Guid ownerId, Guid linkId, CreateHighlightRequest request, CancellationToken ct = default)
    {
        var paragraphs = await ParagraphsAsync(ownerId, linkId, ct);

        if (request.ParagraphIndex < 0 || request.ParagraphIndex >= paragraphs.Length)
        {
            throw new ValidationException("paragraphIndex", "That paragraph is not in this article.");
        }

        var paragraph = paragraphs[request.ParagraphIndex];
        if (request.Start < 0 || request.End > paragraph.Length || request.Start >= request.End)
        {
            throw new ValidationException("start", "That selection is not inside that paragraph.");
        }

        var text = paragraph[request.Start..request.End];
        if (text.Length > Highlight.MaxTextLength)
        {
            throw new ValidationException(
                "text", $"A highlight can be at most {Highlight.MaxTextLength:N0} characters.");
        }

        var note = Trimmed(request.Note);
        if (note?.Length > Highlight.MaxNoteLength)
        {
            throw new ValidationException(
                "note", $"A note can be at most {Highlight.MaxNoteLength:N0} characters.");
        }

        // The same passage marked twice is one passage. Handing back what is already there beats
        // a 409 the reader would have to translate into "you already did that".
        var existing = await db.Highlights.FirstOrDefaultAsync(
            h => h.OwnerId == ownerId
                && h.LinkId == linkId
                && h.ParagraphIndex == request.ParagraphIndex
                && h.Start == request.Start
                && h.End == request.End,
            ct);

        if (existing is not null)
        {
            if (note is not null && existing.Note != note)
            {
                existing.Note = note;
                await db.SaveChangesAsync(ct);
            }

            return Describe(existing, paragraphs);
        }

        var count = await db.Highlights.CountAsync(h => h.OwnerId == ownerId && h.LinkId == linkId, ct);
        if (count >= MaxPerLink)
        {
            throw new ValidationException(
                "highlights", $"There is a limit of {MaxPerLink} highlights on one article.");
        }

        var highlight = new Highlight
        {
            OwnerId = ownerId,
            LinkId = linkId,
            ParagraphIndex = request.ParagraphIndex,
            Start = request.Start,
            End = request.End,
            // The text as stored, not as the caller reported it: a client sending back something
            // other than what it selected would otherwise be quoting itself.
            Text = text,
            Note = note,
        };

        db.Highlights.Add(highlight);
        await db.SaveChangesAsync(ct);
        await webhooks.HighlightCreatedAsync(highlight.Id, ct);

        return Describe(highlight, paragraphs);
    }

    public async Task<HighlightResponse> UpdateAsync(
        Guid ownerId, Guid id, UpdateHighlightRequest request, CancellationToken ct = default)
    {
        var highlight = await db.Highlights.FirstOrDefaultAsync(h => h.Id == id && h.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Highlight not found.");

        var note = Trimmed(request.Note);
        if (note?.Length > Highlight.MaxNoteLength)
        {
            throw new ValidationException(
                "note", $"A note can be at most {Highlight.MaxNoteLength:N0} characters.");
        }

        highlight.Note = note;
        await db.SaveChangesAsync(ct);

        return Describe(highlight, await ParagraphsAsync(ownerId, highlight.LinkId, ct));
    }

    public async Task DeleteAsync(Guid ownerId, Guid id, CancellationToken ct = default)
    {
        var highlight = await db.Highlights.FirstOrDefaultAsync(h => h.Id == id && h.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Highlight not found.");

        highlight.DeletionTime = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The article's paragraphs, having checked the caller saved this link.
    /// </summary>
    /// <remarks>
    /// Ownership is by having saved it, not by owning the link — links are global rows, so the
    /// link alone says nothing about who may read the text out of it, let alone mark it.
    /// </remarks>
    private async Task<string[]> ParagraphsAsync(Guid ownerId, Guid linkId, CancellationToken ct)
    {
        var content = await db.Links
            .AsNoTracking()
            .Where(l => l.Id == linkId
                && db.PlaylistItems.Any(i => i.LinkId == l.Id && i.Playlist!.OwnerId == ownerId))
            .Select(l => l.Content)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("No readable text was found on that page.");

        return content.Split(ArticleExtractor.ParagraphSeparator, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Turns a stored highlight into a response, saying whether its offsets still find its quote.
    /// </summary>
    /// <remarks>
    /// Enrichment can run again — a paywall lifts, the extractor improves — and the paragraphs
    /// shift underneath. Rather than draw a mark over whatever now happens to sit at those
    /// offsets, a highlight whose quote has moved comes back as an orphan: the words are still
    /// there to read, and the client knows not to place them.
    /// </remarks>
    private static HighlightResponse Describe(Highlight h, string[] paragraphs)
    {
        var stillThere = h.ParagraphIndex >= 0
            && h.ParagraphIndex < paragraphs.Length
            && h.End <= paragraphs[h.ParagraphIndex].Length
            && paragraphs[h.ParagraphIndex][h.Start..h.End] == h.Text;

        return new HighlightResponse(
            h.Id, h.LinkId, h.ParagraphIndex, h.Start, h.End, h.Text, h.Note, h.CreationTime, !stillThere);
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
