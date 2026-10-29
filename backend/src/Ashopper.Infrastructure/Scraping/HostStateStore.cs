using Ashopper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ashopper.Infrastructure.Scraping;

/// <summary>Per-host politeness state. Shared through the database so parallel scraper processes respect one budget.</summary>
public interface IHostStateStore
{
    Task<ScrapeHost> GetAsync(string host, CancellationToken ct);

    /// <summary>Saves robots.txt and block fields. Never moves <see cref="ScrapeHost.NextRequestAt"/> back.</summary>
    Task SaveAsync(ScrapeHost state, CancellationToken ct);

    /// <summary>
    /// Claims the next request slot for the host if it is free (NextRequestAt ≤ now) and pushes the slot
    /// <paramref name="spacing"/> into the future. Returns null on success, or when the slot frees up.
    /// </summary>
    Task<DateTimeOffset?> TryReserveAsync(string host, TimeSpan spacing, CancellationToken ct);
}

public sealed class DbHostStateStore(IServiceScopeFactory scopes, TimeProvider time) : IHostStateStore
{
    public async Task<ScrapeHost> GetAsync(string host, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ScrapeHosts.AsNoTracking().SingleOrDefaultAsync(h => h.Host == host, ct)
            ?? new ScrapeHost { Host = host };
    }

    public async Task SaveAsync(ScrapeHost state, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await EnsureRowAsync(db, state.Host, ct);
        await db.ScrapeHosts.Where(h => h.Host == state.Host).ExecuteUpdateAsync(s => s
            .SetProperty(h => h.BlockedUntil, state.BlockedUntil)
            .SetProperty(h => h.BlockReason, state.BlockReason)
            .SetProperty(h => h.RobotsTxt, state.RobotsTxt)
            .SetProperty(h => h.RobotsFetchedAt, state.RobotsFetchedAt), ct);
    }

    public async Task<DateTimeOffset?> TryReserveAsync(string host, TimeSpan spacing, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await EnsureRowAsync(db, host, ct);

        var now = time.GetUtcNow();
        var next = now + spacing;
        // Atomic compare-and-set: only one process can move the slot from "free" to "taken".
        var claimed = await db.ScrapeHosts
            .Where(h => h.Host == host && h.NextRequestAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(h => h.NextRequestAt, next), ct);
        if (claimed == 1)
            return null;
        return await db.ScrapeHosts.Where(h => h.Host == host).Select(h => h.NextRequestAt).SingleAsync(ct);
    }

    private static async Task EnsureRowAsync(AppDbContext db, string host, CancellationToken ct)
    {
        if (await db.ScrapeHosts.AnyAsync(h => h.Host == host, ct))
            return;
        db.ScrapeHosts.Add(new ScrapeHost { Host = host, NextRequestAt = DateTimeOffset.MinValue });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another process inserted it first; that's fine.
        }
        db.ChangeTracker.Clear();
    }
}

/// <summary>For tests and one-off runs without a database.</summary>
public sealed class InMemoryHostStateStore(TimeProvider time) : IHostStateStore
{
    private readonly Dictionary<string, ScrapeHost> _hosts = [];
    private readonly Lock _lock = new();

    public Task<ScrapeHost> GetAsync(string host, CancellationToken ct)
    {
        lock (_lock)
            return Task.FromResult(_hosts.TryGetValue(host, out var h) ? Copy(h) : new ScrapeHost { Host = host });
    }

    public Task SaveAsync(ScrapeHost state, CancellationToken ct)
    {
        lock (_lock)
        {
            var h = Row(state.Host);
            h.BlockedUntil = state.BlockedUntil;
            h.BlockReason = state.BlockReason;
            h.RobotsTxt = state.RobotsTxt;
            h.RobotsFetchedAt = state.RobotsFetchedAt;
        }
        return Task.CompletedTask;
    }

    public Task<DateTimeOffset?> TryReserveAsync(string host, TimeSpan spacing, CancellationToken ct)
    {
        lock (_lock)
        {
            var h = Row(host);
            var now = time.GetUtcNow();
            if (h.NextRequestAt > now)
                return Task.FromResult<DateTimeOffset?>(h.NextRequestAt);
            h.NextRequestAt = now + spacing;
            return Task.FromResult<DateTimeOffset?>(null);
        }
    }

    private ScrapeHost Row(string host)
    {
        if (!_hosts.TryGetValue(host, out var h))
            _hosts[host] = h = new ScrapeHost { Host = host, NextRequestAt = DateTimeOffset.MinValue };
        return h;
    }

    private static ScrapeHost Copy(ScrapeHost h) => new()
    {
        Host = h.Host, NextRequestAt = h.NextRequestAt, BlockedUntil = h.BlockedUntil, BlockReason = h.BlockReason,
        RobotsTxt = h.RobotsTxt, RobotsFetchedAt = h.RobotsFetchedAt,
    };
}
