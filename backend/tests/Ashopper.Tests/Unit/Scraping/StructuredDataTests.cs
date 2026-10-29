using Ashopper.Infrastructure.Scraping.Parsing;

namespace Ashopper.Tests.Unit.Scraping;

public class StructuredDataTests
{
    internal static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Unit", "Scraping", "Fixtures", name));

    [Fact]
    public void Reads_x_kom_product_page()
    {
        var p = StructuredData.Extract(Fixture("x-kom.html"))!;

        Assert.Equal("ASRock Radeon RX 9070 XT Challenger 16GB GDDR6", p.Name);
        Assert.Equal("ASRock", p.Brand);
        Assert.Equal("RX9070XT CL 16G", p.Mpn);
        Assert.Equal("1455300", p.Sku);
        Assert.Equal(4699m, p.Price);
        Assert.Equal("PLN", p.Currency);
        Assert.True(p.InStock);
    }

    [Fact]
    public void Reads_mediamarkt_product_nested_in_buy_action()
    {
        var p = StructuredData.Extract(Fixture("mediamarkt.html"))!;

        Assert.StartsWith("Myjka wysokociśnieniowa KÄRCHER K 5", p.Name);
        Assert.Equal("KÄRCHER", p.Brand);
        Assert.Equal("4066529204608", p.Ean);
        Assert.Equal(1680m, p.Price);
        Assert.Equal("PLN", p.Currency);
        Assert.True(p.InStock);
        Assert.Equal("Action S.A. PL", p.Seller);
    }

    [Fact]
    public void Graph_with_aggregate_offer_and_string_prices()
    {
        const string html = """
            <script type="application/ld+json">
            {"@context":"https://schema.org","@graph":[
              {"@type":"WebPage","name":"x"},
              {"@type":["Product","Thing"],"name":"Lodówka &amp; zamrażarka","brand":"Bosch","gtin":"04242005196025",
               "offers":{"@type":"AggregateOffer","lowPrice":"2 199,99","priceCurrency":"pln","availability":"OutOfStock"}}
            ]}
            </script>
            """;
        var p = StructuredData.Extract(html)!;

        Assert.Equal("Lodówka & zamrażarka", p.Name);
        Assert.Equal("Bosch", p.Brand);
        Assert.Equal("4242005196025", p.Ean); // GTIN-14 with a leading zero → EAN-13
        Assert.Equal(2199.99m, p.Price);
        Assert.Equal("PLN", p.Currency);
        Assert.False(p.InStock);
    }

    [Fact]
    public void Prefers_cheapest_in_stock_offer()
    {
        const string html = """
            <script type='application/ld+json'>{"@type":"Product","name":"A","offers":[
              {"@type":"Offer","price":90,"priceCurrency":"PLN","availability":"https://schema.org/OutOfStock"},
              {"@type":"Offer","price":120,"priceCurrency":"PLN","availability":"https://schema.org/InStock"},
              {"@type":"Offer","price":110,"priceCurrency":"PLN","availability":"https://schema.org/InStock"}]}</script>
            """;
        var p = StructuredData.Extract(html)!;

        Assert.Equal(110m, p.Price);
        Assert.True(p.InStock);
    }

    [Fact]
    public void Falls_back_to_meta_tags_and_skips_broken_json()
    {
        const string html = """
            <script type="application/ld+json">{ not json </script>
            <meta property="og:title" content="Pralka X"/>
            <meta content="1499,00" property="product:price:amount">
            <meta property="product:price:currency" content="PLN">
            """;
        var p = StructuredData.Extract(html)!;

        Assert.Equal("Pralka X", p.Name);
        Assert.Equal(1499m, p.Price);
        Assert.Equal("PLN", p.Currency);
    }

    [Fact]
    public void No_product_data_returns_null() =>
        Assert.Null(StructuredData.Extract(Fixture("allegro-datadome.html")));

    [Theory]
    [InlineData("4699.00", "4699.00")]
    [InlineData("1 299,99 zł", "1299.99")]
    [InlineData("1.299,99", "1299.99")]
    [InlineData("1,299.99", "1299.99")]
    [InlineData("1,299", "1299")]
    [InlineData("12,5", "12.5")]
    [InlineData("1.299.000", "1299000")]
    public void Parses_price_text(string text, string expected) =>
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), PriceText.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("zł")]
    [InlineData("0")]
    public void Rejects_non_prices(string text) => Assert.Null(PriceText.Parse(text));
}
