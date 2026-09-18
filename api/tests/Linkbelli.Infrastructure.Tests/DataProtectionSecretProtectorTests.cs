using System.Security.Cryptography;
using Linkbelli.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Linkbelli.Infrastructure.Tests;

/// <summary>
/// Whether a secret put away comes back — and comes back only to the thing that put it away.
/// </summary>
/// <remarks>
/// Source credentials and webhook signing secrets are stored through this. A failure here does
/// not look like an error on the day it happens; it looks like every stored secret becoming
/// unreadable at once, some time later.
/// </remarks>
public class DataProtectionSecretProtectorTests
{
    /// <summary>The purpose every stored secret was written under. Changing it orphans all of them.</summary>
    private const string Purpose = "Linkbelli.Sources.Secrets.v1";

    [Fact]
    public void A_secret_comes_back_as_it_went_in()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var stored = protector.Protect("api-key-123");

        Assert.Equal("api-key-123", protector.Unprotect(stored));
    }

    [Fact]
    public void What_is_stored_is_not_the_secret()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());

        var stored = protector.Protect("api-key-123");

        Assert.DoesNotContain("api-key-123", stored);
        // And not the same thing twice: equal secrets must not be recognisable as equal at rest.
        Assert.NotEqual(stored, protector.Protect("api-key-123"));
    }

    /// <summary>
    /// Pinned because it is a promise to data already on disk: every secret an instance has ever
    /// stored was written under this purpose, and a change to the string would make all of them
    /// unreadable on the next deploy without a single error at the time.
    /// </summary>
    [Fact]
    public void Secrets_written_under_the_existing_purpose_still_read_back()
    {
        var provider = new EphemeralDataProtectionProvider();
        var writtenEarlier = provider.CreateProtector(Purpose).Protect("stored last year");

        Assert.Equal("stored last year", new DataProtectionSecretProtector(provider).Unprotect(writtenEarlier));
    }

    [Fact]
    public void Something_protected_for_another_purpose_is_not_readable_here()
    {
        var provider = new EphemeralDataProtectionProvider();
        var elsewhere = provider.CreateProtector("Some.Other.Purpose").Protect("not yours");

        Assert.ThrowsAny<CryptographicException>(() => new DataProtectionSecretProtector(provider).Unprotect(elsewhere));
    }

    [Fact]
    public void A_tampered_value_is_refused_rather_than_decrypted_to_garbage()
    {
        var protector = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider());
        var stored = protector.Protect("api-key-123");
        var tampered = stored[..^4] + (stored[^4] == 'A' ? "B" : "A") + stored[^3..];

        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
    }

    /// <summary>A key ring from another installation is another installation's secrets.</summary>
    [Fact]
    public void Another_key_ring_cannot_read_it()
    {
        var stored = new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Protect("api-key-123");

        Assert.ThrowsAny<CryptographicException>(
            () => new DataProtectionSecretProtector(new EphemeralDataProtectionProvider()).Unprotect(stored));
    }
}
