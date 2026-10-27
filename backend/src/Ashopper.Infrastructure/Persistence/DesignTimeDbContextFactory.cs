using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ashopper.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` (migrations). Adding a migration never connects; `database update`
/// uses ASHOPPER_SQL or the local docker-compose database.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ASHOPPER_SQL")
            ?? "Server=localhost,1433;Database=ashopper;User Id=sa;Password=Ashopper_dev_123!;TrustServerCertificate=True";
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
    }
}
