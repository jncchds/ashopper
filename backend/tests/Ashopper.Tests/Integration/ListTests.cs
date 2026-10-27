using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ashopper.Api.Endpoints;
using Ashopper.Domain.Matching;
using Ashopper.Infrastructure.Persistence;

namespace Ashopper.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ListTests(ApiFactory api)
{
    private static readonly JsonSerializerOptions Json = AshopperJson.Options;

    private static readonly List<SpecConstraint> Ddr5Cl30Kit =
    [
        new("memory_type", ConstraintOp.Eq, ["DDR5"]),
        new("capacity_gb", ConstraintOp.Eq, ["32"]),
        new("cas_latency", ConstraintOp.Lte, ["30"]),
    ];

    private async Task<(HttpClient Client, ListDto List)> CreateBuildAsync(List<Guid>? shops = null)
    {
        var (client, _) = await api.SignUpAsync();
        var categories = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!;
        var ram = categories.Single(c => c.Slug == "pc-ram");

        var created = await client.PostAsJsonAsync("/api/lists", new CreateListRequest("Gaming PC", "PL", shops), Json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var list = (await created.Content.ReadFromJsonAsync<ListDto>(Json))!;

        var added = await client.PostAsJsonAsync($"/api/lists/{list.Id}/requirements",
            new RequirementRequest("RAM 2x16 DDR5 CL30", ram.Id, 1, Ddr5Cl30Kit, null), Json);
        added.EnsureSuccessStatusCode();
        return (client, (await added.Content.ReadFromJsonAsync<ListDto>(Json))!);
    }

    [Fact]
    public async Task Requirement_matches_any_product_with_the_specs()
    {
        var (_, list) = await CreateBuildAsync();
        // Three DDR5 2x16GB CL30 kits in the fake catalog; the CL36 and 64GB kits don't qualify.
        Assert.Equal(3, Assert.Single(list.Requirements).MatchCount);
    }

    [Fact]
    public async Task History_has_a_complete_price_for_every_day()
    {
        var (client, list) = await CreateBuildAsync();
        var today = DateOnly.FromDateTime(api.Time.GetUtcNow().UtcDateTime);

        var history = (await client.GetFromJsonAsync<HistoryDto>(
            $"/api/lists/{list.Id}/history?from={today.AddDays(-20):yyyy-MM-dd}&to={today:yyyy-MM-dd}", Json))!;

        Assert.Equal("PLN", history.Currency);
        Assert.Equal(21, history.Points.Count);
        Assert.All(history.Points, p => Assert.True(p.Total > 300 && p.Total < 700, $"{p.Day}: {p.Total}"));
        Assert.True(history.Points.Count(p => p.Complete) >= 20);
    }

    [Fact]
    public async Task History_supports_conditional_requests_and_changes_with_discounts()
    {
        var (client, list) = await CreateBuildAsync();
        var url = $"/api/lists/{list.Id}/history";

        var first = await client.GetAsync(url);
        first.EnsureSuccessStatusCode();
        var etag = first.Headers.ETag!;
        Assert.Contains("private", first.Headers.CacheControl!.ToString());

        var again = new HttpRequestMessage(HttpMethod.Get, url);
        again.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);

        // A new personal discount must invalidate the cached history.
        var current = (await client.GetFromJsonAsync<CurrentDto>($"/api/lists/{list.Id}/current", Json))!;
        var bestShop = current.Items[0].Best!.ShopId;
        (await client.PutAsJsonAsync($"/api/me/shop-discounts/{bestShop}", new SetDiscountRequest(10), Json)).EnsureSuccessStatusCode();

        var afterDiscount = new HttpRequestMessage(HttpMethod.Get, url);
        afterDiscount.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(afterDiscount)).StatusCode);

        var discounted = (await client.GetFromJsonAsync<CurrentDto>($"/api/lists/{list.Id}/current", Json))!;
        Assert.True(discounted.Total < current.Total);
    }

    [Fact]
    public async Task Allowed_shops_restrict_which_offers_count()
    {
        var (client, list) = await CreateBuildAsync();
        var shops = (await client.GetFromJsonAsync<List<ShopDto>>("/api/shops?market=PL", Json))!;
        var current = (await client.GetFromJsonAsync<CurrentDto>($"/api/lists/{list.Id}/current", Json))!;
        var otherShop = shops.First(s => s.Id != current.Items[0].Best!.ShopId);

        (await client.PutAsJsonAsync($"/api/lists/{list.Id}", new UpdateListRequest(list.Name, [otherShop.Id]), Json))
            .EnsureSuccessStatusCode();

        var restricted = (await client.GetFromJsonAsync<CurrentDto>($"/api/lists/{list.Id}/current", Json))!;
        Assert.All(restricted.Items.Where(i => i.Best is not null), i => Assert.Equal(otherShop.Id, i.Best!.ShopId));
    }

    [Fact]
    public async Task Invalid_constraints_are_rejected()
    {
        var (client, list) = await CreateBuildAsync();
        var ram = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Slug == "pc-ram");

        var response = await client.PostAsJsonAsync($"/api/lists/{list.Id}/requirements",
            new RequirementRequest("Bad", ram.Id, 1, [new("warp_speed", ConstraintOp.Gte, ["9"])], null), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_lists()
    {
        var (_, list) = await CreateBuildAsync();
        var (stranger, _) = await api.SignUpAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/lists/{list.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/lists/{list.Id}/history")).StatusCode);
    }

    [Fact]
    public async Task Catalog_is_public_cacheable_and_revalidates()
    {
        var client = api.CreateBareClient();
        var first = await client.GetAsync("/api/categories");
        first.EnsureSuccessStatusCode();
        Assert.Contains("public", first.Headers.CacheControl!.ToString());

        var again = new HttpRequestMessage(HttpMethod.Get, "/api/categories");
        again.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
    }

    [Fact]
    public async Task Product_search_previews_matches_with_prices()
    {
        var (client, _) = await api.SignUpAsync();
        var ram = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Json))!.Single(c => c.Slug == "pc-ram");

        var response = await client.PostAsJsonAsync("/api/products/search", new ProductSearchRequest(ram.Id, "PL", Ddr5Cl30Kit, null), Json);
        var result = (await response.Content.ReadFromJsonAsync<ProductSearchResult>(Json))!;

        Assert.Equal(3, result.Total);
        Assert.All(result.Items, p => Assert.NotNull(p.LowestPrice));
        Assert.Equal(result.Items.OrderBy(p => p.LowestPrice).Select(p => p.Id), result.Items.Select(p => p.Id));
    }
}
