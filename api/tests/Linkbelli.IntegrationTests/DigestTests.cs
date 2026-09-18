using System.Net.Http.Headers;
using System.Net.Http.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Email;
using Linkbelli.Application.Enrichment;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// The weekly summary. The thing worth testing hardest is when it does *not* go out: a digest
/// about a week in which nothing happened is the message people unsubscribe from.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class DigestTests(PostgresApiFactory factory)
{
    private record PlaylistViewDto(Guid Id, string Name);

    private async Task<(HttpClient Client, Guid UserId, string Email)> NewUserAsync()
    {
        var client = factory.CreateClient();
        var username = NewUsername();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(username));

        // Nothing is mailed to an address nobody has proved they own, and these tests are all
        // about what arrives.
        await factory.ConfirmEmailAsync(username);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var id = await db.Users.Where(u => u.UserName == username).Select(u => u.Id).FirstAsync();

        return (client, id, $"{username}@example.com");
    }

    private static async Task<PlaylistViewDto> NewPlaylistAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name, visibility = "Private" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistViewDto>())!;
    }

    private async Task SubscribeAsync(HttpClient client) =>
        (await client.PutAsJsonAsync("/api/v1/notifications", new { weeklyDigest = true }))
            .EnsureSuccessStatusCode();

    /// <summary>Marks every account as just-sent, so a test can say which one is due.</summary>
    private async Task QuietEveryoneAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.DigestSentAt, DateTimeOffset.UtcNow));
    }

    private async Task SetDueAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DigestSentAt, (DateTimeOffset?)null));
    }

    private async Task<int> SweepAsync()
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IDigestSweep>().SweepAsync();
    }

    private async Task<DateTimeOffset?> SentAtAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Users.Where(u => u.Id == userId).Select(u => u.DigestSentAt).FirstAsync();
    }

    [Fact]
    public async Task A_subscriber_with_a_week_behind_them_gets_one()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var playlist = await NewPlaylistAsync(client, $"Weekly {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 3);

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        var mail = factory.Email.LastTo(email);
        Assert.NotNull(mail);
        Assert.Contains("Your week", mail!.Subject);
        Assert.Contains("3 links arrived", mail.TextBody);
    }

    [Fact]
    public async Task A_week_in_which_nothing_happened_sends_nothing()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        // A playlist, but nothing added to it.
        await NewPlaylistAsync(client, $"Empty {Guid.NewGuid():N}");

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        // "Nothing new arrived this week" is not worth an inbox, and sending it anyway is how a
        // digest becomes the message people unsubscribe from.
        Assert.Null(factory.Email.LastTo(email));
        // Still stamped, so the account is not reconsidered every hour for a week.
        Assert.NotNull(await SentAtAsync(userId));
    }

    [Fact]
    public async Task Somebody_who_did_not_ask_gets_nothing()
    {
        var (client, userId, email) = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, $"Unsubscribed {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        // The digest is opt-in, so a brand-new account is not subscribed.
        Assert.Null(factory.Email.LastTo(email));
    }

    [Fact]
    public async Task An_account_sent_one_this_week_is_left_alone()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var playlist = await NewPlaylistAsync(client, $"Recent {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        await QuietEveryoneAsync();
        factory.Email.Clear();
        await SweepAsync();

        Assert.Null(factory.Email.LastTo(email));
    }

    [Fact]
    public async Task The_digest_names_some_of_what_arrived()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var playlist = await NewPlaylistAsync(client, $"Named {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 2);

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        var mail = factory.Email.LastTo(email)!;
        Assert.Contains("Some of what came in:", mail.TextBody);
        // A link, so it can be opened straight from the message.
        Assert.Contains("http", mail.TextBody);
    }

    /// <summary>
    /// Reading an old backlog adds nothing and marks a great deal. A digest that only counted
    /// arrivals would call that week empty and stay silent about the only thing that happened.
    /// </summary>
    [Fact]
    public async Task A_week_of_marking_passages_is_not_a_quiet_week()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var playlist = await NewPlaylistAsync(client, $"Backlog {Guid.NewGuid():N}");
        var items = await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();

            // Saved long ago: nothing arrived this week.
            await db.PlaylistItems.Where(i => i.Id == items[0])
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.CreationTime, DateTimeOffset.UtcNow.AddDays(-60)));

            var linkId = await db.PlaylistItems.Where(i => i.Id == items[0]).Select(i => i.LinkId).FirstAsync();
            await factory.SetArticleTextAsync(
                linkId, "Headline" + ArticleExtractor.ParagraphSeparator + "A sentence worth keeping.");

            (await client.PostAsJsonAsync($"/api/v1/links/{linkId}/highlights", new
            {
                paragraphIndex = 1,
                start = 0,
                end = 25,
                text = "",
            })).EnsureSuccessStatusCode();
        }

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        var mail = factory.Email.LastTo(email);
        Assert.NotNull(mail);
        Assert.Contains("Nothing new arrived", mail!.TextBody);
        Assert.Contains("“A sentence worth keeping.”", mail.TextBody);
    }

    [Fact]
    public async Task It_carries_a_way_to_stop_it()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var playlist = await NewPlaylistAsync(client, $"Stoppable {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1);

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        var mail = factory.Email.LastTo(email)!;
        Assert.Contains("/unsubscribe?token=", mail.TextBody);
        Assert.Contains("the weekly summary", mail.TextBody);
    }

    [Fact]
    public async Task A_preview_arrives_even_in_a_quiet_week()
    {
        var (client, _, email) = await NewUserAsync();
        // Deliberately not subscribed and with nothing added: somebody deciding whether to
        // subscribe should be able to see one rather than wait a week to find out.
        factory.Email.Clear();

        (await client.PostAsync("/api/v1/notifications/digest/preview", null))
            .EnsureSuccessStatusCode();

        var mail = factory.Email.LastTo(email);
        Assert.NotNull(mail);
        Assert.Contains("Nothing new arrived", mail!.TextBody);
    }

    [Fact]
    public async Task A_preview_does_not_count_as_the_weekly_one()
    {
        var (client, userId, _) = await NewUserAsync();
        await SubscribeAsync(client);

        (await client.PostAsync("/api/v1/notifications/digest/preview", null))
            .EnsureSuccessStatusCode();

        // Otherwise asking to see one would silently cost somebody their next real digest.
        Assert.Null(await SentAtAsync(userId));
    }

    [Fact]
    public async Task The_same_link_in_two_playlists_is_only_named_once()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var shared = $"https://seed.example/shared-{Guid.NewGuid():N}";

        // The same address in two playlists, which is an ordinary thing to do — and which the
        // seeder reproduces faithfully, reusing the one globally deduplicated link row.
        var first = await NewPlaylistAsync(client, $"First {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(first.Id, 1, url: _ => shared);
        var second = await NewPlaylistAsync(client, $"Second {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(second.Id, 1, url: _ => shared);

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        var body = factory.Email.LastTo(email)!.TextBody;
        var mentions = body.Split('\n')
            .Count(l => l.TrimStart().StartsWith("\u2022") && l.Contains(shared));

        // Listing one page twice reads as a bug rather than as a busy week.
        Assert.Equal(1, mentions);
    }

    [Fact]
    public async Task An_untitled_link_is_not_printed_twice()
    {
        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var playlist = await NewPlaylistAsync(client, $"Untitled {Guid.NewGuid():N}");

        // A scraped page with nothing usable for a title, which happens often enough.
        await factory.SeedEnrichedItemsAsync(playlist.Id, 1, title: _ => "   ");

        await QuietEveryoneAsync();
        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        var body = factory.Email.LastTo(email)!.TextBody;
        var line = body.Split('\n').First(l => l.TrimStart().StartsWith("\u2022"));

        // "https://x - https://x" is worse than printing the address once.
        Assert.DoesNotContain("\u2014", line);
    }

    [Fact]
    public async Task Another_persons_library_is_not_in_it()
    {
        var (mine, myId, myEmail) = await NewUserAsync();
        await SubscribeAsync(mine);
        var myPlaylist = await NewPlaylistAsync(mine, $"Mine {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(myPlaylist.Id, 1);

        var (theirs, _, _) = await NewUserAsync();
        var theirName = $"Theirs {Guid.NewGuid():N}";
        var theirPlaylist = await NewPlaylistAsync(theirs, theirName);
        await factory.SeedEnrichedItemsAsync(theirPlaylist.Id, 4);

        await QuietEveryoneAsync();
        await SetDueAsync(myId);
        factory.Email.Clear();
        await SweepAsync();

        var mail = factory.Email.LastTo(myEmail)!;
        Assert.Contains("1 link arrived", mail.TextBody);
        Assert.DoesNotContain(theirName, mail.TextBody);
    }

    [Fact]
    public async Task An_account_that_has_never_had_one_is_reached_before_accounts_that_have()
    {
        var crowd = new List<Guid>();
        for (var i = 0; i < DigestSweep.BatchSize; i++)
        {
            var (member, memberId, _) = await NewUserAsync();
            await SubscribeAsync(member);
            var pl = await NewPlaylistAsync(member, $"Crowd {i} {Guid.NewGuid():N}");
            await factory.SeedEnrichedItemsAsync(pl.Id, 1);
            crowd.Add(memberId);
        }

        var (client, userId, email) = await NewUserAsync();
        await SubscribeAsync(client);
        var mine = await NewPlaylistAsync(client, $"Never {Guid.NewGuid():N}");
        await factory.SeedEnrichedItemsAsync(mine.Id, 1);

        // A full batch already waiting, each with a digest behind it, and one account with none.
        await QuietEveryoneAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await db.Users.Where(u => crowd.Contains(u.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(
                    u => u.DigestSentAt, DateTimeOffset.UtcNow.AddDays(-DigestSweep.WindowDays - 30)));
        }

        await SetDueAsync(userId);
        factory.Email.Clear();
        await SweepAsync();

        // Postgres sorts NULLs last, so ordering on the column alone would put the account that
        // has never had one behind every account that already has.
        Assert.NotNull(factory.Email.LastTo(email));
    }
}
