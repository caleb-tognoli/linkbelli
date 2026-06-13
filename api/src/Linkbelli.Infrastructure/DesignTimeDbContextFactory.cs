using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Linkbelli.Infrastructure;

/// <summary>Used by `dotnet ef` at design time only; never at runtime.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LinkbelliDbContext>
{
    public LinkbelliDbContext CreateDbContext(string[] args)
    {
        // Design-time only (used by `dotnet ef`). Prefer an env var; fall back to local dev.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5433;Database=linkbelli;Username=linkbelli;Password=linkbelli";

        var options = new DbContextOptionsBuilder<LinkbelliDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new LinkbelliDbContext(options);
    }
}
