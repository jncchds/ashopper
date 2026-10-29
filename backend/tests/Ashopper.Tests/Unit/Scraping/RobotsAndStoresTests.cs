using Ashopper.Infrastructure.Scraping;
using Ashopper.Infrastructure.Scraping.Stores;

namespace Ashopper.Tests.Unit.Scraping;

public class RobotsTxtTests
{
    [Fact]
    public void Uses_star_group_with_longest_match()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: Googlebot
            Disallow: /

            User-agent: *
            Disallow: /product/
            Allow: /product/public/
            Disallow: *search?query*
            Disallow: /*.pdf$
            """, "AshopperBot");

        Assert.True(robots.IsAllowed("/komputery/sluchawki-steelseries"));
        Assert.False(robots.IsAllowed("/product/123"));
        Assert.True(robots.IsAllowed("/product/public/123"));
        Assert.False(robots.IsAllowed("/x/search?query=abc"));
        Assert.False(robots.IsAllowed("/files/manual.pdf"));
        Assert.True(robots.IsAllowed("/files/manual.pdf?download=1"));
    }

    [Fact]
    public void Own_group_wins_over_star_and_crawl_delay_is_read()
    {
        var robots = RobotsTxt.Parse("""
            User-agent: *
            Disallow:

            User-agent: OtherBot
            User-agent: AshopperBot
            Disallow: /p/
            Crawl-delay: 45
            """, "AshopperBot");

        Assert.False(robots.IsAllowed("/p/1"));
        Assert.True(robots.IsAllowed("/c/1"));
        Assert.Equal(TimeSpan.FromSeconds(45), robots.CrawlDelay);
    }

    [Fact]
    public void Empty_disallow_allows_everything() =>
        Assert.True(RobotsTxt.Parse("User-agent: *\nDisallow:\n", "AshopperBot").IsAllowed("/anything"));

    [Theory]
    [InlineData("https://www.x-kom.pl/p/1512563-sluchawki-bezprzewodowe-steelseries-arctis-nova-pro-omni-black.html")]
    [InlineData("https://mediamarkt.pl/pl/product/_sluchawki-steelseries-arctis-nova-pro-omni-czarny-1503631.html")]
    [InlineData("https://www.mediaexpert.pl/komputery-i-tablety/sluchawki-i-glosniki-komputerowe/sluchawki-z-mikrofonem/sluchawki-steelseries-arctis-nova-pro-omni-bezprzewodowe-nauszne-dzwiek-przestrzenny-czarny")]
    [InlineData("https://allegro.pl/produkt/sluchawki-bezprzewodowe-steelseries-arctis-nova-pro-omni-czarne-b04840b6-3f9d-49a8-99d8-4d66f8802fff?offerId=18548111058")]
    public void Stores_product_pages_are_allowed_by_their_robots_txt_rules(string url)
    {
        // The wildcard rules each store published on 2026-10-29 that could touch product pages.
        var robots = RobotsTxt.Parse("""
            User-agent: *
            Disallow: /product/
            Disallow: /search/
            Disallow: *search?query*
            Disallow: /offer/
            Disallow: /koszyk
            Disallow: *?filter*
            Disallow: *query*
            Disallow: /*?*=*&*=*&*=*&*=*&*=*&*=*&*=*&*=*&*=*&*=*&*
            """, "AshopperBot");

        Assert.True(robots.IsAllowed(new Uri(url).PathAndQuery));
    }
}

public class StoreAdapterTests
{
    private readonly StoreRegistry _stores = new(StoreRegistry.Default);

    [Theory]
    [InlineData(
        "https://www.mediaexpert.pl/komputery-i-tablety/sluchawki-i-glosniki-komputerowe/sluchawki-z-mikrofonem/sluchawki-steelseries-arctis-nova-pro-omni-bezprzewodowe-nauszne-dzwiek-przestrzenny-czarny?utm_source=Ceneo&utm_medium=cpc&utm_content=2121574&utm_campaign=2026-10&utm_term=Sluchawki-z-mikrofonem&ceneo_spo=true&ceneo_cid=c9801bf5-ca4d-d77d-3654-98f138ba7768",
        "mediaexpert",
        "https://www.mediaexpert.pl/komputery-i-tablety/sluchawki-i-glosniki-komputerowe/sluchawki-z-mikrofonem/sluchawki-steelseries-arctis-nova-pro-omni-bezprzewodowe-nauszne-dzwiek-przestrzenny-czarny")]
    [InlineData(
        "https://www.x-kom.pl/p/1512563-sluchawki-bezprzewodowe-steelseries-arctis-nova-pro-omni-black.html?utm_source=ceneo&utm_medium=comparison&utm_campaign=ceneo_click&sm=comparision&ceneo_cid=b93bb08f-2e2a-d36a-0d1f-b300515b0d78",
        "x-kom",
        "https://www.x-kom.pl/p/1512563-sluchawki-bezprzewodowe-steelseries-arctis-nova-pro-omni-black.html")]
    [InlineData(
        "https://mediamarkt.pl/pl/product/_sluchawki-steelseries-arctis-nova-pro-omni-czarny-1503631.html?utm_source=ceneo&utm_medium=v2-psm&utm_campaign=pl|mm|rt|v2-psm|pfm|nsp|ongoing|na|d2c|na|mbb51554371&utm_term=1503631&utm_content=mbb51554371&cr_pro_id=1503631&bid=true&ceneo_cid=fba4c420-4385-3b17-2777-d1bb86f826ff",
        "mediamarkt",
        "https://mediamarkt.pl/pl/product/_sluchawki-steelseries-arctis-nova-pro-omni-czarny-1503631.html")]
    [InlineData(
        "https://allegro.pl/produkt/sluchawki-bezprzewodowe-steelseries-arctis-nova-pro-omni-czarne-b04840b6-3f9d-49a8-99d8-4d66f8802fff?utm_source=x&offerId=18548111058#opinie",
        "allegro",
        "https://allegro.pl/produkt/sluchawki-bezprzewodowe-steelseries-arctis-nova-pro-omni-czarne-b04840b6-3f9d-49a8-99d8-4d66f8802fff?offerId=18548111058")]
    [InlineData("http://WWW.X-KOM.PL/p/1-a.html#specyfikacja", "x-kom", "https://www.x-kom.pl/p/1-a.html")]
    public void Recognizes_store_and_strips_tracking(string url, string storeKey, string canonical)
    {
        var store = _stores.Find(new Uri(url));

        Assert.NotNull(store);
        Assert.Equal(storeKey, store.Key);
        Assert.Equal(canonical, store.Canonicalize(new Uri(url)).AbsoluteUri);
    }

    [Theory]
    [InlineData("https://www.ceneo.pl/123")]
    [InlineData("https://notx-kom.pl/p/1.html")]
    [InlineData("https://x-kom.pl.evil.example/p/1.html")]
    public void Unknown_hosts_are_not_matched(string url) => Assert.Null(_stores.Find(new Uri(url)));
}
