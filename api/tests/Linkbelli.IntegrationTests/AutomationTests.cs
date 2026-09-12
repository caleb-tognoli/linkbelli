using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Automation;
using Linkbelli.Application.Data;
using Linkbelli.Core.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Everything a source finds lands where the source was pointed and stays there, so filing it is
/// a decision made again for every item. These cover what the rules do, the order they do it in,
/// and the two things that stop them being dangerous: they only see what arrives after them, and
/// they can't write into a playlist that isn't yours.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class AutomationTests(PostgresApiFactory factory)
{
    private record RuleDto(
        Guid Id, string Name, bool Enabled, int Position, string[] AddTags,
        Guid? MoveToPlaylistId, bool MarkWatched, bool Trash, bool StopOnMatch,
        int MatchCount, DateTimeOffset? LastMatchedAt);

    private record PreviewDto(int Matches, List<PreviewItemDto> Sample);

    private record PreviewItemDto(Guid ItemId, string PlaylistName, string Url, string? Title);

    private record ItemDto(Guid Id, string Status, LinkDto Link, string[] Tags);

    private record LinkDto(Guid Id, string Url, string? Title);

    private record PagedItems(List<ItemDto> Items);

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        var token = await client.RegisterAndLoginAsync(NewUsername());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    /// <summary>
    /// Seeds an item the way a source run leaves one, then runs the sweep. Seeded items arrive
    /// already stamped as enriched but never having been past the rules — which is the state the
    /// sweep exists for.
    /// </summary>
    private async Task<Guid> ArriveAsync(
        Guid playlistId, string url, string title, ContentKind kind = ContentKind.Article)
    {
        var seeded = await factory.SeedEnrichedItemsAsync(playlistId, 1, _ => title, _ => url);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Settle the field before declaring which row is due. The sweep takes the oldest
            // BatchSize items awaiting automation belonging to anybody, and this suite shares one
            // database — so once it is large enough, every other test's backlog crowds this
            // test's row out of the batch and the assertion fails for a reason that has nothing
            // to do with automation. The same shape as the backup, digest and classification
            // tests, and the same reason.
            await db.PlaylistItems
                .Where(i => i.AutomationAppliedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.AutomationAppliedAt, DateTimeOffset.UtcNow));

            var item = await db.PlaylistItems.Include(i => i.Link).FirstAsync(i => i.Id == seeded[0]);
            item.Link!.Kind = kind;
            item.AutomationAppliedAt = null;
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAutomationRunner>().SweepAsync();
        }

        return seeded[0];
    }

    private static async Task<List<ItemDto>> ItemsAsync(HttpClient client, Guid playlistId) =>
        (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlistId}/items"))!.Items;

    private static async Task<List<RuleDto>> RulesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<RuleDto>>("/api/v1/automations"))!;

    [Fact]
    public async Task A_matching_rule_tags_and_files_what_arrives()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var reading = await NewPlaylistAsync(client, "Reading");

        await NewRuleAsync(client, new
        {
            name = "Rust goes to reading",
            titlePattern = "rust",
            addTags = new[] { "rust" },
            moveToPlaylistId = reading,
        });

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Rust 1.90 is out");

        Assert.Empty(await ItemsAsync(client, inbox));
        var filed = Assert.Single(await ItemsAsync(client, reading));
        Assert.Equal(["rust"], filed.Tags);
    }

    [Fact]
    public async Task Something_that_does_not_match_is_left_exactly_where_it_landed()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var reading = await NewPlaylistAsync(client, "Reading");

        await NewRuleAsync(client, new { name = "Rust only", titlePattern = "rust", moveToPlaylistId = reading });

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Go 1.26 is out");

        Assert.Single(await ItemsAsync(client, inbox));
        Assert.Empty(await ItemsAsync(client, reading));
    }

    [Fact]
    public async Task Rules_run_in_order_and_one_can_shield_an_item_from_the_next()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var keep = await NewPlaylistAsync(client, "Keep");

        // Specific first, and it stops. Without that the broad rule below would have to
        // enumerate every exception to it.
        await NewRuleAsync(client, new
        {
            name = "Keep the release notes",
            titlePattern = "release",
            moveToPlaylistId = keep,
            stopOnMatch = true,
            position = 1,
        });
        await NewRuleAsync(client, new { name = "Bin the rest", trash = true, position = 2 });

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Release notes for 2.0");
        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Anything else");

        Assert.Single(await ItemsAsync(client, keep));
        Assert.Empty(await ItemsAsync(client, inbox));
    }

    [Fact]
    public async Task A_trashed_item_is_recoverable_rather_than_gone()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        await NewRuleAsync(client, new { name = "Bin ads", titlePattern = "sponsored", trash = true });

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "A sponsored post");

        Assert.Empty(await ItemsAsync(client, inbox));

        // A rule that deletes permanently is one bad pattern away from losing someone's library.
        var trash = await client.GetFromJsonAsync<TrashDto>("/api/v1/trash");
        Assert.Single(trash!.Items);
    }

    private record TrashDto(List<TrashItemDto> Items);

    private record TrashItemDto(Guid Id);

    [Fact]
    public async Task A_rule_can_mark_things_watched_without_moving_them()
    {
        var client = await NewUserAsync();
        var archive = await NewPlaylistAsync(client, "Archive");
        await NewRuleAsync(client, new { name = "Already seen", playlistId = archive, markWatched = true });

        await ArriveAsync(archive, $"https://blog.example/{Guid.NewGuid():N}", "For the record");

        var item = Assert.Single(await ItemsAsync(client, archive));
        Assert.Equal("Watched", item.Status);
    }

    [Fact]
    public async Task A_rule_scoped_to_one_playlist_leaves_the_others_alone()
    {
        var client = await NewUserAsync();
        var watched = await NewPlaylistAsync(client, "Watched list");
        var other = await NewPlaylistAsync(client, "Other list");
        await NewRuleAsync(client, new { name = "Scoped", playlistId = watched, markWatched = true });

        await ArriveAsync(watched, $"https://blog.example/{Guid.NewGuid():N}", "In scope");
        await ArriveAsync(other, $"https://blog.example/{Guid.NewGuid():N}", "Out of scope");

        Assert.Equal("Watched", (await ItemsAsync(client, watched))[0].Status);
        Assert.Equal("Added", (await ItemsAsync(client, other))[0].Status);
    }

    [Fact]
    public async Task A_rule_only_ever_sees_what_arrives_after_it()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var reading = await NewPlaylistAsync(client, "Reading");

        // Already here, already past the rules.
        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Rust was here first");

        await NewRuleAsync(client, new { name = "Late rule", titlePattern = "rust", moveToPlaylistId = reading });
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAutomationRunner>().SweepAsync();
        }

        // A rule says "when this arrives", not "reorganise everything I have ever saved".
        Assert.Single(await ItemsAsync(client, inbox));
        Assert.Empty(await ItemsAsync(client, reading));
    }

    [Fact]
    public async Task A_rule_cannot_file_into_someone_elses_playlist()
    {
        var stranger = await NewUserAsync();
        var theirs = await NewPlaylistAsync(stranger, "Not yours");

        var client = await NewUserAsync();
        var res = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Reach across",
            moveToPlaylistId = theirs,
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_rule_cannot_both_move_and_copy()
    {
        var client = await NewUserAsync();
        var a = await NewPlaylistAsync(client, "A");
        var b = await NewPlaylistAsync(client, "B");

        var res = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = "Both",
            moveToPlaylistId = a,
            copyToPlaylistId = b,
        });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task A_pattern_that_will_not_compile_is_refused_at_save_time()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/automations", new { name = "Broken", titlePattern = "(" });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Copying_leaves_the_original_where_it_is()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var digest = await NewPlaylistAsync(client, "Digest");
        await NewRuleAsync(client, new { name = "Also to digest", copyToPlaylistId = digest });

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Both places");

        Assert.Single(await ItemsAsync(client, inbox));
        Assert.Single(await ItemsAsync(client, digest));
    }

    [Fact]
    public async Task A_copy_does_not_run_the_rules_again()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var digest = await NewPlaylistAsync(client, "Digest");

        // Left to itself, the copy would match the same rule and copy again, forever.
        await NewRuleAsync(client, new { name = "Copy everything", copyToPlaylistId = digest });

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Once only");

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAutomationRunner>().SweepAsync();
        }

        Assert.Single(await ItemsAsync(client, digest));
    }

    [Fact]
    public async Task A_rule_counts_what_it_has_acted_on()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        await NewRuleAsync(client, new { name = "Tag everything", addTags = new[] { "inbox" } });

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "One");
        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Two");

        // A rule that has never fired is almost always a rule that doesn't work, and nothing
        // else on the page would ever say so.
        var rule = Assert.Single(await RulesAsync(client));
        Assert.Equal(2, rule.MatchCount);
        Assert.NotNull(rule.LastMatchedAt);
    }

    [Fact]
    public async Task A_disabled_rule_does_nothing_but_is_still_there()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var rule = await NewRuleAsync(client, new { name = "Off", addTags = new[] { "tagged" } });

        (await client.PatchAsJsonAsync($"/api/v1/automations/{rule.Id}", new { enabled = false }))
            .EnsureSuccessStatusCode();

        await ArriveAsync(inbox, $"https://blog.example/{Guid.NewGuid():N}", "Untouched");

        Assert.Empty((await ItemsAsync(client, inbox))[0].Tags);
        Assert.Single(await RulesAsync(client));
    }

    [Fact]
    public async Task A_preview_says_what_a_rule_would_have_caught()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var tag = Guid.NewGuid().ToString("N")[..8];
        await factory.SeedEnrichedItemsAsync(inbox, 3, n => n == 2 ? "Something else" : $"{tag} matched {n}");

        var preview = await client.PostAsJsonAsync("/api/v1/automations/preview", new
        {
            name = "Would it work",
            titlePattern = tag,
        });
        preview.EnsureSuccessStatusCode();

        // A rule only acts on what arrives next, so without this the only way to find out
        // whether it works is to wait and see what it does.
        var result = (await preview.Content.ReadFromJsonAsync<PreviewDto>())!;
        Assert.Equal(2, result.Matches);
        Assert.All(result.Sample, item => Assert.Contains(tag, item.Title));
    }

    [Fact]
    public async Task Rules_are_private_to_their_owner()
    {
        var client = await NewUserAsync();
        var rule = await NewRuleAsync(client, new { name = "Mine" });

        var stranger = await NewUserAsync();

        Assert.Empty(await RulesAsync(stranger));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await stranger.DeleteAsync($"/api/v1/automations/{rule.Id}")).StatusCode);
    }
}
