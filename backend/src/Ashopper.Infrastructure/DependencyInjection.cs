using System.Net;
using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Scraping;
using Ashopper.Infrastructure.Scraping.Stores;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

        // Store adapters are pure (URL rules + parsing); the API uses them to validate and canonicalize tracked URLs.
        services.AddOptions<ScraperOptions>().BindConfiguration(ScraperOptions.Section);
        services.AddSingleton(new StoreRegistry(StoreRegistry.Default));
        // Also used by the API to save a reading the user sent from their own browser.
        services.AddScoped<ScrapeRecorder>();
        return services;
    }

    /// <summary>The polite scraper: fetches tracked product pages. Only the collector needs it.</summary>
    public static IServiceCollection AddAshopperScraper(this IServiceCollection services)
    {
        services.AddHttpClient<PoliteFetcher>(PoliteFetcher.HttpClientName, (sp, http) =>
            {
                http.Timeout = TimeSpan.FromSeconds(sp.GetRequiredService<IOptions<ScraperOptions>>().Value.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 3,
                UseCookies = false, // stateless: no sessions, nothing that looks like a returning browser
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });
        // No resilience handler on purpose: retries would multiply requests to shops. Failed pages wait for a later run.
        services.TryAddSingleton<IHostStateStore, DbHostStateStore>();
        services.AddTransient<ScrapeService>();
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
