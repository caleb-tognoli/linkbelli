using Hangfire;
using Hangfire.PostgreSql;
using Linkbelli.Application.Data;
using Linkbelli.Application.Enrichment;
using Linkbelli.Application.Identity;
using Linkbelli.Application.Sources;
using Linkbelli.Infrastructure.Jobs;
using Microsoft.AspNetCore.Builder;
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

        services.AddIdentityApiEndpoints<ApplicationUser>()
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

        return services;
    }

    /// <summary>Maps the Hangfire dashboard at /hangfire (development only for now).</summary>
    public static void UseLinkbelliDashboard(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseHangfireDashboard("/hangfire");
        }
    }
}
