using Ashopper.Api.Auth;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Scraping;
using Ashopper.Infrastructure.Scraping.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ashopper.Api.Endpoints;

public sealed record StoreDto(
    string Key, string Name, string MarketCode, string Domain, bool Enabled, string? KnownLimitation,
    DateTimeOffset? BlockedUntil, string? BlockReason);
public sealed record TrackedPricePointDto(DateOnly Day, decimal Price, bool InStock);
public sealed record TrackedUrlDto(
    Guid Id, string Url, string StoreKey, string StoreName, Guid CategoryId, Guid? ProductId, string? Title,
    ScrapeStatus Status, string? LastError, DateTimeOffset? LastCheckedAt, DateTimeOffset NextCheckAt,
    decimal? Price, string? Currency, bool? InStock, IReadOnlyList<TrackedPricePointDto> History, DateTimeOffset AddedAt);
/// <param name="ProductId">Optional "same item as": attach this page's prices to an existing product.</param>
public sealed record TrackUrlRequest(string Url, Guid CategoryId, Guid? ProductId);

/// <summary>Links to product pages that the polite scraper checks. Prices land in the shared catalog tables.</summary>
public static class TrackingEndpoints
{
    private const int MaxTrackedPerUser = 200;
    private const int HistoryDays = 180;
    private static readonly TimeSpan CheckNowCooldown = TimeSpan.FromMinutes(10);

