using Ashopper.Infrastructure.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashopper.Infrastructure.Persistence;

public static class DatabaseMigrator
{
    /// <summary>
    /// Applies EF migrations and upserts reference data. In production this runs once per deploy
    /// (collector "migrate" command), never from scaled-out API replicas.
    /// </summary>
    public static async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync(ct);
        await ReferenceData.SeedAsync(db, ct);
    }
}
