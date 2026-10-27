using System.Reflection;
using Ashopper.Api.Http;
using Ashopper.Domain.Catalog;
using Ashopper.Domain.Matching;
using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace Ashopper.Api.Endpoints;

public sealed record MarketDto(string Code, string Name, string Currency, string Locale);
public sealed record CategoryDto(Guid Id, Guid? ParentId, string Slug, string Name, IReadOnlyList<AttributeDefinition> Attributes);
public sealed record ShopDto(Guid Id, string MarketCode, string Name, string Domain);
public sealed record ProductSearchRequest(Guid CategoryId, string MarketCode, List<SpecConstraint>? Constraints, List<Guid>? ShopIds, int Take = 50);
public sealed record ProductDto(Guid Id, string Name, string Brand, string Model, Dictionary<string, string> Attributes, decimal? LowestPrice, int OfferCount);
public sealed record ProductSearchResult(int Total, IReadOnlyList<ProductDto> Items);

public static class CatalogEndpoints
{
    /// <summary>Changes on every deploy, so cached catalog responses don't outlive a schema/seed change.</summary>
    private static readonly string AppVersion =
        typeof(CatalogEndpoints).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

    private const string PublicCache = "public, max-age=300";

    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").WithTags("Catalog");
        api.MapGet("/markets", GetMarketsAsync).CacheOutput(HttpCaching.CatalogPolicy);
        api.MapGet("/categories", GetCategoriesAsync).CacheOutput(HttpCaching.CatalogPolicy);
        api.MapGet("/shops", GetShopsAsync).CacheOutput(HttpCaching.CatalogPolicy);
        api.MapPost("/products/search", SearchProductsAsync);
    }

    private static async Task<IResult> GetMarketsAsync(HttpContext http, AppDbContext db, ListPricingQuery pricing, CancellationToken ct)
    {
        if (HttpCaching.IsNotModified(http, PublicCache, AppVersion, await pricing.GetLastCollectedAtAsync(ct)))
            return HttpCaching.NotModified();
        var markets = await db.Markets.AsNoTracking().OrderBy(m => m.Code)
            .Select(m => new MarketDto(m.Code, m.Name, m.Currency, m.Locale)).ToListAsync(ct);
        return Results.Ok(markets);
    }

    private static async Task<IResult> GetCategoriesAsync(HttpContext http, AppDbContext db, ListPricingQuery pricing, CancellationToken ct)
    {
        if (HttpCaching.IsNotModified(http, PublicCache, AppVersion, await pricing.GetLastCollectedAtAsync(ct)))
            return HttpCaching.NotModified();
        var categories = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        return Results.Ok(categories.Select(c => new CategoryDto(c.Id, c.ParentId, c.Slug, c.Name, c.Attributes)));
    }

    private static async Task<IResult> GetShopsAsync(string market, HttpContext http, AppDbContext db, ListPricingQuery pricing, CancellationToken ct)
    {
        if (HttpCaching.IsNotModified(http, PublicCache, AppVersion, market, await pricing.GetLastCollectedAtAsync(ct)))
            return HttpCaching.NotModified();
        var shops = await db.Shops.AsNoTracking().Where(s => s.MarketCode == market).OrderBy(s => s.Name)
            .Select(s => new ShopDto(s.Id, s.MarketCode, s.Name, s.Domain)).ToListAsync(ct);
        return Results.Ok(shops);
    }

    /// <summary>Live preview for the requirement editor: which products match these constraints, and from what price.</summary>
    private static async Task<IResult> SearchProductsAsync(
        ProductSearchRequest req, AppDbContext db, MatchRebuilder matcher, TimeProvider time, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == req.CategoryId, ct);
        if (category is null)
            return Validation.Problem("categoryId", "Unknown category.");
        var constraints = req.Constraints ?? [];
        if (Validation.Constraints(constraints, category) is { } error)
            return error;

        var matched = (await matcher.LoadCandidatesAsync(category.Id, ct))
            .Where(p => SpecMatcher.Matches(p.Attributes, constraints))
            .ToList();
        var ids = matched.Select(p => p.Id).ToList();
        var since = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-2);
        var shopIds = req.ShopIds ?? [];

        var priceQuery =
            from o in db.Offers
            join s in db.Shops on o.ShopId equals s.Id
            join d in db.DailyPrices on o.Id equals d.OfferId
            where ids.Contains(o.ProductId) && s.MarketCode == req.MarketCode && d.Day >= since && d.InStock
            select new { o.ProductId, o.ShopId, OfferId = o.Id, d.MinPrice };
        if (shopIds.Count > 0)
            priceQuery = priceQuery.Where(x => shopIds.Contains(x.ShopId));

        var prices = (await priceQuery.ToListAsync(ct))
            .GroupBy(x => x.ProductId)
            .ToDictionary(g => g.Key, g => (Min: g.Min(x => x.MinPrice), Offers: g.Select(x => x.OfferId).Distinct().Count()));

        var items = matched
            .Select(p => new ProductDto(p.Id, p.Name, p.Brand, p.Model, p.Attributes,
                prices.TryGetValue(p.Id, out var pr) ? pr.Min : null, prices.TryGetValue(p.Id, out var po) ? po.Offers : 0))
            .OrderBy(p => p.LowestPrice ?? decimal.MaxValue)
            .Take(Math.Clamp(req.Take, 1, 200))
            .ToList();

        return Results.Ok(new ProductSearchResult(matched.Count, items));
    }
}
