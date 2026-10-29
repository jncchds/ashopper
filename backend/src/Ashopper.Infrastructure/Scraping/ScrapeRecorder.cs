using Ashopper.Domain.Catalog;
using Ashopper.Domain.Pricing;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Scraping.Parsing;
using Ashopper.Infrastructure.Scraping.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ashopper.Infrastructure.Scraping;

/// <summary>
/// Writes the outcome of one page check. A good reading lands in the same tables aggregator sources use (shop, product,
/// offer, observation, daily minimum), so lists and charts treat scraped prices like any other.
/// </summary>
public sealed class ScrapeRecorder(AppDbContext db, IOptions<ScraperOptions> options, TimeProvider time)
{
    public const string SourceKey = "scraper";

    /// <summary>The scraper fetched the page itself.</summary>
    public const string ViaScraper = "scraper";

    /// <summary>The user opened the page in their own browser and sent what it showed (stores that block bots).</summary>
    public const string ViaBrowser = "browser";

    /// <returns>Number of price observations written (0 or 1).</returns>
    public async Task<int> RecordAsync(
        Guid trackedUrlId, StoreAdapter store, FetchResult fetch, ScrapedProduct? page, CancellationToken ct, string via = ViaScraper)
    {
        var tracked = await db.TrackedUrls.SingleAsync(t => t.Id == trackedUrlId, ct);
        var now = time.GetUtcNow();
        tracked.LastCheckedAt = now;

        var (status, error) = fetch.Kind switch
        {
            FetchKind.Ok when page?.Price is not null => (ScrapeStatus.Ok, null),
            FetchKind.Ok when page is null => (ScrapeStatus.NoPrice, "No product data found on the page."),
            FetchKind.Ok => (ScrapeStatus.NoPrice, "The page has product data but no price."),
            FetchKind.NotFound => (ScrapeStatus.NotFound, fetch.Detail),
            FetchKind.Blocked or FetchKind.HostBackingOff => (ScrapeStatus.Blocked, fetch.Detail),
            FetchKind.RobotsDisallowed => (ScrapeStatus.RobotsDisallowed, fetch.Detail),
            _ => (ScrapeStatus.Error, fetch.Detail),
        };
        tracked.LastStatus = status;
        tracked.LastError = error is { Length: > 500 } ? error[..500] : error;
        if (page?.Name is { } name)
            tracked.Title = Truncate(name, 400);

        var observations = 0;
        if (status == ScrapeStatus.Ok)
        {
            observations = await RecordPriceAsync(tracked, store, page!, now, ct);
            tracked.LastSuccessAt = now;
            tracked.LastSuccessVia = via;
            tracked.ConsecutiveFailures = 0;
            tracked.NextCheckAt = now + Jittered(TimeSpan.FromHours(options.Value.RecheckHours));
        }
        else if (fetch.Kind == FetchKind.HostBackingOff)
        {
            // Nothing was sent, so this isn't the page's fault: don't grow its backoff, just look again later.
            tracked.NextCheckAt = now + TimeSpan.FromHours(1);
        }
        else
        {
            // A product with no price is often just out of stock: record that, so the chart shows it, then back off.
            if (status == ScrapeStatus.NoPrice && page is not null && tracked.OfferId is { } offerId)
                await MarkOutOfStockAsync(offerId, now, ct);
            tracked.ConsecutiveFailures++;
            tracked.NextCheckAt = now + Backoff(tracked.ConsecutiveFailures);
        }

        await db.SaveChangesAsync(ct);
        return observations;
    }

