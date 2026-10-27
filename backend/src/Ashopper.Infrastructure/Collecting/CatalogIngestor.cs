using Ashopper.Domain.Catalog;
using Ashopper.Domain.Pricing;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Sources;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ashopper.Infrastructure.Collecting;

/// <summary>
/// Runs one work item: pulls products from a source, merges them into the canonical catalog,
/// upserts shops and offers, and records prices (raw observations + daily minimums).
/// </summary>
public sealed class CatalogIngestor(
    AppDbContext db,
    IEnumerable<IPriceSource> sources,
    TimeProvider time,
    ILogger<CatalogIngestor> logger)
{
    public async Task<WorkItemResult> IngestAsync(CollectorWorkItem item, CancellationToken ct)
    {
        var source = sources.Single(s => s.Key == item.SourceKey);
        var category = await db.Categories.SingleAsync(c => c.Slug == item.CategorySlug, ct);
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        // Load what this source already knows about this market + category, once.
        var refs = await db.ProductSourceRefs
            .Where(r => r.SourceKey == source.Key)
            .ToDictionaryAsync(r => r.ExternalId, ct);
        var products = await db.Products.Where(p => p.CategoryId == category.Id).ToListAsync(ct);
        var productsById = products.ToDictionary(p => p.Id);
        var shops = await db.Shops.Where(s => s.MarketCode == item.MarketCode).ToDictionaryAsync(s => s.Domain, ct);
        var shopIds = shops.Values.Select(s => s.Id).ToList();
        var productIds = products.Select(p => p.Id).ToList();
        var offers = await db.Offers
            .Where(o => o.SourceKey == source.Key && productIds.Contains(o.ProductId) && shopIds.Contains(o.ShopId))
            .ToDictionaryAsync(o => o.ExternalId, ct);
        var offerIds = offers.Values.Select(o => o.Id).ToList();
        var todaysPrices = await db.DailyPrices
            .Where(d => d.Day == today && offerIds.Contains(d.OfferId))
            .ToDictionaryAsync(d => d.OfferId, ct);

        var seenOffers = new HashSet<string>();
        var newOffers = new List<Offer>();
        int productCount = 0, observationCount = 0;

        await foreach (var sp in source.DiscoverAsync(item.MarketCode, item.CategorySlug, ct))
        {
            productCount++;
            var product = FindOrCreateProduct(sp, category, refs, products, productsById, source.Key);

            foreach (var so in sp.Offers)
            {
                seenOffers.Add(so.ExternalId);
                var shop = GetOrCreateShop(shops, item.MarketCode, so);

                if (!offers.TryGetValue(so.ExternalId, out var offer))
                {
                    offer = new Offer { SourceKey = source.Key, ExternalId = so.ExternalId, Url = so.Url, ProductId = product.Id, ShopId = shop.Id };
                    db.Offers.Add(offer);
                    offers[so.ExternalId] = offer;
                    newOffers.Add(offer);
                }
                offer.Url = so.Url;
                offer.ProductId = product.Id;
                offer.IsActive = true;
                offer.LastSeenAt = now;

                db.PriceObservations.Add(new PriceObservation
                {
                    OfferId = offer.Id, ObservedAt = now, Price = so.Price, Currency = so.Currency, InStock = so.InStock,
                });
                observationCount++;

                if (todaysPrices.TryGetValue(offer.Id, out var daily))
                {
                    daily.Apply(so.Price, so.InStock);
                }
                else
                {
                    daily = new DailyPrice { OfferId = offer.Id, Day = today, MinPrice = so.Price, Currency = so.Currency, InStock = so.InStock };
                    db.DailyPrices.Add(daily);
                    todaysPrices[offer.Id] = daily;
                }
            }
        }

        // Offers the source no longer reports are kept (history matters) but marked inactive.
        foreach (var gone in offers.Values.Where(o => o.IsActive && !seenOffers.Contains(o.ExternalId)))
            gone.IsActive = false;

        await db.SaveChangesAsync(ct);

        if (item.BackfillDays > 0 && source is IHistoricalPriceSource history && newOffers.Count > 0)
            observationCount += await BackfillAsync(history, newOffers, today, item.BackfillDays, ct);

        logger.LogInformation(
            "{Source}/{Market}/{Category}: {Products} products, {Offers} offers ({New} new), {Observations} observations",
            source.Key, item.MarketCode, item.CategorySlug, productCount, seenOffers.Count, newOffers.Count, observationCount);

        return new WorkItemResult(productCount, seenOffers.Count, observationCount);
    }

    private Product FindOrCreateProduct(
        SourceProduct sp, Category category, Dictionary<string, ProductSourceRef> refs,
        List<Product> products, Dictionary<Guid, Product> productsById, string sourceKey)
    {
        Product? product = null;
        if (refs.TryGetValue(sp.ExternalId, out var existingRef))
            productsById.TryGetValue(existingRef.ProductId, out product);

        // Same product seen through another source: merge by strong ids first, then by name.
        product ??= sp.Ean is { Length: > 0 } ean ? products.FirstOrDefault(p => p.Ean == ean) : null;
        product ??= sp.Mpn is { Length: > 0 } mpn ? products.FirstOrDefault(p => string.Equals(p.Mpn, mpn, StringComparison.OrdinalIgnoreCase)) : null;
        product ??= products.FirstOrDefault(p =>
            Normalize(p.Brand) == Normalize(sp.Brand) && Normalize(p.Model) == Normalize(sp.Model));

        if (product is null)
        {
            product = new Product { CategoryId = category.Id, Brand = sp.Brand, Model = sp.Model, Name = sp.Name };
            db.Products.Add(product);
            products.Add(product);
            productsById[product.Id] = product;
        }

        product.Ean ??= sp.Ean;
        product.Mpn ??= sp.Mpn;
        // Source-provided specs win for the keys they provide; other keys (maybe from other sources) stay.
        foreach (var (key, value) in sp.Attributes)
            product.Attributes[key] = value;
        product.UpdatedAt = time.GetUtcNow();

        if (existingRef is null)
        {
            var newRef = new ProductSourceRef { SourceKey = sourceKey, ExternalId = sp.ExternalId, ProductId = product.Id };
            db.ProductSourceRefs.Add(newRef);
            refs[sp.ExternalId] = newRef;
        }
        else
        {
            existingRef.ProductId = product.Id;
        }

        return product;
    }

    private Shop GetOrCreateShop(Dictionary<string, Shop> shops, string marketCode, SourceOffer so)
    {
        var domain = so.ShopDomain.Trim().ToLowerInvariant();
        if (!shops.TryGetValue(domain, out var shop))
        {
            shop = new Shop { MarketCode = marketCode, Name = so.ShopName, Domain = domain };
            db.Shops.Add(shop);
            shops[domain] = shop;
        }
        return shop;
    }

    private async Task<int> BackfillAsync(
        IHistoricalPriceSource history, List<Offer> newOffers, DateOnly today, int days, CancellationToken ct)
    {
        var count = 0;
        foreach (var offer in newOffers)
        {
            foreach (var point in history.GetHistory(offer.ExternalId, today.AddDays(-days), today.AddDays(-1)))
            {
                db.PriceObservations.Add(new PriceObservation
                {
                    OfferId = offer.Id, ObservedAt = point.ObservedAt, Price = point.Price, Currency = point.Currency, InStock = point.InStock,
                });
                db.DailyPrices.Add(new DailyPrice
                {
                    OfferId = offer.Id, Day = DateOnly.FromDateTime(point.ObservedAt.UtcDateTime),
                    MinPrice = point.Price, Currency = point.Currency, InStock = point.InStock,
                });
                count++;
            }
        }
        await db.SaveChangesAsync(ct);
        return count;
    }

    private static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
