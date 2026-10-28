using System.Runtime.CompilerServices;

namespace Ashopper.Infrastructure.Sources.Fake;

/// <summary>
/// Deterministic demo data: every (offer, day) always yields the same price, so history can be
/// backfilled and re-collected without contradictions. Prices drift slowly, wave, and dip in promo weeks.
/// </summary>
public sealed class FakePriceSource(TimeProvider time) : IPriceSource, IHistoricalPriceSource
{
    public const string SourceKey = "fake";
    private static readonly DateOnly Epoch = new(2025, 1, 1);

    public string Key => SourceKey;
    public IReadOnlySet<string> Markets { get; } = FakeCatalog.Shops.Keys.ToHashSet();
    public IReadOnlySet<string> CategorySlugs { get; } = FakeCatalog.Items.Keys.ToHashSet();

    public async IAsyncEnumerable<SourceProduct> DiscoverAsync(
        string marketCode, string categorySlug, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!FakeCatalog.Items.TryGetValue(categorySlug, out var items) || !FakeCatalog.Shops.TryGetValue(marketCode, out var shops))
            yield break;

        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var (currency, _) = FakeCatalog.Currencies[marketCode];

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            var offers = new List<SourceOffer>();
            for (var i = 0; i < shops.Length; i++)
            {
                // Each product is sold by a stable subset of shops (at least one).
                if (i > 0 && Hash(item.Id, shops[i].Domain) % 3 == 0)
                    continue;

                var offerId = OfferId(marketCode, shops[i].Domain, item.Id);
                var (price, inStock) = PriceOn(offerId, today);
                offers.Add(new SourceOffer(
                    offerId, shops[i].Name, shops[i].Domain, $"https://{shops[i].Domain}/p/{item.Id}", price, currency, inStock));
            }

            yield return new SourceProduct(item.Id, item.Brand, item.Model, $"{item.Brand} {item.Model}", item.Ean, null, item.Attributes, offers);
        }

        await Task.CompletedTask;
    }

    public IEnumerable<SourcePricePoint> GetHistory(string offerExternalId, DateOnly from, DateOnly to)
    {
        var currency = FakeCatalog.Currencies[offerExternalId[..2]].Currency;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var (price, inStock) = PriceOn(offerExternalId, day);
            yield return new SourcePricePoint(new DateTimeOffset(day.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero), price, currency, inStock);
        }
    }

    private static string OfferId(string market, string shopDomain, string itemId) => $"{market}:{shopDomain}:{itemId}";

    private static (decimal Price, bool InStock) PriceOn(string offerId, DateOnly day)
    {
        var parts = offerId.Split(':');
        var (market, shopDomain, itemId) = (parts[0], parts[1], parts[2]);
        var item = FakeCatalog.Items.Values.SelectMany(x => x).First(x => x.Id == itemId);
        var shop = FakeCatalog.Shops[market].First(s => s.Domain == shopDomain);
        var rate = FakeCatalog.Currencies[market].Rate;

        var seed = Hash(offerId);
        var t = day.DayNumber - Epoch.DayNumber;
        var period = 45 + (int)(seed % 60);                       // days per price wave
        var phase = seed % 1000 / 1000.0 * 2 * Math.PI;
        var wave = 1 + 0.06 * Math.Sin(2 * Math.PI * t / period + phase);
        var drift = 1 - 0.00012 * t;                               // ~4% cheaper per year
        var noise = 1 + ((int)(Hash(offerId, t.ToString()) % 200) - 100) / 10000.0; // ±1% (int first: uint would wrap)
        var promo = Hash(offerId, (t / 7).ToString()) % 11 == 0 ? 0.88 : 1.0; // some weeks are sales

        var raw = (double)(item.BasePricePln * shop.Factor * rate) * wave * drift * noise * promo;
        var price = Math.Floor((decimal)raw) + 0.99m;              // shops love .99
        var inStock = Hash(offerId, "stock", t.ToString()) % 25 != 0;
        return (price, inStock);
    }

    /// <summary>FNV-1a: stable across processes, unlike string.GetHashCode.</summary>
    private static uint Hash(params string[] parts)
    {
        var h = 2166136261u;
        foreach (var part in parts)
        {
            foreach (var c in part)
            {
                h ^= c;
                h *= 16777619u;
            }
            h ^= '|';
            h *= 16777619u;
        }
        return h;
    }
}
