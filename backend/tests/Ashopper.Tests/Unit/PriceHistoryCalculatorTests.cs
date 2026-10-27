using Ashopper.Domain.Pricing;

namespace Ashopper.Tests.Unit;

public class PriceHistoryCalculatorTests
{
    private static readonly Guid Ram = Guid.NewGuid(), Cpu = Guid.NewGuid();
    private static readonly Guid ShopA = Guid.NewGuid(), ShopB = Guid.NewGuid();
    private static readonly Guid OfferA = Guid.NewGuid(), OfferB = Guid.NewGuid(), OfferCpu = Guid.NewGuid();
    private static readonly DateOnly D1 = new(2026, 1, 1), D2 = new(2026, 1, 2), D3 = new(2026, 1, 3);
    private static readonly Dictionary<Guid, decimal> NoDiscounts = [];

    private static OfferDayPrice P(Guid req, Guid offer, Guid shop, DateOnly day, decimal price, bool inStock = true) =>
        new(req, offer, shop, day, price, inStock);

    [Fact]
    public void Picks_cheapest_offer_per_requirement_and_multiplies_by_quantity()
    {
        var points = PriceHistoryCalculator.Calculate(
            [new(Ram, 2), new(Cpu, 1)],
            [P(Ram, OfferA, ShopA, D1, 100), P(Ram, OfferB, ShopB, D1, 90), P(Cpu, OfferCpu, ShopA, D1, 500)],
            NoDiscounts, D1, D1);

        var day = Assert.Single(points);
        Assert.True(day.Complete);
        Assert.Equal(90 * 2 + 500, day.Total);
        var ram = day.Items.Single(i => i.RequirementId == Ram);
        Assert.Equal(OfferB, ram.OfferId);
        Assert.Equal(180, ram.LinePrice);
    }

    [Fact]
    public void Shop_discount_can_change_the_winner()
    {
        var discounts = new Dictionary<Guid, decimal> { [ShopA] = 15 }; // 100 → 85 beats 90
        var day = PriceHistoryCalculator.Calculate(
            [new(Ram, 1)], [P(Ram, OfferA, ShopA, D1, 100), P(Ram, OfferB, ShopB, D1, 90)], discounts, D1, D1).Single();

        Assert.Equal(OfferA, day.Items[0].OfferId);
        Assert.Equal(85, day.Total);
    }

    [Fact]
    public void Out_of_stock_is_ignored_and_missing_requirement_marks_day_incomplete()
    {
        var day = PriceHistoryCalculator.Calculate(
            [new(Ram, 1), new(Cpu, 1)],
            [P(Ram, OfferA, ShopA, D1, 50, inStock: false), P(Ram, OfferB, ShopB, D1, 90)],
            NoDiscounts, D1, D1).Single();

        Assert.False(day.Complete);
        Assert.Equal(90, day.Total);
        Assert.Null(day.Items.Single(i => i.RequirementId == Cpu).LinePrice);
    }

    [Fact]
    public void Carries_last_reading_forward_for_a_limited_number_of_days()
    {
        var points = PriceHistoryCalculator.Calculate(
            [new(Ram, 1)], [P(Ram, OfferA, ShopA, D1, 100)], NoDiscounts, D1, D3, carryForwardDays: 1);

        Assert.Equal([true, true, false], points.Select(p => p.Complete));
        Assert.Equal(100, points[1].Total);
    }

    [Fact]
    public void Empty_list_is_never_complete()
    {
        var day = PriceHistoryCalculator.Calculate([], [], NoDiscounts, D1, D1).Single();
        Assert.False(day.Complete);
        Assert.Equal(0, day.Total);
    }

    [Theory]
    [InlineData(100, 0, 100)]
    [InlineData(100, 10, 90)]
    [InlineData(99.99, 5, 94.99)] // 94.9905 rounds to cents
    [InlineData(100, 150, 0)]     // clamped
    public void Discount_apply(decimal price, decimal percent, decimal expected) =>
        Assert.Equal(expected, Discount.Apply(price, percent));

    [Fact]
    public void Daily_price_prefers_in_stock_then_lowest()
    {
        var daily = new DailyPrice { OfferId = OfferA, Day = D1, MinPrice = 80, Currency = "PLN", InStock = false };
        daily.Apply(100, inStock: true);
        Assert.Equal((100m, true), (daily.MinPrice, daily.InStock));
        daily.Apply(70, inStock: false); // cheaper but unavailable: ignored
        Assert.Equal(100, daily.MinPrice);
        daily.Apply(95, inStock: true);
        Assert.Equal(95, daily.MinPrice);
    }
}
