using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Scraping.Parsing;
using Ashopper.Infrastructure.Scraping.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashopper.Infrastructure.Scraping;

public sealed record ScrapeRunResult(int Checked, int Succeeded, int Observations);

/// <summary>
/// One scraper pass: checks every tracked page that is due, host by host. Hosts run in parallel; pages on one host run
/// one after another, spaced by <see cref="PoliteFetcher"/>. Ends like a collector run so cached price responses refresh.
/// </summary>
public sealed class ScrapeService(
    IServiceScopeFactory scopes,
    StoreRegistry stores,
    PoliteFetcher fetcher,
    IOptions<ScraperOptions> options,
    TimeProvider time,
    ILogger<ScrapeService> logger)
{
    public async Task<ScrapeRunResult> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        List<(Guid Id, string Url, string StoreKey)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Only pages someone still tracks.
            due = (await db.TrackedUrls.AsNoTracking()
                    .Where(t => t.NextCheckAt <= now && db.TrackedUrlSubscriptions.Any(s => s.TrackedUrlId == t.Id))
                    .OrderBy(t => t.NextCheckAt)
                    .Select(t => new { t.Id, t.Url, t.StoreKey })
                    .ToListAsync(ct))
                .Select(t => (t.Id, t.Url, t.StoreKey))
                .ToList();
        }
        if (due.Count == 0)
            return new ScrapeRunResult(0, 0, 0);

        var hosts = due
            .Where(t => stores.Get(t.StoreKey) is { } s && options.Value.IsEnabled(s.Key))
            .GroupBy(t => new Uri(t.Url).Host)
            .ToList();
        logger.LogInformation("Scraping {Pages} due page(s) on {Hosts} host(s)", hosts.Sum(h => h.Count()), hosts.Count);

        int checkedCount = 0, succeeded = 0, observations = 0;
        await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = options.Value.MaxParallelHosts, CancellationToken = ct },
            async (host, token) =>
            {
                foreach (var page in host.Take(options.Value.MaxPerHostPerRun))
                {
                    var store = stores.Get(page.StoreKey)!;
                    var (fetch, product) = await CheckAsync(store, new Uri(page.Url), token);

                    await using var scope = scopes.CreateAsyncScope();
                    var recorder = scope.ServiceProvider.GetRequiredService<ScrapeRecorder>();
                    var written = await recorder.RecordAsync(page.Id, store, fetch, product, token);

                    Interlocked.Increment(ref checkedCount);
                    Interlocked.Add(ref observations, written);
                    if (written > 0)
                        Interlocked.Increment(ref succeeded);
                    logger.LogInformation("{Store} {Url}: {Kind} {Price} {Currency} {Detail}",
                        store.Key, page.Url, fetch.Kind, product?.Price, product?.Currency, fetch.Detail);

                    // The whole host is backing off now; the rest of its pages wait for a later run.
                    if (fetch.Kind is FetchKind.Blocked or FetchKind.HostBackingOff)
                        break;
                }
            });

        await FinishRunAsync(now, observations, checkedCount, succeeded, ct);
        logger.LogInformation("Scrape run: {Checked} checked, {Ok} with a price, {Obs} observations", checkedCount, succeeded, observations);
        return new ScrapeRunResult(checkedCount, succeeded, observations);
    }

    private async Task<(FetchResult, ScrapedProduct?)> CheckAsync(StoreAdapter store, Uri url, CancellationToken ct)
    {
        var fetch = await fetcher.FetchAsync(url, ct);
        if (fetch.Kind != FetchKind.Ok)
            return (fetch, null);
        try
        {
            return (fetch, store.Parse(fetch.Html!, url));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Parsing {Url} failed", url);
            return (fetch with { Kind = FetchKind.Error, Detail = $"Parsing failed: {ex.Message}" }, null);
        }
    }

    /// <summary>Same bookkeeping as a collector run: new products may match requirements, and the run versions ETags.</summary>
    private async Task FinishRunAsync(DateTimeOffset startedAt, int observations, int checkedCount, int succeeded, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (observations > 0)
            await scope.ServiceProvider.GetRequiredService<MatchRebuilder>().RebuildAllAsync(ct);

        db.CollectorRuns.Add(new CollectorRun
        {
            StartedAt = startedAt,
            FinishedAt = time.GetUtcNow(),
            Sources = ScrapeRecorder.SourceKey,
            WorkItems = checkedCount,
            FailedWorkItems = checkedCount - succeeded,
            Observations = observations,
            Succeeded = true,
        });
        await db.SaveChangesAsync(ct);
    }
}
