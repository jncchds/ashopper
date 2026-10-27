namespace Ashopper.Infrastructure.Sources;

/// <summary>
/// A plugin that knows where to get products and prices for some markets: an aggregator API,
/// a shop feed, a fake generator... Each source maps its own categories and spec names to the
/// canonical category slugs and attribute keys, so the rest of the app never sees source formats.
/// </summary>
public interface IPriceSource
{
    /// <summary>Short stable key stored with offers, e.g. "fake", "ceneo".</summary>
    string Key { get; }

    /// <summary>Market codes this source covers, e.g. {"PL"}.</summary>
    IReadOnlySet<string> Markets { get; }

    /// <summary>Canonical category slugs this source can discover products for.</summary>
    IReadOnlySet<string> CategorySlugs { get; }

    /// <summary>Current products with their offers in one market and category.</summary>
    IAsyncEnumerable<SourceProduct> DiscoverAsync(string marketCode, string categorySlug, CancellationToken ct);
}

/// <summary>Optional: a source that can also return past prices (used to backfill new offers).</summary>
public interface IHistoricalPriceSource
{
    IEnumerable<SourcePricePoint> GetHistory(string offerExternalId, DateOnly from, DateOnly to);
}

/// <param name="ExternalId">The product's id in the source.</param>
/// <param name="Attributes">Already mapped to canonical keys and invariant-culture values.</param>
public sealed record SourceProduct(
    string ExternalId,
    string Brand,
    string Model,
    string Name,
    string? Ean,
    string? Mpn,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<SourceOffer> Offers);

/// <param name="ShopDomain">Host name used to identify the shop across sources.</param>
public sealed record SourceOffer(
    string ExternalId,
    string ShopName,
    string ShopDomain,
    string Url,
    decimal Price,
    string Currency,
    bool InStock);

public sealed record SourcePricePoint(DateTimeOffset ObservedAt, decimal Price, string Currency, bool InStock);
