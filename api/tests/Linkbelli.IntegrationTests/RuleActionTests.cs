using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// What a rule gained the ability to do.
/// </summary>
/// <remarks>
/// A rule could silently trash things and could not flag them: no score, no archive. Scoring in
/// particular matters because the queue sorts on it — "this source is worth my time" was a thing
/// somebody could only say by rating every item by hand.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class RuleActionTests(PostgresApiFactory factory)
{
    private record RuleDto(Guid Id, string Name, int? SetScore, bool Archive, int? MaxMinutes, bool? Broken);

    private record ItemDto(Guid Id, int? Score, LinkRef Link);

    private record LinkRef(Guid Id);

    private record PagedItems(List<ItemDto> Items);

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

    private static async Task<RuleDto> NewRuleAsync(HttpClient client, object body)
    {
        var res = await client.PostAsJsonAsync("/api/v1/automations", body);
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<RuleDto>())!;
    }

    /// <summary>Runs the saved rule over what is already in the playlist.</summary>
    private static async Task RunAsync(HttpClient client, Guid ruleId, Guid playlistId) =>
        (await client.PostAsJsonAsync($"/api/v1/automations/{ruleId}/run", new { playlistId }))
            .EnsureSuccessStatusCode();

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    [Fact]
    public async Task A_rule_can_score_what_it_matches()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Scored {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist, 2);

        var rule = await NewRuleAsync(client, new
        {
            name = $"Worth my time {Guid.NewGuid():N}",
            playlistId = playlist,
            setScore = 80,
        });

        Assert.Equal(80, rule.SetScore);
        await RunAsync(client, rule.Id, playlist);

        Assert.All(await ItemsAsync(client, playlist), i => Assert.Equal(80, i.Score));
    }

    [Fact]
    public async Task A_rule_can_ask_for_a_snapshot()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Kept {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist, 1);

        var rule = await NewRuleAsync(client, new
        {
            name = $"Keep a copy {Guid.NewGuid():N}",
            playlistId = playlist,
            archive = true,
        });

        Assert.True(rule.Archive);
        await RunAsync(client, rule.Id, playlist);

        var linkId = Assert.Single(await ItemsAsync(client, playlist)).Link.Id;

        // Asked for, not done here: archiving is an outbound request, and the sweep that already
        // exists is where the rate limiting and the retry budget live.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        Assert.True(await db.Links.Where(l => l.Id == linkId).Select(l => l.ArchiveRequested).FirstAsync());
    }

    /// <summary>
    /// The Rules page's own example copy suggests exactly this, and the engine could not
    /// express it.
    /// </summary>
    [Fact]
    public async Task A_rule_can_pick_out_the_long_ones()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Mixed lengths {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 2);

        await factory.SetWordCountAsync(seeded[0], 6_000);
        await factory.SetWordCountAsync(seeded[1], 300);

        var rule = await NewRuleAsync(client, new
        {
            name = $"The long ones {Guid.NewGuid():N}",
            playlistId = playlist,
            minMinutes = 20,
            setScore = 10,
        });

        await RunAsync(client, rule.Id, playlist);

        var items = await ItemsAsync(client, playlist);
        Assert.Equal(10, items.Single(i => i.Id == seeded[0]).Score);
        Assert.Null(items.Single(i => i.Id == seeded[1]).Score);
    }

    /// <summary>
    /// The preview has to agree with the run, or the confirmation before a destructive rule is
    /// worth nothing.
    /// </summary>
    [Fact]
    public async Task The_preview_counts_what_the_run_would_act_on()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Previewed {Guid.NewGuid():N}");
        var seeded = await factory.SeedEnrichedItemsAsync(playlist, 3);

        await factory.SetWordCountAsync(seeded[0], 6_000);
        await factory.SetWordCountAsync(seeded[1], 6_000);
        await factory.SetWordCountAsync(seeded[2], 300);

        var res = await client.PostAsJsonAsync("/api/v1/automations/preview", new
        {
            name = "Preview",
            playlistId = playlist,
            minMinutes = 20,
        });
        res.EnsureSuccessStatusCode();

        var preview = (await res.Content.ReadFromJsonAsync<PreviewDto>())!;
        Assert.Equal(2, preview.Matches);
    }

    private record PreviewDto(int Matches);
}
