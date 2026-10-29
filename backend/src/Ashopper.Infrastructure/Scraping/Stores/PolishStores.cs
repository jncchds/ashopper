namespace Ashopper.Infrastructure.Scraping.Stores;

// Probed 2026-10-29 with one request each and an honest User-Agent; see docs/plans/03-polite-scraper.md.

/// <summary>x-kom.pl: JSON-LD Product with offers, mpn and sku. Works with the default parser.</summary>
public sealed class XKomStore : StoreAdapter
{
    public override string Key => "x-kom";
    public override string Name => "x-kom";
    public override string MarketCode => "PL";
    public override string Domain => "x-kom.pl";
}

/// <summary>
/// mediamarkt.pl: the Product sits inside a JSON-LD BuyAction, with gtin13 and an offer list that may name a
/// marketplace seller. The default parser walks the whole tree, so it needs nothing special.
/// </summary>
public sealed class MediaMarktStore : StoreAdapter
{
    public override string Key => "mediamarkt";
    public override string Name => "MediaMarkt";
    public override string MarketCode => "PL";
    public override string Domain => "mediamarkt.pl";
}

/// <summary>mediaexpert.pl: product pages answer non-browser clients with a captcha challenge (403).</summary>
public sealed class MediaExpertStore : StoreAdapter
{
    public override string Key => "mediaexpert";
    public override string Name => "Media Expert";
    public override string MarketCode => "PL";
    public override string Domain => "mediaexpert.pl";
    public override string? KnownLimitation => "Answers bots with a captcha; checks are expected to end as blocked.";
}

/// <summary>
/// allegro.pl: a marketplace behind DataDome bot protection (403 + captcha for non-browser clients). The product page
/// (/produkt/...) lists many sellers; offerId picks one, so it is part of the canonical URL.
/// </summary>
public sealed class AllegroStore : StoreAdapter
{
    public override string Key => "allegro";
    public override string Name => "Allegro";
    public override string MarketCode => "PL";
    public override string Domain => "allegro.pl";
    public override string? KnownLimitation => "Protected by DataDome; checks are expected to end as blocked. Allegro's REST API is the legitimate route.";
    protected override IReadOnlyCollection<string> KeptQueryParameters => ["offerId"];
}
