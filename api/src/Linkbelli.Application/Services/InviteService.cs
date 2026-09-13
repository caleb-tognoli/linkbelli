using Linkbelli.Application.Auth;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Email;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Linkbelli.Application.Services;

/// <summary>
/// Inviting somebody who does not have an account yet.
/// </summary>
/// <remarks>
/// Membership resolved an existing username or failed, so collaborating meant the other person
/// already had an account here <em>and</em> you knew their exact username — which, on an instance
/// you have just stood up, nobody does. Paired with registration that can be closed, an operator's
/// only two options were "let the whole internet sign up" and "nobody can ever share with me".
///
/// The link is the thing, and mail is optional. An instance with no mail configured still has to
/// be able to invite somebody, so the token comes back to the caller to paste into a message they
/// send themselves — and is emailed as well when there is somewhere to send it.
/// </remarks>
public interface IInviteService
{
    /// <summary>How long a link lasts. Long enough to be seen, short enough not to linger.</summary>
    const int ValidForDays = 14;

    /// <summary>Creates one for a playlist the caller owns. Returns the link, once.</summary>
    Task<InviteCreated> CreateAsync(
        Guid ownerId, Guid playlistId, CreateInviteRequest request, CancellationToken ct = default);

    /// <summary>What a link leads to, before anybody commits to it. Anonymous.</summary>
    Task<InvitePreview> PreviewAsync(string token, CancellationToken ct = default);

    /// <summary>Takes it up, as the signed-in caller. Idempotent for whoever already accepted it.</summary>
    Task<InviteAccepted> AcceptAsync(Guid userId, string token, CancellationToken ct = default);

    /// <summary>The live invitations on a playlist, for its owner.</summary>
    Task<IReadOnlyList<InviteSummary>> ListAsync(
        Guid ownerId, Guid playlistId, CancellationToken ct = default);

    /// <summary>Takes one back before it is used.</summary>
    Task RevokeAsync(Guid ownerId, Guid inviteId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class InviteService(
    IAppDbContext db,
    IEmailSender email,
    IOptions<EmailOptions> options,
    IAuditLog audit) : IInviteService
{
    private readonly EmailOptions _options = options.Value;

    public async Task<InviteCreated> CreateAsync(
        Guid ownerId, Guid playlistId, CreateInviteRequest request, CancellationToken ct = default)
    {
        var playlist = await db.Playlists
            .FirstOrDefaultAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Playlist not found.");

        var (token, hash) = InviteToken.Generate();
        var expires = DateTimeOffset.UtcNow.AddDays(IInviteService.ValidForDays);

        var invite = new Invite
        {
            TokenHash = hash,
            PlaylistId = playlist.Id,
            Role = request.Role ?? PlaylistRole.Viewer,
            InvitedBy = ownerId,
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            ExpiresAt = expires,
        };

        db.Invites.Add(invite);
        await db.SaveChangesAsync(ct);

        var url = $"{_options.PublicUrl.TrimEnd('/')}/invite/{token}";
        var sent = false;

        if (invite.Email is { } address && email.IsConfigured)
        {
            var from = await db.Users
                .Where(u => u.Id == ownerId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(ct) ?? "Somebody";

            sent = await email.SendAsync(
                EmailTemplates.PlaylistInvite(address, from, playlist.Name, url, IInviteService.ValidForDays),
                ct);
        }

        await audit.RecordAsync(
            ownerId, "playlist.invite.created", "Playlist", playlist.Id,
            $"Role {invite.Role}.", ct: ct);

        // The link comes back whether or not it was emailed. Mail is optional on this product,
        // and an instance without it still has to be able to invite somebody — so the caller can
        // always copy the link into a message they send themselves.
        return new InviteCreated(invite.Id, url, invite.ExpiresAt, sent);
    }

    public async Task<InvitePreview> PreviewAsync(string token, CancellationToken ct = default)
    {
        var invite = await FindLiveAsync(token, ct);

        var playlist = await db.Playlists
            .Where(p => p.Id == invite.PlaylistId)
            .Select(p => new { p.Name, Owner = db.Users.Where(u => u.Id == p.OwnerId).Select(u => u.UserName).FirstOrDefault() })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("That invitation is no longer valid.");

        return new InvitePreview(playlist.Name, playlist.Owner ?? "Somebody", invite.Role, invite.ExpiresAt);
    }

    public async Task<InviteAccepted> AcceptAsync(Guid userId, string token, CancellationToken ct = default)
    {
        var invite = await FindLiveAsync(token, ct);

        if (invite.PlaylistId is not { } playlistId)
        {
            throw new NotFoundException("That invitation is no longer valid.");
        }

        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId, ct)
            ?? throw new NotFoundException("That playlist is no longer here.");

        if (playlist.OwnerId == userId)
        {
            // Their own list. Not an error — following your own invitation link from a second
            // device is an ordinary thing to do — but there is no membership to add.
            return new InviteAccepted(playlistId, playlist.Name, invite.Role);
        }

        var existing = await db.PlaylistMembers
            .FirstOrDefaultAsync(m => m.PlaylistId == playlistId && m.UserId == userId, ct);

        if (existing is null)
        {
            db.PlaylistMembers.Add(new PlaylistMember
            {
                PlaylistId = playlistId,
                UserId = userId,
                Role = invite.Role,
                InvitedBy = invite.InvitedBy,
            });
        }
        else if (existing.Role < invite.Role)
        {
            // An invitation to do more than they already can is a promotion. One to do less is
            // not a demotion: somebody's existing access is not taken away by a link.
            existing.Role = invite.Role;
        }

        invite.AcceptedAt = DateTimeOffset.UtcNow;
        invite.AcceptedBy = userId;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            userId, "playlist.invite.accepted", "Playlist", playlistId, ct: ct);

        return new InviteAccepted(playlistId, playlist.Name, invite.Role);
    }

    public async Task<IReadOnlyList<InviteSummary>> ListAsync(
        Guid ownerId, Guid playlistId, CancellationToken ct = default)
    {
        _ = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId && p.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Playlist not found.");

        var now = DateTimeOffset.UtcNow;

        // The live ones. A used or expired invitation is not something to offer to revoke, and
        // listing them would make a short list look like a long one.
        return await db.Invites
            .Where(i => i.PlaylistId == playlistId && i.AcceptedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.ExpiresAt)
            .Select(i => new InviteSummary(i.Id, i.Email, i.Role, i.ExpiresAt))
            .ToListAsync(ct);
    }

    public async Task RevokeAsync(Guid ownerId, Guid inviteId, CancellationToken ct = default)
    {
        var invite = await db.Invites
            .FirstOrDefaultAsync(i => i.Id == inviteId && i.Playlist!.OwnerId == ownerId, ct)
            ?? throw new NotFoundException("Invitation not found.");

        db.Invites.Remove(invite);
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            ownerId, "playlist.invite.revoked", "Playlist", invite.PlaylistId, ct: ct);
    }

    /// <summary>
    /// The invitation a token names, if it is still good for anything.
    /// </summary>
    /// <remarks>
    /// Used, expired and never-existed all answer the same way. Which of the three it is tells an
    /// anonymous caller something about a link they were not given.
    /// </remarks>
    private async Task<Invite> FindLiveAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new NotFoundException("That invitation is no longer valid.");
        }

        var hash = InviteToken.HashOf(token.Trim());
        var now = DateTimeOffset.UtcNow;

        return await db.Invites
            .FirstOrDefaultAsync(i => i.TokenHash == hash && i.AcceptedAt == null && i.ExpiresAt > now, ct)
            ?? throw new NotFoundException("That invitation is no longer valid.");
    }
}
