using Hangfire;
using Npgsql;
using Hangfire.PostgreSql;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Email;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Security;
using Linkbelli.Application.Services;
using Linkbelli.Application.Sources;
using Linkbelli.Application.Webhooks;
using Linkbelli.Infrastructure.Email;
using Linkbelli.Infrastructure.Jobs;
using Linkbelli.Infrastructure.Search;
using Linkbelli.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers all persistence + Identity + background-job wiring. The single place the
    /// composition root needs to know about Infrastructure concretions.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");

        var dataSource = new NpgsqlDataSourceBuilder(connectionString).EnableDynamicJson().Build();
        services.AddDbContext<LinkbelliDbContext>(options => options
            .UseNpgsql(dataSource)
            .ConfigureWarnings(w => w
                // LinkContent shares Link's row and has one column of its own, which is null when a
                // page had no article. EF warns that it cannot tell "no article" from "not there" —
                // which is exactly the point: they mean the same thing.
                .Ignore(RelationalEventId.OptionalDependentWithoutIdentifyingPropertyWarning)
                // And that Link is filtered while LinkContent is not. It is the same row, only
                // reached through a Link, so Link's filter is the one that applies.
                .Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

        // Expose the same scoped DbContext instance as the Application's abstraction.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<LinkbelliDbContext>());

        services.AddHealthChecks().AddDbContextCheck<LinkbelliDbContext>("database");

        // Protects Identity bearer/refresh tokens AND encrypts source-config secrets at rest.
        // The key ring MUST be persisted: without it, keys regenerate on every restart, which
        // logs out all users and makes previously-encrypted secrets permanently undecryptable.
        // Set DataProtection:KeyRingPath to a stable, shared volume in production (a single
        // path is also what lets multiple instances share one key ring). A fixed application
        // name keeps the purpose isolation stable across deploys.
        var dataProtection = services.AddDataProtection()
            .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "Linkbelli");

        var keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddScoped<IFullTextSearch, PostgresFullTextSearch>();
        services.AddScoped<ISeededShuffle, PostgresSeededShuffle>();

        // --- Mail ---
        // Bound from configuration and nothing else, so which provider sends the mail is an
        // operational decision: Brevo today, something else tomorrow, same code.
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddIdentityApiEndpoints<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>() // enables RoleManager + role claims in the bearer principal
            .AddEntityFrameworkStores<LinkbelliDbContext>();

        // Usernames are unique in Identity; require unique emails too so login-by-email is unambiguous.
        services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true);

        // Note on where usernames are validated. The rule lives in UsernamePolicy and is applied
        // at registration, deliberately *not* through IdentityOptions.AllowedUserNameCharacters.
        // Identity validates the whole user on every update, so setting it there would mean an
        // account created before the rule — anyone who typed their email address into the sign-up
        // box, which is what the rule exists to stop — could no longer reset their password, and
        // would fail on a field they never touched. LegacyUsernameTests pins that behaviour.

        // Identity's default for these is a day, which is a long life for a link that grants an
        // account. Set here so it matches what the reset mail tells people.
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(PasswordResetService.ValidForHours));

        // --- Hangfire (durable background jobs, Postgres storage) ---
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseFilter(new AutomaticRetryAttribute { Attempts = 3 })
            .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer();
        services.AddSingleton<ILinkEnrichmentQueue, HangfireLinkEnrichmentQueue>();
        services.AddSingleton<ISourceScheduler, HangfireSourceScheduler>();
        services.AddSingleton<INotificationQueue, HangfireNotificationQueue>();
        services.AddSingleton<IWebhookQueue, HangfireWebhookQueue>();
        services.AddScoped<WebhookDeliveryJob>();
        services.AddSingleton<IBackgroundJobStats, HangfireJobStats>();
        services.AddHostedService<SourceScheduleSyncService>();
        services.AddHostedService<AdminRoleSeeder>();
        services.AddHostedService<MaintenanceScheduler>();
        services.AddHostedService<SourceTemplateSeeder>();

        return services;
    }

    /// <summary>
    /// Maps the Hangfire dashboard at /hangfire. Open in Development; in other environments it
    /// requires HTTP Basic credentials from config ("Hangfire:Dashboard:Username"/"Password") and
    /// is closed entirely if none are set.
    /// </summary>
    public static void UseLinkbelliDashboard(this WebApplication app)
    {
        var authFilter = new HangfireDashboardAuthFilter(
            allowAll: app.Environment.IsDevelopment(),
            username: app.Configuration["Hangfire:Dashboard:Username"],
            password: app.Configuration["Hangfire:Dashboard:Password"]);

        app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [authFilter] });
    }

    /// <summary>
    /// A lock id for the migration advisory lock. Arbitrary, but it has to be the same number in
    /// every instance, so it is written down here rather than derived from anything.
    /// </summary>
    private const long MigrationLockId = 6_675_481_073_241_001;

    /// <summary>Applies pending EF migrations (opt-in via config "Database:MigrateAtStartup").</summary>
    /// <remarks>
    /// Behind a Postgres advisory lock, because every instance runs this on boot. Two of them
    /// starting together — which is exactly what a rolling restart or a scale-up does — both saw
    /// the same pending list and both tried to apply it, and the loser failed on an object that
    /// already existed. Whoever gets the lock migrates; the others wait and then find nothing to
    /// do.
    ///
    /// The connection is opened explicitly so the lock and the migration share one session: a
    /// session-level advisory lock taken on a connection that is then returned to the pool
    /// protects nothing.
    /// </remarks>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Linkbelli.Migrations");

        await db.Database.OpenConnectionAsync();
        try
        {
            // Tried first so the wait can be explained. Blocking on pg_advisory_lock outright
            // works, but it looks from the outside like a container that has hung with no logs —
            // which is exactly what somebody watching a rolling restart does not need.
            // Aliased "Value": SqlQuery<T> wraps this in a subquery and reads a column by that
            // name, so without the alias it fails with 42703 — on every startup, not just a
            // contended one.
            var acquired = await db.Database
                .SqlQuery<bool>($"SELECT pg_try_advisory_lock({MigrationLockId}) AS \"Value\"")
                .SingleAsync();

            if (!acquired)
            {
                logger.LogInformation(
                    "Another instance is applying migrations. Waiting for it to finish.");
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({MigrationLockId})");
            }

            await db.Database.MigrateAsync();
        }
        finally
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({MigrationLockId})");
            await db.Database.CloseConnectionAsync();
        }
    }
}
