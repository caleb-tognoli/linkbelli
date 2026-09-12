using Linkbelli.Application.Security;

namespace Linkbelli.Application.Email;

/// <summary>
/// The link at the bottom of every notification that turns that kind off.
/// </summary>
/// <remarks>
/// Defaulting these to on is only defensible if leaving takes one click and no password. Somebody
/// who does not want mail from us is in the worst position to go and find a settings page: they
/// may not remember the account, and asking them to sign in to stop unwanted mail is how a
/// product earns a spam complaint instead of an unsubscribe.
///
/// The token is protected with the app's data protection key ring, so it cannot be forged or
/// edited to unsubscribe somebody else, and it carries no secret beyond the user id it names.
/// </remarks>
public interface IUnsubscribeTokens
{
    string Create(Guid userId, NotificationKind kind);

    /// <summary>Reads one back. Null when it was tampered with, truncated, or from another instance.</summary>
    (Guid UserId, NotificationKind Kind)? Read(string token);
}

/// <inheritdoc />
public sealed class UnsubscribeTokens(ISecretProtector protector) : IUnsubscribeTokens
{
    // Prefixed so a protected value from somewhere else in the app cannot be used here. The key
    // ring is shared, and a source's stored auth header must not double as an unsubscribe link.
    private const string Prefix = "unsubscribe";

    public string Create(Guid userId, NotificationKind kind) =>
        protector.Protect($"{Prefix}:{userId:N}:{kind.Slug()}");

    public (Guid UserId, NotificationKind Kind)? Read(string token)
    {
        string plain;
        try
        {
            plain = protector.Unprotect(token);
        }
        catch
        {
            // Tampered, truncated by a mail client, or protected by a different key ring. All of
            // them mean the same thing to the person: the link did not work.
            return null;
        }

        var parts = plain.Split(':');
        if (parts.Length != 3 || parts[0] != Prefix) return null;
        if (!Guid.TryParseExact(parts[1], "N", out var userId)) return null;
        if (NotificationKinds.Parse(parts[2]) is not { } kind) return null;

        return (userId, kind);
    }
}
