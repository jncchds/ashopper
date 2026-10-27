using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Queries;
using Ashopper.Infrastructure.Sources;
using Ashopper.Infrastructure.Sources.Fake;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ashopper.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Database, queries and matching. Used by both the API and the collector.</summary>
    public static IServiceCollection AddAshopperData(this IServiceCollection services, IConfiguration config)
    {
        // Resolved lazily, so tooling that only builds the host (OpenAPI generation) needs no database.
        services.AddDbContext<AppDbContext>(o => o.UseSqlServer(
            config.GetConnectionString("Sql") is { Length: > 0 } connectionString
                ? connectionString
                : throw new InvalidOperationException("Connection string 'Sql' is not configured (ConnectionStrings__Sql)."),
            sql =>
        {
            // Azure SQL free tier auto-pauses; the first connection after a pause fails until it resumes (~1 min).
            sql.EnableRetryOnFailure(maxRetryCount: 8, maxRetryDelay: TimeSpan.FromSeconds(15), errorNumbersToAdd: null);
            sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
        }));

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<MatchRebuilder>();
        services.AddScoped<ListPricingQuery>();
        return services;
    }

    /// <summary>Price sources and the collection pipeline. Only the collector needs these.</summary>
    public static IServiceCollection AddAshopperCollector(this IServiceCollection services, IConfiguration config)
    {
        if (config.GetValue("Sources:Fake:Enabled", true))
            services.AddSingleton<IPriceSource, FakePriceSource>();
        // Real sources (Ceneo, Allegro, ...) register here, each behind its own "Sources:<Name>:Enabled" flag,
        // with AddHttpClient(...).AddStandardResilienceHandler() for retries and rate limiting.

        services.AddScoped<CatalogIngestor>();
        services.AddScoped<CollectorService>();
        services.AddSingleton<IWorkDispatcher, InProcessWorkDispatcher>();
        return services;
    }
}
