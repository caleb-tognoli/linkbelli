using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Linkbelli.Infrastructure;

/// <summary>Used by `dotnet ef` at design time only; never at runtime.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LinkbelliDbContext>
{
    public LinkbelliDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LinkbelliDbContext>()
            .UseNpgsql("Host=localhost;Port=5433;Database=linkbelli;Username=linkbelli;Password=linkbelli")
            .Options;
        return new LinkbelliDbContext(options);
    }
}
