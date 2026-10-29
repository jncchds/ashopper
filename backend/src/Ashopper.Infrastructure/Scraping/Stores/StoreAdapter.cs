using Ashopper.Infrastructure.Scraping.Parsing;

namespace Ashopper.Infrastructure.Scraping.Stores;

/// <summary>
/// Everything store-specific about scraping one shop: which hosts it owns, what its canonical product URL looks like and
/// how to read a product page. The defaults (schema.org JSON-LD + OpenGraph) fit most shops; override what differs.
/// To add a store: subclass this, register it in <see cref="StoreRegistry.Default"/>, and add a fixture test.
/// </summary>
public abstract class StoreAdapter
{
    /// <summary>Short stable key stored on tracked URLs, e.g. "x-kom".</summary>
    public abstract string Key { get; }

    /// <summary>Display name, also used for the shop row.</summary>
    public abstract string Name { get; }

    public abstract string MarketCode { get; }

    /// <summary>The shop's domain (the <c>Shop.Domain</c> key), e.g. "x-kom.pl".</summary>
    public abstract string Domain { get; }

    /// <summary>
    /// What we know about this store's bot protection, shown in the UI before anyone tries. Null when pages load fine.
    /// The live state (blocked or not) comes from <see cref="ScrapeHost"/>.
    /// </summary>
    public virtual string? KnownLimitation => null;

    /// <summary>Query parameters that identify the product (kept by <see cref="Canonicalize"/>). All others are dropped.</summary>
    protected virtual IReadOnlyCollection<string> KeptQueryParameters => [];

    /// <summary>The domain itself and any subdomain of it ("www.x-kom.pl", "m.x-kom.pl").</summary>
    public bool Handles(Uri url) =>
        url.Host.Equals(Domain, StringComparison.OrdinalIgnoreCase)
        || url.Host.EndsWith("." + Domain, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the URL that identifies the product page: https, lower-case host, no fragment and no tracking parameters
    /// (utm_*, ceneo_*, ...). Two links to the same product must canonicalize to the same string.
    /// </summary>
    public virtual Uri Canonicalize(Uri url)
    {
        var builder = new UriBuilder(url) { Scheme = Uri.UriSchemeHttps, Port = -1, Fragment = "", Host = url.Host.ToLowerInvariant() };
        var kept = ParseQuery(url.Query)
            .Where(kv => KeptQueryParameters.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
        builder.Query = string.Join("&", kept);
        return builder.Uri;
    }

    /// <summary>Reads the product page. Null when the page has no product data at all.</summary>
    public virtual ScrapedProduct? Parse(string html, Uri url) => StructuredData.Extract(html);

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Select(kv => new KeyValuePair<string, string>(
                Uri.UnescapeDataString(kv[0]), kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : ""));
}

/// <summary>The stores the scraper supports. Adding a store means adding it to <see cref="Default"/>.</summary>
public sealed class StoreRegistry(IEnumerable<StoreAdapter> stores)
{
    public static StoreAdapter[] Default => [new XKomStore(), new MediaMarktStore(), new MediaExpertStore(), new AllegroStore()];

    public IReadOnlyList<StoreAdapter> All { get; } = stores.ToList();

    public StoreAdapter? Find(Uri url) => All.FirstOrDefault(s => s.Handles(url));

    public StoreAdapter? Get(string key) => All.FirstOrDefault(s => s.Key == key);
}
