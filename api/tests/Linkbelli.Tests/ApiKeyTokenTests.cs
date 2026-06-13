using Linkbelli.Application.Auth;

namespace Linkbelli.Tests;

public class ApiKeyTokenTests
{
    [Fact]
    public void Generate_then_parse_roundtrips_and_hash_matches()
    {
        var generated = ApiKeyToken.Generate();

        Assert.True(ApiKeyToken.TryParse(generated.Token, out var publicId, out var secret));
        Assert.Equal(generated.PublicId, publicId);
        Assert.Equal(generated.Hash, ApiKeyToken.Hash(secret));
    }

    [Fact]
    public void Generate_produces_unique_tokens()
    {
        Assert.NotEqual(ApiKeyToken.Generate().Token, ApiKeyToken.Generate().Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("lbk_onlytwo")]
    [InlineData("wrong_prefix_secret")]
    [InlineData("lbk__emptysecret")]
    public void TryParse_rejects_malformed_tokens(string token)
    {
        Assert.False(ApiKeyToken.TryParse(token, out _, out _));
    }

    [Fact]
    public void TryParse_keeps_underscores_in_the_secret()
    {
        // The secret is base64url and may contain '_'; only the publicId delimiter must split.
        Assert.True(ApiKeyToken.TryParse("lbk_abc123_se_cr_et", out var publicId, out var secret));
        Assert.Equal("abc123", publicId);
        Assert.Equal("se_cr_et", secret);
    }

    [Fact]
    public void Hash_is_stable_and_lowercase_hex()
    {
        var hash = ApiKeyToken.Hash("some-secret");
        Assert.Equal(ApiKeyToken.Hash("some-secret"), hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash.ToLowerInvariant(), hash);
    }
}
