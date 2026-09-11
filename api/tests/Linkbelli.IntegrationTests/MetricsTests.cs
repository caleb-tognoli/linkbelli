using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Observability was one health check and Hangfire's dashboard: enough to say the process was
/// alive and nothing about whether it was doing its job. These cover the scrape endpoint and who
/// may read it — metrics name every host this instance fetches.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MetricsTests(PostgresApiFactory factory)
{
    private record TokenDto(string AccessToken);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> NewAdminAsync()
    {
        var username = NewUsername();
        var client = factory.CreateClient();
        await client.RegisterAndLoginAsync(username);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync("Admin"))
            {
                await roles.CreateAsync(new IdentityRole<Guid>("Admin"));
            }

            await users.AddToRoleAsync((await users.FindByNameAsync(username))!, "Admin");
        }

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { login = username, password = Password });
        var token = (await login.Content.ReadFromJsonAsync<TokenDto>())!.AccessToken;

        var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return admin;
    }

    [Fact]
    public async Task Metrics_are_not_public()
    {
        // They name every host this instance fetches and how much of everything there is. That is
        // not a public fact about somebody's private collection.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync("/metrics")).StatusCode);

        var user = await NewUserAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/metrics")).StatusCode);
    }

    [Fact]
    public async Task An_admin_can_scrape_them()
    {
        var admin = await NewAdminAsync();

        var res = await admin.GetAsync("/metrics");
        res.EnsureSuccessStatusCode();

        var body = await res.Content.ReadAsStringAsync();

        // Prometheus exposition, which is the contract a scraper relies on. Which series are
        // present on the very first scrape depends on what has been collected by then, so this
        // asserts the format rather than a particular counter.
        Assert.StartsWith("text/plain", res.Content.Headers.ContentType?.MediaType ?? "");
        Assert.Contains("# TYPE", body);
    }

    [Fact]
    public async Task The_app_meter_is_exported_once_it_has_something_to_say()
    {
        var admin = await NewAdminAsync();
        var user = await NewUserAsync();

        // A request that exercises the pipeline, so the ASP.NET instrumentation has a series.
        (await user.GetAsync("/api/v1/playlists")).EnsureSuccessStatusCode();

        var body = await (await admin.GetAsync("/metrics")).Content.ReadAsStringAsync();

        Assert.Contains("http_server", body);
    }
}
