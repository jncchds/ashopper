using Ashopper.Infrastructure.Sources.Fake;
using Microsoft.Extensions.Time.Testing;

namespace Ashopper.Tests.Unit;

public class FakePriceSourceTests
{
    private readonly FakePriceSource _source = new(new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task Discovery_returns_products_with_offers_in_market_currency()
    {
        var products = await _source.DiscoverAsync("UA", "pc-ram", CancellationToken.None).ToListAsync();

        Assert.NotEmpty(products);
        Assert.All(products, p => Assert.NotEmpty(p.Offers));
        Assert.All(products.SelectMany(p => p.Offers), o => Assert.Equal("UAH", o.Currency));
    }

    [Fact]
    public async Task History_is_deterministic_and_matches_todays_price()
    {
        var offer = (await _source.DiscoverAsync("PL", "pc-gpu", CancellationToken.None).FirstAsync()).Offers[0];
        var today = new DateOnly(2026, 6, 1);

        var first = _source.GetHistory(offer.ExternalId, today.AddDays(-30), today).ToList();
        var second = _source.GetHistory(offer.ExternalId, today.AddDays(-30), today).ToList();

        Assert.Equal(31, first.Count);
        Assert.Equal(first, second);
        Assert.Equal(offer.Price, first[^1].Price);
    }

    [Fact]
    public async Task Unknown_market_or_category_yields_nothing()
    {
        Assert.Empty(await _source.DiscoverAsync("DE", "pc-ram", CancellationToken.None).ToListAsync());
        Assert.Empty(await _source.DiscoverAsync("PL", "garden-hose", CancellationToken.None).ToListAsync());
    }
}
