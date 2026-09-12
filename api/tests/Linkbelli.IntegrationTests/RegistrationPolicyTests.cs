using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Whether this instance will take a new account.
/// </summary>
/// <remarks>
/// It always would, with no way to say otherwise — so a self-hosted Linkbelli anyone could reach
/// was a Linkbelli anyone could get an outbound fetcher, a share of the run quota and a public
/// profile on. Read from configuration rather than stored, so it cannot be switched back on by
/// whoever gets into the admin console.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class RegistrationPolicyTests(PostgresApiFactory factory)
{
    private HttpClient ClientWithMode(string? mode)
    {
        var app = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(config =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Registration:Mode"] = mode })));

        return app.CreateClient();
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client)
    {
        var username = NewUsername();
        return client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            email = $"{username}@example.com",
            password = Password,
        });
    }

    [Fact]
    public async Task Open_is_the_default_and_takes_anyone()
    {
        var res = await RegisterAsync(ClientWithMode(null));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Open_said_explicitly_behaves_the_same()
    {
        var res = await RegisterAsync(ClientWithMode("Open"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Closed_refuses_and_says_so()
    {
        var res = await RegisterAsync(ClientWithMode("Closed"));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("not accepting new accounts", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unrecognised_mode_falls_back_to_open_rather_than_locking_everyone_out()
    {
        var res = await RegisterAsync(ClientWithMode("Nonsense"));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    /// <summary>
    /// The suite has registered plenty of users by the time this runs, which is exactly the state
    /// FirstUserOnly is meant to refuse in.
    /// </summary>
    [Fact]
    public async Task FirstUserOnly_refuses_once_somebody_already_has_an_account()
    {
        var client = ClientWithMode("Open");
        (await RegisterAsync(client)).EnsureSuccessStatusCode();

        var res = await RegisterAsync(ClientWithMode("FirstUserOnly"));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task The_mode_is_read_without_regard_to_case()
    {
        var res = await RegisterAsync(ClientWithMode("closed"));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
