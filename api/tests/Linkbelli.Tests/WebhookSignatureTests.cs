using System.Security.Cryptography;
using System.Text;
using Linkbelli.Core.Webhooks;

namespace Linkbelli.Tests;

/// <summary>
/// How a receiver knows a delivery came from this Linkbelli, unaltered and recently.
/// </summary>
public class WebhookSignatureTests
{
    private const string Secret = "whsec_test-secret";
    private const string Body = """{"id":"1","event":"items.added"}""";
    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1_789_000_000);
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Pinned against an HMAC worked out here, independently, because the format is a promise to
    /// every receiver already written against it: changing it quietly breaks all of them.
    /// </summary>
    [Fact]
    public void The_format_is_a_timestamp_and_an_hmac_of_timestamp_dot_body()
    {
        var expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"1789000000.{Body}")));

        Assert.Equal($"t=1789000000,v1={expected}", WebhookSignature.Sign(Secret, Body, At));
    }

    [Fact]
    public void A_signature_verifies_against_the_body_it_was_made_for()
    {
        var header = WebhookSignature.Sign(Secret, Body, At);

        Assert.True(WebhookSignature.Verify(Secret, Body, header, At.AddSeconds(30), Tolerance));
    }

    [Fact]
    public void A_changed_body_does_not_verify()
    {
        var header = WebhookSignature.Sign(Secret, Body, At);

        Assert.False(WebhookSignature.Verify(Secret, Body.Replace("added", "removed"), header, At, Tolerance));
    }

    [Fact]
    public void Another_secret_does_not_verify()
    {
        var header = WebhookSignature.Sign(Secret, Body, At);

        Assert.False(WebhookSignature.Verify("whsec_somebody-else", Body, header, At, Tolerance));
    }

    /// <summary>
    /// The timestamp is inside the signature so a captured delivery cannot be sent again later —
    /// changing the date breaks the signature, and keeping it makes the delivery stale.
    /// </summary>
    [Fact]
    public void An_old_delivery_replayed_later_does_not_verify()
    {
        var header = WebhookSignature.Sign(Secret, Body, At);

        Assert.False(WebhookSignature.Verify(Secret, Body, header, At.AddHours(1), Tolerance));
    }

    [Fact]
    public void Moving_the_timestamp_breaks_the_signature()
    {
        var header = WebhookSignature.Sign(Secret, Body, At);
        var later = header.Replace("t=1789000000", "t=1789003600");

        Assert.False(WebhookSignature.Verify(Secret, Body, later, At.AddHours(1), Tolerance));
    }

    /// <summary>More than one v1 is how a receiver would accept both secrets during a rotation.</summary>
    [Fact]
    public void Any_one_matching_signature_is_enough()
    {
        var real = WebhookSignature.Sign(Secret, Body, At);
        var withAnother = real + ",v1=" + new string('0', 64);

        Assert.True(WebhookSignature.Verify(Secret, Body, withAnother, At, Tolerance));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v1=abc")]
    [InlineData("t=notanumber,v1=abc")]
    [InlineData("t=1789000000")]
    [InlineData("garbage")]
    public void A_malformed_header_is_simply_not_valid(string? header)
    {
        Assert.False(WebhookSignature.Verify(Secret, Body, header, At, Tolerance));
    }

    [Fact]
    public void A_new_secret_is_recognisable_and_never_the_same_twice()
    {
        var a = WebhookSignature.NewSecret();
        var b = WebhookSignature.NewSecret();

        Assert.StartsWith(WebhookSignature.SecretPrefix, a);
        Assert.NotEqual(a, b);
        // 32 random bytes, base64url without padding.
        Assert.Equal(WebhookSignature.SecretPrefix.Length + 43, a.Length);
        Assert.DoesNotContain('+', a);
        Assert.DoesNotContain('/', a);
    }
}
