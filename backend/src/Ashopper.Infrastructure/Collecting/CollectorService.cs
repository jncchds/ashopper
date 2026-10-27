using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ashopper.Infrastructure.Collecting;

/// <summary>
/// One full collection run: fans out source × market × category work items, then refreshes
/// requirement matches and records the run (which invalidates cached price responses).
/// </summary>
public sealed class CollectorService(
    AppDbContext db,
    IEnumerable<IPriceSource> sources,
    IWorkDispatcher dispatcher,
    MatchRebuilder matches,
    TimeProvider time,
    ILogger<CollectorService> logger)
{
    public async Task<CollectorRun> RunAsync(CollectorOptions options, CancellationToken ct)
    {
        var selected = sources
            .Where(s => options.Sources.Count == 0 || options.Sources.Contains(s.Key, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException($"No price sources match '{string.Join(",", options.Sources)}'. Registered: {string.Join(",", sources.Select(s => s.Key))}.");

        var run = new CollectorRun { StartedAt = time.GetUtcNow(), Sources = string.Join(",", selected.Select(s => s.Key)) };
        db.CollectorRuns.Add(run);
        await db.SaveChangesAsync(ct);

        var markets = await db.Markets.Select(m => m.Code).ToListAsync(ct);
        var categories = await db.Categories.Select(c => c.Slug).ToListAsync(ct);

        var work = selected
            .SelectMany(s => s.Markets.Intersect(markets).SelectMany(m => s.CategorySlugs.Intersect(categories)
                .Select(c => new CollectorWorkItem(s.Key, m, c, options.BackfillDays))))
            .ToList();

        foreach (var item in work)
        {
            try
            {
                var result = await dispatcher.DispatchAsync(item, ct);
                run.Observations += result.Observations;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken source/category must not stop the rest of the run.
                run.FailedWorkItems++;
                logger.LogError(ex, "Work item failed: {Item}", item);
            }
            run.WorkItems++;
        }

        var matchCount = await matches.RebuildAllAsync(ct);
        logger.LogInformation("Rebuilt requirement matches: {Count}", matchCount);

        // The rebuilder clears the change tracker, so re-attach the run before updating it.
        db.CollectorRuns.Attach(run);
        run.FinishedAt = time.GetUtcNow();
        run.Succeeded = run.FailedWorkItems < run.WorkItems || run.WorkItems == 0;
        db.Entry(run).State = EntityState.Modified;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Collector run {Id}: {Items} work items, {Failed} failed, {Obs} observations",
            run.Id, run.WorkItems, run.FailedWorkItems, run.Observations);
        return run;
    }
}
