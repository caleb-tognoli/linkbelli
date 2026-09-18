using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Linkbelli.Application.Data;
using Linkbelli.Application.Webhooks;
using Linkbelli.Core.Entities;
using Linkbelli.Core.Webhooks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Linkbelli.IntegrationTests.ApiTestHelpers;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Telling other things what happened.
/// </summary>
/// <remarks>
/// Linkbelli was built to be written to — feeds, scrapers, an inbox address, an email worker —
/// and could tell nothing else what happened next. "A link was saved" is exactly what people hang
/// a Home Assistant automation, a chat bot or a site rebuild off.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class WebhookTests(PostgresApiFactory factory)
{
    private record HookDto(
        Guid Id, string Url, string? Description, string[] Events, string Status, int ConsecutiveFailures,
        string? DisabledReason);

    private record CreatedDto(HookDto Webhook, string Secret);

    private record DeliveryDto(Guid Id, string Event, string Status, int Attempts, int? ResponseStatus, string? Error);

    private record ItemRef(Guid Id, LinkRef Link);

    private record LinkRef(Guid Id);

    private record PagedItems(List<ItemRef> Items);

    private record SourceDto(Guid Id, string? WebhookToken);

    private static string NewReceiver() => $"https://hooks.test/{Guid.NewGuid():N}";

    private async Task<HttpClient> NewUserAsync()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", await client.RegisterAndLoginAsync(NewUsername()));
        return client;
    }

    private static async Task<Guid> NewPlaylistAsync(HttpClient client, string? name = null)
    {
        var res = await client.PostAsJsonAsync("/api/v1/playlists", new { name = name ?? $"Hooked {Guid.NewGuid():N}" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<PlaylistDto>())!.Id;
    }

    private static async Task<CreatedDto> NewHookAsync(HttpClient client, string url, params string[] events)
    {
        var res = await client.PostAsJsonAsync("/api/v1/me/webhooks", new
        {
            url,
            events = events.Length == 0 ? [WebhookEvents.ItemsAdded] : events,
        });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<CreatedDto>())!;
    }

    private async Task<List<WebhookDelivery>> DeliveriesAsync(Guid webhookId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.WebhookId == webhookId)
            .OrderBy(d => d.CreationTime)
            .ToListAsync();
    }

    private async Task DeliverAsync(Guid deliveryId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>().DeliverAsync(deliveryId);
    }

    /// <summary>Sends one delivery until it stops being retried, as the schedule would.</summary>
    private async Task DeliverUntilSettledAsync(Guid deliveryId)
    {
        for (var i = 0; i < WebhookDelivery.MaxAttempts; i++)
        {
            await DeliverAsync(deliveryId);
            var status = (await DeliveriesByIdAsync(deliveryId)).Status;
            if (status != WebhookDeliveryStatus.Retrying)
            {
                return;
            }
        }
    }

    private async Task<WebhookDelivery> DeliveriesByIdAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.WebhookDeliveries.AsNoTracking().FirstAsync(d => d.Id == id);
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid playlist, string? url = null) =>
        await client.PostAsJsonAsync($"/api/v1/playlists/{playlist}/items",
            new { url = url ?? $"https://example.org/{Guid.NewGuid():N}" });

    private static JsonElement Body(WebhookDelivery delivery) => JsonDocument.Parse(delivery.Payload).RootElement;

    // --- Managing them ---

    [Fact]
    public async Task A_new_webhook_hands_back_its_secret_once()
    {
        var client = await NewUserAsync();

        var created = await NewHookAsync(client, NewReceiver());

        Assert.StartsWith(WebhookSignature.SecretPrefix, created.Secret);
        Assert.Equal("Active", created.Webhook.Status);

        var listed = await client.GetStringAsync("/api/v1/me/webhooks");
        Assert.DoesNotContain(created.Secret, listed);
        Assert.DoesNotContain("secret", listed, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ftp://hooks.test/x")]
    [InlineData("/relative")]
    [InlineData("not a url")]
    [InlineData("http://127.0.0.1:8123/api/webhook/x")]
    [InlineData("http://localhost/hook")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task An_address_it_will_not_send_to_is_refused_with_a_reason(string url)
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/me/webhooks", new { url, events = new[] { WebhookEvents.ItemsAdded } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// A home network is exactly where people keep their receivers, and exactly what the server
    /// should not be sending into unless whoever runs it says so.
    /// </summary>
    [Fact]
    public async Task A_private_address_needs_the_operator_to_allow_it()
    {
        var client = await NewUserAsync();

        var res = await client.PostAsJsonAsync("/api/v1/me/webhooks",
            new { url = "http://192.168.1.10:8123/api/webhook/linkbelli", events = new[] { WebhookEvents.ItemsAdded } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("whoever runs this server", await res.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("items.exploded")]
    [InlineData("ping")] // sent by the Test button, never subscribed to
    public async Task It_has_to_be_subscribed_to_something_real(string requested)
    {
        var client = await NewUserAsync();
        var events = requested.Split(',', StringSplitOptions.RemoveEmptyEntries);

        var res = await client.PostAsJsonAsync("/api/v1/me/webhooks", new { url = NewReceiver(), events });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task There_is_a_limit_per_account()
    {
        var client = await NewUserAsync();
        for (var i = 0; i < Webhook.MaxPerOwner; i++)
        {
            await NewHookAsync(client, NewReceiver());
        }

        var res = await client.PostAsJsonAsync("/api/v1/me/webhooks",
            new { url = NewReceiver(), events = new[] { WebhookEvents.ItemsAdded } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    /// <summary>
    /// A webhook forwards everything from here on to an address of the caller's choosing. A key
    /// limited to reading one thing must not be able to turn itself into a copy of everything.
    /// </summary>
    [Fact]
    public async Task An_api_key_cannot_set_one_up()
    {
        var owner = await NewUserAsync();
        var keyRes = await owner.PostAsJsonAsync("/api/v1/me/apikeys",
            new { name = $"key-{Guid.NewGuid():N}", scopes = new[] { "playlists:read", "playlists:write" } });
        var key = (await keyRes.Content.ReadFromJsonAsync<ApiKeyCreatedDto>())!;

        var keyed = factory.CreateClient();
        keyed.DefaultRequestHeaders.Add("X-Api-Key", key.Token);

        var res = await keyed.PostAsJsonAsync("/api/v1/me/webhooks",
            new { url = NewReceiver(), events = new[] { WebhookEvents.ItemsAdded } });

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Somebody_elses_webhook_is_not_there_to_see()
    {
        var owner = await NewUserAsync();
        var stranger = await NewUserAsync();
        var hook = (await NewHookAsync(owner, NewReceiver())).Webhook;

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/me/webhooks/{hook.Id}/deliveries")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync($"/api/v1/me/webhooks/{hook.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await stranger.PatchAsJsonAsync($"/api/v1/me/webhooks/{hook.Id}", new { url = NewReceiver() })).StatusCode);
    }

    // --- What gets sent ---

    [Fact]
    public async Task Saving_a_link_tells_the_webhook_signed_and_labelled()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client, "Reading");
        var receiver = NewReceiver();
        var created = await NewHookAsync(client, receiver);
        var url = $"https://example.org/{Guid.NewGuid():N}";

        (await SaveAsync(client, playlist, url)).EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        Assert.Contains(delivery.Id, factory.WebhookQueue.Enqueued);

        await DeliverAsync(delivery.Id);

        var sent = Assert.Single(factory.WebhookReceiver.At(receiver));
        Assert.Equal(WebhookEvents.ItemsAdded, sent.Headers["Linkbelli-Event"]);
        Assert.Equal(delivery.Id.ToString(), sent.Headers["Linkbelli-Delivery"]);
        Assert.StartsWith("application/json", sent.Headers["Content-Type"]);
        Assert.True(WebhookSignature.Verify(
            created.Secret, sent.Body, sent.Headers[WebhookSignature.Header], DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));

        var body = JsonDocument.Parse(sent.Body).RootElement;
        Assert.Equal(delivery.Id, body.GetProperty("id").GetGuid());
        Assert.Equal(WebhookEvents.ItemsAdded, body.GetProperty("event").GetString());
        var data = body.GetProperty("data");
        Assert.Equal("manual", data.GetProperty("via").GetString());
        Assert.Equal("Reading", data.GetProperty("playlist").GetProperty("name").GetString());
        Assert.Equal(url, Assert.Single(data.GetProperty("items").EnumerateArray()).GetProperty("url").GetString());

        Assert.Equal(WebhookDeliveryStatus.Delivered, (await DeliveriesByIdAsync(delivery.Id)).Status);
    }

    /// <summary>The signature is over the body that was stored, so a retry verifies too.</summary>
    [Fact]
    public async Task A_signature_made_with_another_secret_does_not_verify()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        var created = await NewHookAsync(client, receiver);
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();

        await DeliverAsync(Assert.Single(await DeliveriesAsync(created.Webhook.Id)).Id);

        var sent = Assert.Single(factory.WebhookReceiver.At(receiver));
        Assert.False(WebhookSignature.Verify(
            WebhookSignature.NewSecret(), sent.Body, sent.Headers[WebhookSignature.Header], DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task Nothing_is_sent_for_an_event_it_did_not_ask_for()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var created = await NewHookAsync(client, NewReceiver(), WebhookEvents.ItemsFinished);

        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();

        Assert.Empty(await DeliveriesAsync(created.Webhook.Id));
    }

    [Fact]
    public async Task A_paused_webhook_hears_nothing()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var created = await NewHookAsync(client, NewReceiver());
        (await client.PatchAsJsonAsync($"/api/v1/me/webhooks/{created.Webhook.Id}", new { active = false }))
            .EnsureSuccessStatusCode();

        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();

        Assert.Empty(await DeliveriesAsync(created.Webhook.Id));
    }

    [Fact]
    public async Task Somebody_elses_saves_do_not_reach_your_webhook()
    {
        var mine = await NewUserAsync();
        var created = await NewHookAsync(mine, NewReceiver());

        var theirs = await NewUserAsync();
        (await SaveAsync(theirs, await NewPlaylistAsync(theirs))).EnsureSuccessStatusCode();

        Assert.Empty(await DeliveriesAsync(created.Webhook.Id));
    }

    /// <summary>
    /// A source run that finds forty links is one event with forty items, not forty requests — a
    /// chat bot posting each one separately is how a webhook gets switched off by whoever reads it.
    /// </summary>
    [Fact]
    public async Task A_source_run_is_one_event_however_much_it_found()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var created = await NewHookAsync(client, NewReceiver());

        var res = await client.PostAsJsonAsync("/api/v1/sources", new
        {
            name = $"Pushed {Guid.NewGuid():N}",
            type = "Webhook",
            config = new { },
            schedule = "",
            playlistIds = new[] { playlist },
        });
        res.EnsureSuccessStatusCode();
        var source = (await res.Content.ReadFromJsonAsync<SourceDto>())!;

        var links = Enumerable.Range(0, 3).Select(_ => new { url = $"https://pushed.example/{Guid.NewGuid():N}" }).ToArray();
        (await factory.CreateClient().PostAsJsonAsync($"/api/v1/hooks/{source.WebhookToken}", new { links }))
            .EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        var data = Body(delivery).GetProperty("data");
        Assert.Equal("source", data.GetProperty("via").GetString());
        Assert.Equal(source.Id, data.GetProperty("source").GetProperty("id").GetGuid());
        Assert.Equal(3, data.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Finishing_something_is_its_own_event()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var item = (await factory.SeedEnrichedItemsAsync(playlist, 1))[0];
        var created = await NewHookAsync(client, NewReceiver(), WebhookEvents.ItemsFinished);

        (await client.PatchAsJsonAsync($"/api/v1/items/{item}", new { status = "Watched" })).EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        Assert.Equal(WebhookEvents.ItemsFinished, delivery.Event);
        Assert.Equal(item, Assert.Single(Body(delivery).GetProperty("data").GetProperty("items").EnumerateArray())
            .GetProperty("id").GetGuid());
    }

    /// <summary>
    /// Sending the whole set of tags back is how a client removes one. The tags it kept are not
    /// news, and announcing them again would fire every "tagged to-read" automation twice.
    /// </summary>
    [Fact]
    public async Task Only_a_tag_that_is_new_is_announced()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var item = (await factory.SeedEnrichedItemsAsync(playlist, 1))[0];

        (await client.PatchAsJsonAsync($"/api/v1/items/{item}", new { tags = new[] { "kept" } })).EnsureSuccessStatusCode();

        var created = await NewHookAsync(client, NewReceiver(), WebhookEvents.ItemsTagged);
        (await client.PatchAsJsonAsync($"/api/v1/items/{item}", new { tags = new[] { "kept", "to-read" } }))
            .EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        Assert.Equal("to-read", Body(delivery).GetProperty("data").GetProperty("tag").GetString());
    }

    [Fact]
    public async Task A_rule_filing_a_link_somewhere_announces_it_arriving_there()
    {
        var client = await NewUserAsync();
        var inbox = await NewPlaylistAsync(client, "Inbox");
        var later = await NewPlaylistAsync(client, "Later");
        await factory.SeedEnrichedItemsAsync(inbox, 2);
        var created = await NewHookAsync(client, NewReceiver(), WebhookEvents.ItemsAdded, WebhookEvents.ItemsTagged);

        var ruleRes = await client.PostAsJsonAsync("/api/v1/automations", new
        {
            name = $"File it {Guid.NewGuid():N}",
            playlistId = inbox,
            copyToPlaylistId = later,
            addTags = new[] { "filed" },
        });
        ruleRes.EnsureSuccessStatusCode();
        var ruleId = (await ruleRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await client.PostAsJsonAsync($"/api/v1/automations/{ruleId}/run", new { playlistId = inbox }))
            .EnsureSuccessStatusCode();

        var deliveries = await DeliveriesAsync(created.Webhook.Id);

        var added = Assert.Single(deliveries, d => d.Event == WebhookEvents.ItemsAdded);
        var data = Body(added).GetProperty("data");
        Assert.Equal("rule", data.GetProperty("via").GetString());
        Assert.Equal("Later", data.GetProperty("playlist").GetProperty("name").GetString());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());

        var tagged = Assert.Single(deliveries, d => d.Event == WebhookEvents.ItemsTagged);
        Assert.Equal("filed", Body(tagged).GetProperty("data").GetProperty("tag").GetString());
    }

    [Fact]
    public async Task Marking_a_passage_is_announced_with_the_words()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        await factory.SeedEnrichedItemsAsync(playlist, 1);
        var linkId = (await client.GetFromJsonAsync<PagedItems>($"/api/v1/playlists/{playlist}/items"))!.Items[0].Link.Id;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            await db.Links.Where(l => l.Id == linkId)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.Content, "Headline\n\nThe part worth keeping."));
        }

        var created = await NewHookAsync(client, NewReceiver(), WebhookEvents.HighlightCreated);
        (await client.PostAsJsonAsync($"/api/v1/links/{linkId}/highlights", new { paragraphIndex = 1, start = 0, end = 22 }))
            .EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        Assert.Equal("The part worth keeping", Body(delivery).GetProperty("data").GetProperty("highlight").GetProperty("text").GetString());
    }

    // --- When it does not arrive ---

    [Fact]
    public async Task A_failure_is_retried_later_rather_than_straight_away()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        factory.WebhookReceiver.Answer(receiver, HttpStatusCode.InternalServerError);
        var created = await NewHookAsync(client, receiver);
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        await DeliverAsync(delivery.Id);

        var after = await DeliveriesByIdAsync(delivery.Id);
        Assert.Equal(WebhookDeliveryStatus.Retrying, after.Status);
        Assert.Equal(500, after.ResponseStatus);
        Assert.Contains("receiver says no", after.Error);
        Assert.Contains(factory.WebhookQueue.Scheduled, s => s.Id == delivery.Id && s.Delay == WebhookDispatcher.Backoff[0]);
    }

    [Fact]
    public async Task A_delivery_gives_up_after_its_attempts_and_counts_against_the_webhook()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        factory.WebhookReceiver.Answer(receiver, HttpStatusCode.BadGateway);
        var created = await NewHookAsync(client, receiver);
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        await DeliverUntilSettledAsync(delivery.Id);

        var after = await DeliveriesByIdAsync(delivery.Id);
        Assert.Equal(WebhookDeliveryStatus.Failed, after.Status);
        Assert.Equal(WebhookDelivery.MaxAttempts, after.Attempts);

        var hook = Assert.Single(await client.GetFromJsonAsync<List<HookDto>>("/api/v1/me/webhooks") ?? []);
        Assert.Equal(1, hook.ConsecutiveFailures);
        Assert.Equal("Active", hook.Status);
    }

    /// <summary>
    /// Each delivery already retries for hours on its own; a run of them giving up is a receiver
    /// that is gone, and calling it forever is noise in somebody else's logs.
    /// </summary>
    [Fact]
    public async Task Enough_failed_deliveries_in_a_row_switch_it_off_and_turning_it_on_starts_again()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        factory.WebhookReceiver.Answer(receiver, HttpStatusCode.ServiceUnavailable);
        var created = await NewHookAsync(client, receiver);

        for (var i = 0; i < Webhook.FailureThreshold; i++)
        {
            (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();
        }

        foreach (var delivery in await DeliveriesAsync(created.Webhook.Id))
        {
            await DeliverUntilSettledAsync(delivery.Id);
        }

        var hook = Assert.Single(await client.GetFromJsonAsync<List<HookDto>>("/api/v1/me/webhooks") ?? []);
        Assert.Equal("Disabled", hook.Status);
        Assert.NotNull(hook.DisabledReason);

        // Off means off: the next save is not queued for it.
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();
        Assert.Equal(Webhook.FailureThreshold, (await DeliveriesAsync(created.Webhook.Id)).Count);

        var res = await client.PatchAsJsonAsync($"/api/v1/me/webhooks/{created.Webhook.Id}", new { active = true });
        var back = (await res.Content.ReadFromJsonAsync<HookDto>())!;
        Assert.Equal("Active", back.Status);
        Assert.Equal(0, back.ConsecutiveFailures);
        Assert.Null(back.DisabledReason);
    }

    /// <summary>410 is the one unambiguous thing a receiver can say: this address is finished with.</summary>
    [Fact]
    public async Task A_receiver_that_says_gone_is_believed()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        factory.WebhookReceiver.Answer(receiver, HttpStatusCode.Gone);
        var created = await NewHookAsync(client, receiver);
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();

        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        await DeliverAsync(delivery.Id);

        Assert.Equal(WebhookDeliveryStatus.Failed, (await DeliveriesByIdAsync(delivery.Id)).Status);
        var hook = Assert.Single(await client.GetFromJsonAsync<List<HookDto>>("/api/v1/me/webhooks") ?? []);
        Assert.Equal("Disabled", hook.Status);
        Assert.Contains("410", hook.DisabledReason);
    }

    /// <summary>Nothing leaves for an address its owner has just said to stop using.</summary>
    [Fact]
    public async Task Deleting_a_webhook_cancels_what_was_still_waiting()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        var created = await NewHookAsync(client, receiver);
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();
        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));

        (await client.DeleteAsync($"/api/v1/me/webhooks/{created.Webhook.Id}")).EnsureSuccessStatusCode();
        await DeliverAsync(delivery.Id);

        Assert.Empty(factory.WebhookReceiver.At(receiver));
        Assert.Equal(WebhookDeliveryStatus.Cancelled, (await DeliveriesByIdAsync(delivery.Id)).Status);
    }

    /// <summary>
    /// The point of testing a disabled webhook is to see whether the receiver is fixed before
    /// turning it back on, so a test goes regardless — and does not move the failure count.
    /// </summary>
    [Fact]
    public async Task A_test_goes_even_to_a_paused_webhook()
    {
        var client = await NewUserAsync();
        var receiver = NewReceiver();
        var created = await NewHookAsync(client, receiver);
        (await client.PatchAsJsonAsync($"/api/v1/me/webhooks/{created.Webhook.Id}", new { active = false }))
            .EnsureSuccessStatusCode();

        var res = await client.PostAsync($"/api/v1/me/webhooks/{created.Webhook.Id}/test", null);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var queued = (await res.Content.ReadFromJsonAsync<DeliveryDto>())!;
        Assert.Equal(WebhookEvents.Ping, queued.Event);

        await DeliverAsync(queued.Id);

        var sent = Assert.Single(factory.WebhookReceiver.At(receiver));
        Assert.Equal(WebhookEvents.Ping, sent.Headers["Linkbelli-Event"]);
        Assert.Equal(WebhookDeliveryStatus.Delivered, (await DeliveriesByIdAsync(queued.Id)).Status);
    }

    [Fact]
    public async Task A_delivery_can_be_sent_again_exactly_as_it_was()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        var created = await NewHookAsync(client, receiver);
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();
        var original = Assert.Single(await DeliveriesAsync(created.Webhook.Id));
        await DeliverAsync(original.Id);

        var res = await client.PostAsync($"/api/v1/me/webhooks/deliveries/{original.Id}/redeliver", null);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        var copy = (await res.Content.ReadFromJsonAsync<DeliveryDto>())!;
        await DeliverAsync(copy.Id);

        var sent = factory.WebhookReceiver.At(receiver);
        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0].Body, sent[1].Body);

        var log = await client.GetFromJsonAsync<List<DeliveryDto>>($"/api/v1/me/webhooks/{created.Webhook.Id}/deliveries");
        Assert.Equal(2, log!.Count);
        Assert.Equal(copy.Id, log[0].Id);
    }

    [Fact]
    public async Task A_rotated_secret_signs_what_goes_next()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        var created = await NewHookAsync(client, receiver);

        var rotated = (await (await client.PostAsync($"/api/v1/me/webhooks/{created.Webhook.Id}/secret", null))
            .Content.ReadFromJsonAsync<CreatedDto>())!;
        Assert.NotEqual(created.Secret, rotated.Secret);

        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();
        await DeliverAsync(Assert.Single(await DeliveriesAsync(created.Webhook.Id)).Id);

        var sent = Assert.Single(factory.WebhookReceiver.At(receiver));
        var header = sent.Headers[WebhookSignature.Header];
        Assert.True(WebhookSignature.Verify(rotated.Secret, sent.Body, header, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
        Assert.False(WebhookSignature.Verify(created.Secret, sent.Body, header, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5)));
    }

    /// <summary>A job can run twice. A delivery that already arrived is not sent a second time.</summary>
    [Fact]
    public async Task Running_a_finished_delivery_again_sends_nothing()
    {
        var client = await NewUserAsync();
        var playlist = await NewPlaylistAsync(client);
        var receiver = NewReceiver();
        var created = await NewHookAsync(client, receiver);
        (await SaveAsync(client, playlist)).EnsureSuccessStatusCode();
        var delivery = Assert.Single(await DeliveriesAsync(created.Webhook.Id));

        await DeliverAsync(delivery.Id);
        await DeliverAsync(delivery.Id);

        Assert.Single(factory.WebhookReceiver.At(receiver));
    }

    [Fact]
    public async Task The_events_are_listed_with_what_they_mean()
    {
        var client = await NewUserAsync();

        var events = await client.GetFromJsonAsync<List<JsonElement>>("/api/v1/me/webhooks/events") ?? [];

        Assert.Equal(WebhookEvents.Subscribable, events.Select(e => e.GetProperty("name").GetString()!).ToList());
        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("description").GetString())));
    }
}
