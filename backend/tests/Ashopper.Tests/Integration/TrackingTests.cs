using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ashopper.Api.Endpoints;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Scraping;
using Ashopper.Tests.Unit.Scraping;

namespace Ashopper.Tests.Integration;

[Collection(ApiCollection.Name)]
public class TrackingTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Json = AshopperJson.Options;

    private const string XKomLink = "https://www.x-kom.pl/p/1455300-karta-graficzna-amd-asrock-radeon-rx-9070-xt-challenger-16gb-gddr6.html?utm_source=ceneo&ceneo_cid=abc";
    private const string MediaExpertLink = "https://www.mediaexpert.pl/agd/test-product?utm_source=Ceneo";

    [Fact]
    public async Task Tracked_link_gets_a_price_and_can_be_pinned_in_a_list()
    {
        var (client, _) = await api.SignUpAsync();
        var gpu = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Slug == "pc-gpu");

        var created = await client.PostAsJsonAsync("/api/tracked", new TrackUrlRequest(XKomLink, gpu.Id, null), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var tracked = (await created.Content.ReadFromJsonAsync<TrackedUrlDto>(Json))!;
        Assert.Equal("https://www.x-kom.pl/p/1455300-karta-graficzna-amd-asrock-radeon-rx-9070-xt-challenger-16gb-gddr6.html", tracked.Url);
        Assert.Equal(ScrapeStatus.Pending, tracked.Status);

        // Same page from a second link: one tracked URL, not two.
        var again = await client.PostAsJsonAsync("/api/tracked", new TrackUrlRequest(XKomLink.Replace("abc", "xyz"), gpu.Id, null), Json);
        Assert.Equal(tracked.Id, (await again.Content.ReadFromJsonAsync<TrackedUrlDto>(Json))!.Id);

        var shop = new FixtureShop();
        await api.CreateScraper(shop).RunAsync(CancellationToken.None);

        var list = (await client.GetFromJsonAsync<List<TrackedUrlDto>>("/api/tracked", Json))!;
        var item = Assert.Single(list);
        Assert.Equal(ScrapeStatus.Ok, item.Status);
        Assert.Equal(4699m, item.Price);
        Assert.Equal("PLN", item.Currency);
        Assert.Equal("ASRock Radeon RX 9070 XT Challenger 16GB GDDR6", item.Title);
        Assert.Single(item.History);
        Assert.NotNull(item.ProductId);
        Assert.True(item.NextCheckAt > api.Time.GetUtcNow().AddHours(10));

        // The scraped product works like any other: pin it in a list and the list has a current price.
        var listResponse = await client.PostAsJsonAsync("/api/lists", new CreateListRequest("GPU watch", "PL", null), Json);
        var shoppingList = (await listResponse.Content.ReadFromJsonAsync<ListDto>(Json))!;
        (await client.PostAsJsonAsync($"/api/lists/{shoppingList.Id}/requirements",
            new RequirementRequest("That exact card", gpu.Id, 1, [], item.ProductId), Json)).EnsureSuccessStatusCode();
        var current = (await client.GetFromJsonAsync<CurrentDto>($"/api/lists/{shoppingList.Id}/current", Json))!;
        Assert.Equal(4699m, current.Total);

        // Not due again yet: a second pass sends nothing.
        var requestsBefore = shop.Requests.Count;
        await api.CreateScraper(shop).RunAsync(CancellationToken.None);
        Assert.Equal(requestsBefore, shop.Requests.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/tracked/{item.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<TrackedUrlDto>>("/api/tracked", Json))!);
    }

    [Fact]
    public async Task Challenged_store_is_reported_as_blocked()
    {
        var (client, _) = await api.SignUpAsync();
        var fridge = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Slug == "home-fridge");
        (await client.PostAsJsonAsync("/api/tracked", new TrackUrlRequest(MediaExpertLink, fridge.Id, null), Json)).EnsureSuccessStatusCode();

        await api.CreateScraper(new FixtureShop()).RunAsync(CancellationToken.None);

        var item = Assert.Single((await client.GetFromJsonAsync<List<TrackedUrlDto>>("/api/tracked", Json))!);
        Assert.Equal(ScrapeStatus.Blocked, item.Status);
        Assert.Null(item.Price);
        Assert.Contains("challenge", item.LastError);
    }

    [Theory]
    [InlineData("https://www.ceneo.pl/12345")]
    [InlineData("not a url")]
    [InlineData("ftp://x-kom.pl/p/1.html")]
    public async Task Rejects_unsupported_links(string url)
    {
        var (client, _) = await api.SignUpAsync();
        var gpu = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Slug == "pc-gpu");

        var response = await client.PostAsJsonAsync("/api/tracked", new TrackUrlRequest(url, gpu.Id, null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lists_supported_stores()
    {
        var (client, _) = await api.SignUpAsync();
        var stores = (await client.GetFromJsonAsync<List<StoreDto>>("/api/stores", Json))!;
        Assert.Equal(["x-kom", "mediamarkt", "mediaexpert", "allegro"], stores.Select(s => s.Key));
    }

    /// <summary>x-kom serves the fixture page; Media Expert serves a captcha challenge.</summary>
    private sealed class FixtureShop : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            Requests.Add(uri);
            var (status, body) = uri switch
            {
                { AbsolutePath: "/robots.txt" } => (HttpStatusCode.OK, "User-agent: *\nDisallow: /koszyk\n"),
                { Host: "www.x-kom.pl" } => (HttpStatusCode.OK, StructuredDataTests.Fixture("x-kom.html")),
                { Host: "www.mediaexpert.pl" } => (HttpStatusCode.Forbidden, StructuredDataTests.Fixture("allegro-datadome.html")),
                _ => (HttpStatusCode.NotFound, ""),
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body), RequestMessage = request });
        }
    }
}