    private async Task<int> RecordPriceAsync(TrackedUrl tracked, StoreAdapter store, ScrapedProduct page, DateTimeOffset now, CancellationToken ct)
    {
        var shop = await db.Shops.SingleOrDefaultAsync(s => s.MarketCode == store.MarketCode && s.Domain == store.Domain, ct);
        if (shop is null)
        {
            shop = new Shop { MarketCode = store.MarketCode, Domain = store.Domain, Name = store.Name };
            db.Shops.Add(shop);
        }

        var offer = tracked.OfferId is { } id ? await db.Offers.SingleOrDefaultAsync(o => o.Id == id, ct) : null;
        var product = await FindOrCreateProductAsync(tracked, offer, page, ct);

        if (offer is null)
        {
            offer = new Offer { SourceKey = SourceKey, ExternalId = tracked.Id.ToString("N"), Url = tracked.Url, ProductId = product.Id, ShopId = shop.Id };
            db.Offers.Add(offer);
            tracked.OfferId = offer.Id;
        }
        offer.ProductId = product.Id;
        offer.IsActive = true;
        offer.LastSeenAt = now;

        var price = decimal.Round(page.Price!.Value, 2);
        var currency = page.Currency ?? await db.Markets.Where(m => m.Code == store.MarketCode).Select(m => m.Currency).SingleAsync(ct);
        var inStock = page.InStock ?? true;
        db.PriceObservations.Add(new PriceObservation { OfferId = offer.Id, ObservedAt = now, Price = price, Currency = currency, InStock = inStock });

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var daily = await db.DailyPrices.SingleOrDefaultAsync(d => d.OfferId == offer.Id && d.Day == today, ct);
        if (daily is null)
            db.DailyPrices.Add(new DailyPrice { OfferId = offer.Id, Day = today, MinPrice = price, Currency = currency, InStock = inStock });
        else
            daily.Apply(price, inStock);
        return 1;
    }

    /// <summary>The product the user picked, else the one this page already feeds, else a match by EAN/MPN, else a new one.</summary>
    private async Task<Product> FindOrCreateProductAsync(TrackedUrl tracked, Offer? offer, ScrapedProduct page, CancellationToken ct)
    {
        var productId = tracked.ProductId ?? offer?.ProductId;
        var product = productId is { } pid ? await db.Products.SingleOrDefaultAsync(p => p.Id == pid, ct) : null;

        if (product is null && page.Ean is { Length: > 0 } ean)
            product = await db.Products.FirstOrDefaultAsync(p => p.CategoryId == tracked.CategoryId && p.Ean == ean, ct);
        if (product is null && page.Mpn is { Length: > 0 } mpn)
            product = await db.Products.FirstOrDefaultAsync(p => p.CategoryId == tracked.CategoryId && p.Mpn == mpn, ct);

        if (product is null)
        {
            var name = page.Name ?? tracked.Url;
            var brand = page.Brand ?? "";
            var model = brand.Length > 0 && name.StartsWith(brand, StringComparison.OrdinalIgnoreCase) ? name[brand.Length..].Trim() : name;
            product = new Product
            {
                CategoryId = tracked.CategoryId,
                Brand = Truncate(brand, 100),
                Model = Truncate(model.Length > 0 ? model : name, 200),
                Name = Truncate(name, 400),
            };
            db.Products.Add(product);
        }

        product.Ean ??= page.Ean is { Length: <= 14 } e ? e : null;
        product.Mpn ??= page.Mpn is { } m ? Truncate(m, 100) : null;
        product.UpdatedAt = time.GetUtcNow();
        return product;
    }

    private async Task MarkOutOfStockAsync(Guid offerId, DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var last = await db.DailyPrices.Where(d => d.OfferId == offerId).OrderByDescending(d => d.Day).FirstOrDefaultAsync(ct);
        if (last is null)
            return;
        db.PriceObservations.Add(new PriceObservation { OfferId = offerId, ObservedAt = now, Price = last.MinPrice, Currency = last.Currency, InStock = false });
        if (last.Day != today)
            db.DailyPrices.Add(new DailyPrice { OfferId = offerId, Day = today, MinPrice = last.MinPrice, Currency = last.Currency, InStock = false });
    }

    /// <summary>Recheck interval doubles with each failure in a row, up to MaxBackoffHours.</summary>
    private TimeSpan Backoff(int failures)
    {
        var hours = Math.Min(options.Value.RecheckHours * Math.Pow(2, Math.Min(failures - 1, 10)), options.Value.MaxBackoffHours);
        return Jittered(TimeSpan.FromHours(hours));
    }

    /// <summary>±10 %, so URLs added together don't stay in lockstep forever.</summary>
    private static TimeSpan Jittered(TimeSpan t) => t * (0.9 + Random.Shared.NextDouble() * 0.2);

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
