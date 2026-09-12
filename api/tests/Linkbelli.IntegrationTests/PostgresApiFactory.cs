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
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Database:MigrateAtStartup", "true");

        // A host, so the app believes mail is configured and the features that check are on.
        // The sender is replaced below, so nothing is actually connected to.
        builder.UseSetting("Email:Host", "test.invalid");
        builder.UseSetting("Email:PublicUrl", "https://linkbelli.test");

        // Every anonymous test request shares one rate-limit partition (this host's IP), so the
        // shipped limit of 10/min would make a dozen mail tests fight each other rather than test
        // anything. Raised, not removed — RateLimitTests still proves the limiter applies.
        builder.UseSetting("RateLimits:SensitivePerMinute", SensitivePerMinute.ToString());

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
