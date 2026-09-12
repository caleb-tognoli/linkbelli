using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Running a rule over the links that were already here.
/// </summary>
/// <remarks>
/// Rules only ever saw what arrived after they were written — which the Rules page said out loud,
/// and which is backwards: you write a rule because you noticed a pattern in the library you
/// already have, and the rule pointedly would not touch it.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class AutomationBacklogTests(PostgresApiFactory factory)
{
    private record ItemDto(Guid Id, LinkDto Link, string[] Tags);

    private record LinkDto(Guid Id, string Url, string? Title);

    private record PagedItems(List<ItemDto> Items);

    private record RuleDto(Guid Id, string Name);

    private record RunResult(int Acted);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    /// <summary>Items that arrived before any rule existed — the whole point of this feature.</summary>
    private async Task SeedExistingAsync(Guid playlistId, int count, string host)
    {
        await factory.SeedEnrichedItemsAsync(
            playlistId, count, url: n => $"https://{host}/{Guid.NewGuid():N}/{n}");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

        // Stamped as already seen, which is what they would be: the sweep runs every minute.
        await db.PlaylistItems
            .Where(i => i.PlaylistId == playlistId && i.AutomationAppliedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.AutomationAppliedAt, DateTimeOffset.UtcNow));
    }

    private static async Task<Guid> NewRuleAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/v1/automations", body);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RuleDto>())!.Id;
    }

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    [Fact]
    public async Task A_rule_can_be_run_over_links_that_were_already_here()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var host = $"h{Guid.NewGuid():N}"[..12] + ".example";
        await SeedExistingAsync(inbox, 3, host);

        var rule = await NewRuleAsync(client, new { name = "Tag them", host, addTags = new[] { "backfilled" } });

        // Before: the rule exists and has done nothing, which is the behaviour being fixed.
        Assert.All(await ItemsAsync(client, inbox), i => Assert.Empty(i.Tags));

        var res = await client.PostAsJsonAsync($"/api/v1/automations/{rule}/run", new { });
        res.EnsureSuccessStatusCode();

        Assert.Equal(3, (await res.Content.ReadFromJsonAsync<RunResult>())!.Acted);
        Assert.All(await ItemsAsync(client, inbox), i => Assert.Equal(["backfilled"], i.Tags));
    }

    [Fact]
    public async Task It_leaves_links_the_rule_does_not_match_alone()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var wanted = $"w{Guid.NewGuid():N}"[..12] + ".example";
        var other = $"o{Guid.NewGuid():N}"[..12] + ".example";
        await SeedExistingAsync(inbox, 2, wanted);
        await SeedExistingAsync(inbox, 3, other);

        var rule = await NewRuleAsync(client, new { name = "Only one host", host = wanted, addTags = new[] { "kept" } });

        var res = await client.PostAsJsonAsync($"/api/v1/automations/{rule}/run", new { });

        Assert.Equal(2, (await res.Content.ReadFromJsonAsync<RunResult>())!.Acted);
        var tagged = (await ItemsAsync(client, inbox)).Count(i => i.Tags.Contains("kept"));
        Assert.Equal(2, tagged);
    }

    [Fact]
    public async Task It_can_be_narrowed_to_one_playlist()
    {
        var client = await NewUserAsync();
        var host = $"n{Guid.NewGuid():N}"[..12] + ".example";
        var first = await NewPlaylistAsync(client, "First");
        var second = await NewPlaylistAsync(client, "Second");
        await SeedExistingAsync(first, 2, host);
        await SeedExistingAsync(second, 2, host);

        var rule = await NewRuleAsync(client, new { name = "Everywhere", host, addTags = new[] { "narrow" } });

        var res = await client.PostAsJsonAsync($"/api/v1/automations/{rule}/run", new { playlistId = first });

        Assert.Equal(2, (await res.Content.ReadFromJsonAsync<RunResult>())!.Acted);
        Assert.All(await ItemsAsync(client, second), i => Assert.Empty(i.Tags));
    }

    /// <summary>
    /// The reason this applies one rule rather than clearing the stamp and letting the sweep do
    /// it: the sweep runs every rule, and a copy rule would copy a second time.
    /// </summary>
    [Fact]
    public async Task Running_one_rule_does_not_re_run_the_others()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var digest = await NewPlaylistAsync(client, "Digest");
        var host = $"c{Guid.NewGuid():N}"[..12] + ".example";

        await NewRuleAsync(client, new { name = "Copy everything", copyToPlaylistId = digest });
        await SeedExistingAsync(inbox, 1, host);

        var tagger = await NewRuleAsync(client, new { name = "Tag it", host, addTags = new[] { "once" } });
        await client.PostAsJsonAsync($"/api/v1/automations/{tagger}/run", new { });

        // The copy rule was not asked for, so nothing was copied.
        Assert.Empty(await ItemsAsync(client, digest));
    }

    [Fact]
    public async Task Running_a_rule_that_is_not_yours_is_not_found()
    {
        var owner = await NewUserAsync();
        var inbox = await NewPlaylistAsync(owner, "Mine");
        await SeedExistingAsync(inbox, 1, "somewhere.example");
        var rule = await NewRuleAsync(owner, new { name = "Mine", host = "somewhere.example" });

        var stranger = await NewUserAsync();
        var res = await stranger.PostAsJsonAsync($"/api/v1/automations/{rule}/run", new { });

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Running_it_twice_does_not_tag_twice()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var host = $"t{Guid.NewGuid():N}"[..12] + ".example";
        await SeedExistingAsync(inbox, 2, host);

        var rule = await NewRuleAsync(client, new { name = "Tag", host, addTags = new[] { "twice" } });

        await client.PostAsJsonAsync($"/api/v1/automations/{rule}/run", new { });
        await client.PostAsJsonAsync($"/api/v1/automations/{rule}/run", new { });

        Assert.All(await ItemsAsync(client, inbox), i => Assert.Equal(["twice"], i.Tags));
    }
}
