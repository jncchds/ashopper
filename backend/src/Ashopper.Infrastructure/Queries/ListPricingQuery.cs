using Ashopper.Domain.Lists;
using Ashopper.Domain.Pricing;
using Ashopper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ashopper.Infrastructure.Queries;

public sealed record BestOffer(
    Guid RequirementId, Guid OfferId, Guid ProductId, string ProductName, Guid ShopId, string ShopName,
    string Url, decimal Price, decimal EffectivePrice, decimal LinePrice);

/// <summary>Price history and current best offers for a shopping list, honoring allowed shops and user discounts.</summary>
public sealed class ListPricingQuery(AppDbContext db)
{
    private const int CarryForwardDays = 2;

    public async Task<IReadOnlyList<HistoryPoint>> GetHistoryAsync(
        ShoppingList list, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var prices = await LoadPricesAsync(list, from.AddDays(-CarryForwardDays), to, ct);
        var discounts = await LoadDiscountsAsync(list.OwnerUserId, ct);
        var requirements = list.Requirements.Select(r => new RequirementQuantity(r.Id, r.Quantity)).ToList();
        return PriceHistoryCalculator.Calculate(requirements, prices, discounts, from, to, CarryForwardDays);
    }

    /// <summary>Cheapest current offer per requirement (null entries = nothing available).</summary>
    public async Task<IReadOnlyDictionary<Guid, BestOffer?>> GetCurrentAsync(ShoppingList list, DateOnly today, CancellationToken ct)
    {
        var point = (await GetHistoryAsync(list, today, today, ct)).Single();
        var offerIds = point.Items.Where(i => i.OfferId is not null).Select(i => i.OfferId!.Value).ToList();

        var details = await (
                from o in db.Offers
                join p in db.Products on o.ProductId equals p.Id
                join s in db.Shops on o.ShopId equals s.Id
                where offerIds.Contains(o.Id)
                select new { o.Id, o.Url, ProductId = p.Id, ProductName = p.Name, ShopId = s.Id, ShopName = s.Name })
            .ToDictionaryAsync(x => x.Id, ct);

        var rawPrices = await db.DailyPrices
            .Where(d => offerIds.Contains(d.OfferId) && d.Day <= today && d.Day >= today.AddDays(-CarryForwardDays))
            .GroupBy(d => d.OfferId)
            .Select(g => new { OfferId = g.Key, Price = g.OrderByDescending(d => d.Day).Select(d => d.MinPrice).First() })
            .ToDictionaryAsync(x => x.OfferId, x => x.Price, ct);

        return point.Items.ToDictionary(
            i => i.RequirementId,
            i => i.OfferId is { } id && details.TryGetValue(id, out var d)
                ? new BestOffer(i.RequirementId, id, d.ProductId, d.ProductName, d.ShopId, d.ShopName, d.Url,
                    rawPrices.GetValueOrDefault(id, i.UnitPrice!.Value), i.UnitPrice!.Value, i.LinePrice!.Value)
                : null);
    }

    /// <summary>Number of matching products per requirement.</summary>
    public async Task<Dictionary<Guid, int>> GetMatchCountsAsync(IEnumerable<Guid> requirementIds, CancellationToken ct)
    {
        var ids = requirementIds.ToList();
        return await db.RequirementMatches
            .Where(m => ids.Contains(m.RequirementId))
            .GroupBy(m => m.RequirementId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
    }

    /// <summary>When prices last changed; used as the cache validator for price responses.</summary>
    public async Task<DateTimeOffset?> GetLastCollectedAtAsync(CancellationToken ct) =>
        await db.CollectorRuns.Where(r => r.Succeeded).MaxAsync(r => r.FinishedAt, ct);

    private async Task<List<OfferDayPrice>> LoadPricesAsync(ShoppingList list, DateOnly fromDay, DateOnly toDay, CancellationToken ct)
    {
        var requirementIds = list.Requirements.Select(r => r.Id).ToList();
        var allowed = list.AllowedShopIds;
        var currency = await db.Markets.Where(m => m.Code == list.MarketCode).Select(m => m.Currency).SingleAsync(ct);

        var query =
            from m in db.RequirementMatches
            join o in db.Offers on m.ProductId equals o.ProductId
            join s in db.Shops on o.ShopId equals s.Id
            join d in db.DailyPrices on o.Id equals d.OfferId
            where requirementIds.Contains(m.RequirementId)
                  && s.MarketCode == list.MarketCode
                  && d.Currency == currency
                  && d.Day >= fromDay && d.Day <= toDay
            select new { m.RequirementId, OfferId = o.Id, o.ShopId, d.Day, d.MinPrice, d.InStock };

        if (allowed.Count > 0)
            query = query.Where(x => allowed.Contains(x.ShopId));

        return (await query.ToListAsync(ct))
            .Select(x => new OfferDayPrice(x.RequirementId, x.OfferId, x.ShopId, x.Day, x.MinPrice, x.InStock))
            .ToList();
    }

    private Task<Dictionary<Guid, decimal>> LoadDiscountsAsync(Guid userId, CancellationToken ct) =>
        db.UserShopDiscounts.Where(d => d.UserId == userId).ToDictionaryAsync(d => d.ShopId, d => d.Percent, ct);
}
