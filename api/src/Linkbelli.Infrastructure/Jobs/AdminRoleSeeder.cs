using Linkbelli.Application.Auth;
using Linkbelli.Application.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Ensures the Admin role exists and grants it to the usernames listed under config
/// "Admin:Usernames" (idempotent; skips users that haven't registered yet).
/// </summary>
public sealed class AdminRoleSeeder(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AdminRoleSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            if (!await roles.RoleExistsAsync(AppRoles.Admin))
            {
                await roles.CreateAsync(new IdentityRole<Guid>(AppRoles.Admin));
            }

            var usernames = configuration.GetSection("Admin:Usernames").Get<string[]>() ?? [];
            if (usernames.Length == 0)
            {
                return;
            }

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            foreach (var username in usernames)
            {
                var user = await users.FindByNameAsync(username);
                if (user is null)
                {
                    logger.LogInformation("Admin user '{Username}' not found yet; will retry on next start.", username);
                    continue;
                }

                if (!await users.IsInRoleAsync(user, AppRoles.Admin))
                {
                    await users.AddToRoleAsync(user, AppRoles.Admin);
                    logger.LogInformation("Granted Admin role to '{Username}'.", username);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Admin role seeding failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
