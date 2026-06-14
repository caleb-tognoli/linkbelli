using Hangfire;
using Hangfire.PostgreSql;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Security;
using Linkbelli.Application.Sources;
using Linkbelli.Infrastructure.Jobs;
using Linkbelli.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

        services.AddDbContext<LinkbelliDbContext>(options => options.UseNpgsql(connectionString));

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

        services.AddIdentityApiEndpoints<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>() // enables RoleManager + role claims in the bearer principal
            .AddEntityFrameworkStores<LinkbelliDbContext>();

        // Usernames are unique in Identity; require unique emails too so login-by-email is unambiguous.
        services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true);

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
        services.AddHostedService<SourceScheduleSyncService>();
        services.AddHostedService<AdminRoleSeeder>();

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

    /// <summary>Applies pending EF migrations (opt-in via config "Database:MigrateAtStartup").</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LinkbelliDbContext>();
        await db.Database.MigrateAsync();
    }
}
