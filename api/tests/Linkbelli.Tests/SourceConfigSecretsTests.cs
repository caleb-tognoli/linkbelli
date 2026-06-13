using Linkbelli.Application.Security;
using Linkbelli.Application.Sources;
using Linkbelli.Core.Entities;

namespace Linkbelli.Tests;

public class SourceConfigSecretsTests
{
    // Reversible stand-in for Data Protection so we can assert encryption happened.
    private sealed class ReversibleProtector : ISecretProtector
    {
        public string Protect(string plaintext) => "enc:" + plaintext;
        public string Unprotect(string protectedValue) => protectedValue["enc:".Length..];
    }

    private static SourceConfigSecrets Make() =>
        new([new JsonApiSourceInterpreter(null!)], new ReversibleProtector());

    [Fact]
    public void Encrypts_secret_keys_only_and_round_trips()
    {
        var secrets = Make();
        var incoming = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.UrlKey] = "https://api.example/feed",
            ["header.Authorization"] = "Bearer s3cret",
        };

        var stored = secrets.Encrypt(SourceType.JsonApi, incoming, stored: null);
        Assert.Equal("https://api.example/feed", stored[JsonApiSourceInterpreter.UrlKey]);
        Assert.Equal("enc:Bearer s3cret", stored["header.Authorization"]);

        var redacted = secrets.Redact(SourceType.JsonApi, stored);
        Assert.Equal("https://api.example/feed", redacted[JsonApiSourceInterpreter.UrlKey]);
        Assert.Equal(SourceConfigSecrets.Redacted, redacted["header.Authorization"]);

        var decrypted = secrets.Decrypt(SourceType.JsonApi, stored);
        Assert.Equal("Bearer s3cret", decrypted["header.Authorization"]);
    }

    [Fact]
    public void Redacted_secret_on_update_preserves_prior_value()
    {
        var secrets = Make();
        var prior = secrets.Encrypt(SourceType.JsonApi, new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.UrlKey] = "https://api.example/feed",
            ["header.Authorization"] = "Bearer original",
        }, stored: null);

        // Caller re-sends the redacted marker (didn't change the secret).
        var update = new Dictionary<string, string>
        {
            [JsonApiSourceInterpreter.UrlKey] = "https://api.example/feed",
            ["header.Authorization"] = SourceConfigSecrets.Redacted,
        };

        var result = secrets.Encrypt(SourceType.JsonApi, update, prior);
        Assert.Equal(prior["header.Authorization"], result["header.Authorization"]);
        Assert.Equal("Bearer original", secrets.Decrypt(SourceType.JsonApi, result)["header.Authorization"]);
    }
}
