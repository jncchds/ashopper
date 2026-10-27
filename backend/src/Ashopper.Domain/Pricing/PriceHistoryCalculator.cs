namespace Ashopper.Domain.Pricing;

public sealed record RequirementQuantity(Guid RequirementId, int Quantity);

/// <summary>The lowest price of one offer on one day, for an offer that matches the requirement.</summary>
public sealed record OfferDayPrice(Guid RequirementId, Guid OfferId, Guid ShopId, DateOnly Day, decimal Price, bool InStock);

/// <summary>Cheapest choice for one requirement on one day. Prices are null when nothing matched.</summary>
public sealed record RequirementDayPrice(Guid RequirementId, decimal? UnitPrice, decimal? LinePrice, Guid? OfferId, Guid? ShopId);

/// <param name="Total">Sum of the cheapest line prices that were found.</param>
/// <param name="Complete">True when every requirement had a price that day.</param>
public sealed record HistoryPoint(DateOnly Day, decimal Total, bool Complete, IReadOnlyList<RequirementDayPrice> Items);

public static class PriceHistoryCalculator
{
    /// <summary>
    /// For each day in [from, to]: the cheapest in-stock offer per requirement (after the user's shop
    /// discounts) times quantity, and the list total.
    /// </summary>
    /// <param name="carryForwardDays">
    /// If an offer has no reading on a day, reuse its reading from up to this many days earlier,
    /// so one missed collector run doesn't punch a hole in the chart.
    /// </param>
    public static IReadOnlyList<HistoryPoint> Calculate(
        IReadOnlyList<RequirementQuantity> requirements,
        IEnumerable<OfferDayPrice> prices,
        IReadOnlyDictionary<Guid, decimal> discountPercentByShop,
        DateOnly from,
        DateOnly to,
        int carryForwardDays = 2)
    {
        // requirement → offer → day → price
        var byRequirement = prices
            .Where(p => p.InStock)
            .GroupBy(p => p.RequirementId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(p => p.OfferId).Select(o => o.ToDictionary(p => p.Day)).ToList());

        var points = new List<HistoryPoint>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var items = new List<RequirementDayPrice>(requirements.Count);
            decimal total = 0;
            var complete = true;

            foreach (var req in requirements)
            {
                OfferDayPrice? best = null;
                decimal bestPrice = 0;

                if (byRequirement.TryGetValue(req.RequirementId, out var offers))
                {
                    foreach (var offerDays in offers)
                    {
                        if (FindReading(offerDays, day, carryForwardDays) is not { } reading)
                            continue;
                        var effective = Discount.Apply(reading.Price, discountPercentByShop.GetValueOrDefault(reading.ShopId));
                        if (best is null || effective < bestPrice)
                        {
                            best = reading;
                            bestPrice = effective;
                        }
                    }
                }

                if (best is null)
                {
                    complete = false;
                    items.Add(new RequirementDayPrice(req.RequirementId, null, null, null, null));
                }
                else
                {
                    var line = bestPrice * req.Quantity;
                    total += line;
                    items.Add(new RequirementDayPrice(req.RequirementId, bestPrice, line, best.OfferId, best.ShopId));
                }
            }

            points.Add(new HistoryPoint(day, total, complete && requirements.Count > 0, items));
        }

        return points;
    }

    private static OfferDayPrice? FindReading(Dictionary<DateOnly, OfferDayPrice> days, DateOnly day, int carryForwardDays)
    {
        for (var back = 0; back <= carryForwardDays; back++)
        {
            if (days.TryGetValue(day.AddDays(-back), out var reading))
                return reading;
        }
        return null;
    }
}

public static class Discount
{
    public static decimal Apply(decimal price, decimal percent) =>
        percent <= 0 ? price : Math.Round(price * (1 - Math.Min(percent, 100) / 100m), 2, MidpointRounding.AwayFromZero);
}
