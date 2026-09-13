using System.Collections.Concurrent;
using Linkbelli.Application.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace Linkbelli.IntegrationTests;

/// <summary>
/// Boots the real API against a throwaway PostgreSQL container, applying migrations at startup.
/// Runs in Development so HTTPS redirection is off (tests talk plain HTTP).
/// </summary>
public sealed class PostgresApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The image goes to the constructor rather than to WithImage: the parameterless form is
    // deprecated, and passing it here is what lets the builder know which defaults apply.
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:17-alpine")
        // Postgres defaults to 100 connections, which this suite outgrew: several test classes
        // boot their own WebApplicationFactory per test — each with its own connection pool and
        // its own Hangfire server — and the failure that follows is "53300: sorry, too many
        // clients already" on whichever test happens to run when the last one is taken.
        .WithCommand("-c", "max_connections=500")
        .Build();

    public async Task InitializeAsync() => await _db.StartAsync();

    // Explicit so it doesn't clash with WebApplicationFactory's ValueTask DisposeAsync.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>Every message the app tried to send, in order.</summary>
    public RecordingEmailSender Email { get; } = new();

    /// <summary>
    /// The "sensitive" allowance this suite runs with, shared so the test that proves the limiter
    /// works knows how far it has to push to reach it.
    /// </summary>
    public const int SensitivePerMinute = 120;

    /// <summary>
    /// The "credentials" allowance this suite runs with.
    /// </summary>
    /// <remarks>
    /// Every test registers a user and signs in, and they all share one anonymous partition, so
    /// the shipped twenty a minute would fail the suite rather than test anything. Raised for the
    /// same reason as the one above, and by enough that adding tests does not quietly re-break it.
    /// </remarks>
    public const int CredentialsPerMinute = 5000;

    /// <summary>
    /// The global burst this suite runs with.
    /// </summary>
    /// <remarks>
    /// The shipped 300 sustains 900 requests a minute per partition, and every anonymous request
    /// in the suite — every register, every login, every public page — shares one. A full run
    /// passes that, so the limiter would start refusing whichever tests happened to run last,
    /// which is how this arrived: a handful of failures that moved between runs and passed
    /// whenever the class was run on its own.
    /// </remarks>
    public const int GlobalBurst = 20_000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // A small pool per app, for the same reason: an app booted for one test does not need
        // Npgsql's default hundred, and several of them at once is what exhausts the server.
        builder.UseSetting(
            "ConnectionStrings:Default",
            $"{_db.GetConnectionString()};Maximum Pool Size=12;Connection Idle Lifetime=10");
        builder.UseSetting("Database:MigrateAtStartup", "true");

        // A host, so the app believes mail is configured and the features that check are on.
        // The sender is replaced below, so nothing is actually connected to.
        builder.UseSetting("Email:Host", "test.invalid");
        builder.UseSetting("Email:PublicUrl", "https://linkbelli.test");

        // Every anonymous test request shares one rate-limit partition (this host's IP), so the
        // shipped limit of 10/min would make a dozen mail tests fight each other rather than test
        // anything. Raised, not removed — RateLimitTests still proves the limiter applies.
        builder.UseSetting("RateLimits:SensitivePerMinute", SensitivePerMinute.ToString());
        builder.UseSetting("RateLimits:CredentialsPerMinute", CredentialsPerMinute.ToString());
        builder.UseSetting("RateLimits:GlobalBurst", GlobalBurst.ToString());

        builder.ConfigureServices(services =>
        {
            // Captured rather than sent. Asserting on what somebody would have received is the
            // only way to test these features, and it needs no SMTP server to do it.
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
        });
    }
}

/// <summary>
/// Keeps every message instead of sending it, so tests can read what a person would have got.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>Set to make sending fail, for the paths that have to cope with that.</summary>
    public bool Fail { get; set; }

    public bool IsConfigured { get; set; } = true;

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];

    public Task<bool> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        if (Fail) return Task.FromResult(false);

        _sent.Enqueue(message);
        return Task.FromResult(true);
    }

    public void Clear() => _sent.Clear();

    /// <summary>The most recent message to this address, or null.</summary>
    public EmailMessage? LastTo(string address) =>
        _sent.Where(m => m.To.Equals(address, StringComparison.OrdinalIgnoreCase)).LastOrDefault();
}

/// <summary>Shared single container/app for all integration tests; serialized to keep the rate-limit test deterministic.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresApiFactory>
{
    public const string Name = "integration";
}
