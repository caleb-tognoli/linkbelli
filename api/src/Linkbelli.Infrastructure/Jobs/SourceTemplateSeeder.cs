using Linkbelli.Application.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Linkbelli.Infrastructure.Jobs;

/// <summary>
/// Puts the built-in source templates in the database at startup, updating any that are already
/// there. Updating rather than skipping matters: a corrected feed path should reach everyone,
/// not just people who create a source after the fix.
/// </summary>
public sealed class SourceTemplateSeeder(
    IServiceScopeFactory scopeFactory,
    ILogger<SourceTemplateSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var templates = scope.ServiceProvider.GetRequiredService<ISourceTemplateService>();
            await templates.SeedBuiltinsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Templates are a convenience; the app is perfectly usable without them.
            logger.LogError(ex, "Failed to seed the built-in source templates.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