    public static void MapTrackingEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Tracking").RequireAuthorization();
        api.MapGet("/stores", GetStoresAsync).Produces<List<StoreDto>>();
        api.MapGet("/tracked", GetTrackedAsync).Produces<List<TrackedUrlDto>>();
        api.MapPost("/tracked", TrackAsync).Produces<TrackedUrlDto>(StatusCodes.Status201Created);
        api.MapPost("/tracked/{id:guid}/check", CheckNowAsync).Produces<TrackedUrlDto>();
        api.MapDelete("/tracked/{id:guid}", UntrackAsync).Produces(StatusCodes.Status204NoContent);
    }

    private static async Task<IResult> GetStoresAsync(AppDbContext db, StoreRegistry stores, IOptions<ScraperOptions> options, CancellationToken ct)
    {
        var hosts = await db.ScrapeHosts.AsNoTracking().ToListAsync(ct);
        return Results.Ok(stores.All.Select(s =>
        {
            // The most recent block among the store's hosts (www.x-kom.pl, x-kom.pl, ...).
            var block = hosts.Where(h => s.Handles(new Uri($"https://{h.Host}/"))).MaxBy(h => h.BlockedUntil);
            return new StoreDto(s.Key, s.Name, s.MarketCode, s.Domain, options.Value.IsEnabled(s.Key), s.KnownLimitation,
                block?.BlockedUntil, block?.BlockReason);
        }).ToList());
    }

    private static async Task<IResult> GetTrackedAsync(HttpContext http, AppDbContext db, StoreRegistry stores, TimeProvider time, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        http.Response.Headers.CacheControl = "private, no-cache";
        return Results.Ok(await LoadDtosAsync(db, stores, time, userId, null, ct));
    }

    private static async Task<IResult> TrackAsync(
        TrackUrlRequest req, HttpContext http, AppDbContext db, StoreRegistry stores, IOptions<ScraperOptions> options,
        TimeProvider time, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        if (string.IsNullOrWhiteSpace(req.Url) || req.Url.Length > 2048
            || !Uri.TryCreate(req.Url.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
            return Validation.Problem("url", "Paste a full product link starting with https://.");

        var store = stores.Find(url);
        if (store is null)
            return Validation.Problem("url", $"This store isn't supported yet. Supported: {string.Join(", ", stores.All.Select(s => s.Domain))}.");
        if (!options.Value.IsEnabled(store.Key))
            return Validation.Problem("url", $"{store.Name} is switched off in the scraper settings.");

        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == req.CategoryId, ct);
        if (category is null)
            return Validation.Problem("categoryId", "Unknown category.");
        if (category.Attributes.Count == 0)
            return Validation.Problem("categoryId", "Pick a specific category, not a group.");
        if (req.ProductId is { } pid && !await db.Products.AnyAsync(p => p.Id == pid && p.CategoryId == category.Id, ct))
            return Validation.Problem("productId", "Product not found in this category.");
        if (await db.TrackedUrlSubscriptions.CountAsync(s => s.UserId == userId, ct) >= MaxTrackedPerUser)
            return Validation.Problem("url", $"At most {MaxTrackedPerUser} tracked links.");

        var canonical = store.Canonicalize(url).AbsoluteUri;
        var hash = TrackedUrl.Hash(canonical);
        var now = time.GetUtcNow();
        var tracked = await db.TrackedUrls.SingleOrDefaultAsync(t => t.UrlHash == hash, ct);
        if (tracked is null)
        {
            tracked = new TrackedUrl
            {
                Url = canonical, UrlHash = hash, StoreKey = store.Key, CategoryId = category.Id, ProductId = req.ProductId,
                CreatedAt = now, NextCheckAt = now,
            };
            db.TrackedUrls.Add(tracked);
        }
        // Someone else already tracks this page: share it. Their category and product choice stay.

        if (!await db.TrackedUrlSubscriptions.AnyAsync(s => s.UserId == userId && s.TrackedUrlId == tracked.Id, ct))
            db.TrackedUrlSubscriptions.Add(new TrackedUrlSubscription { UserId = userId, TrackedUrlId = tracked.Id, AddedAt = now });
        await db.SaveChangesAsync(ct);

        var dto = (await LoadDtosAsync(db, stores, time, userId, tracked.Id, ct)).Single();
        return Results.Created($"/api/tracked/{tracked.Id}", dto);
    }

    /// <summary>Moves the page to the front of the queue. The scraper still applies all its politeness rules.</summary>
    private static async Task<IResult> CheckNowAsync(Guid id, HttpContext http, AppDbContext db, StoreRegistry stores, TimeProvider time, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var tracked = await db.TrackedUrls
            .Where(t => t.Id == id && db.TrackedUrlSubscriptions.Any(s => s.TrackedUrlId == t.Id && s.UserId == userId))
            .SingleOrDefaultAsync(ct);
        if (tracked is null)
            return Results.NotFound();

        var now = time.GetUtcNow();
        if (tracked.LastCheckedAt is { } last && now - last < CheckNowCooldown)
            return Validation.Problem("id", $"Checked less than {CheckNowCooldown.TotalMinutes:0} minutes ago. Try again later.");
        tracked.NextCheckAt = now;
        await db.SaveChangesAsync(ct);
        return Results.Ok((await LoadDtosAsync(db, stores, time, userId, id, ct)).Single());
    }

    private static async Task<IResult> UntrackAsync(Guid id, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        // The page itself and its price history stay: other users or lists may rely on them. Without subscribers it
        // simply stops being fetched.
        var deleted = await db.TrackedUrlSubscriptions.Where(s => s.UserId == userId && s.TrackedUrlId == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<List<TrackedUrlDto>> LoadDtosAsync(
        AppDbContext db, StoreRegistry stores, TimeProvider time, Guid userId, Guid? onlyId, CancellationToken ct)
    {
        var rows = await (
                from s in db.TrackedUrlSubscriptions
                join t in db.TrackedUrls on s.TrackedUrlId equals t.Id
                where s.UserId == userId && (onlyId == null || t.Id == onlyId)
                orderby s.AddedAt descending
                select new { Tracked = t, s.AddedAt })
            .AsNoTracking()
            .ToListAsync(ct);

        var offerIds = rows.Where(r => r.Tracked.OfferId != null).Select(r => r.Tracked.OfferId!.Value).ToList();
        var since = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-HistoryDays);
        var history = (await db.DailyPrices.AsNoTracking()
                .Where(d => offerIds.Contains(d.OfferId) && d.Day >= since)
                .OrderBy(d => d.Day)
                .ToListAsync(ct))
            .GroupBy(d => d.OfferId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var offerProducts = await db.Offers.AsNoTracking().Where(o => offerIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.ProductId, ct);

        return rows.Select(r =>
        {
            var t = r.Tracked;
            var points = t.OfferId is { } oid && history.TryGetValue(oid, out var h) ? h : [];
            var latest = points.LastOrDefault();
            return new TrackedUrlDto(
                t.Id, t.Url, t.StoreKey, stores.Get(t.StoreKey)?.Name ?? t.StoreKey, t.CategoryId,
                t.ProductId ?? (t.OfferId is { } o && offerProducts.TryGetValue(o, out var p) ? p : null),
                t.Title, t.LastStatus, t.LastError, t.LastCheckedAt, t.NextCheckAt,
                latest?.MinPrice, latest?.Currency, latest?.InStock,
                points.Select(d => new TrackedPricePointDto(d.Day, d.MinPrice, d.InStock)).ToList(),
                r.AddedAt);
        }).ToList();
    }
}
