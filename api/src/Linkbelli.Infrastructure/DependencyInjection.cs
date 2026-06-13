using Linkbelli.Application.Data;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Linkbelli.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers all persistence + Identity wiring. The single place the composition
    /// root needs to know about Infrastructure concretions.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<LinkbelliDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default")));

        // Expose the same scoped DbContext instance as the Application's abstraction.
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<LinkbelliDbContext>());

        services.AddHealthChecks().AddDbContextCheck<LinkbelliDbContext>("database");

        services.AddIdentityApiEndpoints<ApplicationUser>()
            .AddEntityFrameworkStores<LinkbelliDbContext>();

        // Usernames are unique in Identity; require unique emails too so login-by-email is unambiguous.
        services.Configure<IdentityOptions>(options => options.User.RequireUniqueEmail = true);

        return services;
    }
}
