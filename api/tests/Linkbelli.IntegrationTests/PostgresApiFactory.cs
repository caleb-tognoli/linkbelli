using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _db.GetConnectionString());
        builder.UseSetting("Database:MigrateAtStartup", "true");
    }
}

/// <summary>Shared single container/app for all integration tests; serialized to keep the rate-limit test deterministic.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationCollection : ICollectionFixture<PostgresApiFactory>
{
    public const string Name = "integration";
}
