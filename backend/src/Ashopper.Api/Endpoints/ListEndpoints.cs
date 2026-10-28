using Ashopper.Api.Auth;
using Ashopper.Api.Http;
using Ashopper.Domain.Lists;
using Ashopper.Domain.Matching;
using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Queries;
using Microsoft.EntityFrameworkCore;

namespace Ashopper.Api.Endpoints;

public sealed record ListSummaryDto(Guid Id, string Name, string MarketCode, int RequirementCount, DateTimeOffset UpdatedAt);
public sealed record RequirementDto(Guid Id, string Title, Guid CategoryId, int Quantity, IReadOnlyList<SpecConstraint> Constraints, Guid? PinnedProductId, int MatchCount);
public sealed record ListDto(Guid Id, string Name, string MarketCode, IReadOnlyList<Guid> AllowedShopIds, IReadOnlyList<RequirementDto> Requirements, DateTimeOffset UpdatedAt);
public sealed record CreateListRequest(string Name, string MarketCode, List<Guid>? AllowedShopIds);
public sealed record UpdateListRequest(string Name, List<Guid> AllowedShopIds);
public sealed record RequirementRequest(string Title, Guid CategoryId, int Quantity, List<SpecConstraint>? Constraints, Guid? PinnedProductId);
public sealed record HistoryItemDto(Guid RequirementId, decimal? LinePrice, Guid? ShopId);
public sealed record HistoryPointDto(DateOnly Day, decimal Total, bool Complete, IReadOnlyList<HistoryItemDto> Items);
public sealed record HistoryDto(string Currency, DateOnly From, DateOnly To, IReadOnlyList<HistoryPointDto> Points);
public sealed record CurrentItemDto(Guid RequirementId, BestOffer? Best);
public sealed record CurrentDto(string Currency, decimal Total, bool Complete, IReadOnlyList<CurrentItemDto> Items);

public static class ListEndpoints
{
    private const int MaxListsPerUser = 50;
    private const int MaxRequirementsPerList = 50;
    private const int MaxHistoryDays = 730;
    private const string PrivateRevalidate = "private, no-cache";

    public static void MapListEndpoints(this IEndpointRouteBuilder app)
    {
        var lists = app.MapGroup("/api/lists").WithTags("Lists").RequireAuthorization();
        lists.MapGet("/", GetListsAsync).Produces<List<ListSummaryDto>>();
        lists.MapPost("/", CreateListAsync).Produces<ListDto>(StatusCodes.Status201Created);
        lists.MapGet("/{id:guid}", GetListAsync).Produces<ListDto>();
        lists.MapPut("/{id:guid}", UpdateListAsync).Produces<ListDto>();
        lists.MapDelete("/{id:guid}", DeleteListAsync).Produces(StatusCodes.Status204NoContent);
        lists.MapPost("/{id:guid}/requirements", AddRequirementAsync).Produces<ListDto>();
        lists.MapPut("/{id:guid}/requirements/{requirementId:guid}", UpdateRequirementAsync).Produces<ListDto>();
        lists.MapDelete("/{id:guid}/requirements/{requirementId:guid}", DeleteRequirementAsync).Produces(StatusCodes.Status204NoContent);
        lists.MapGet("/{id:guid}/history", GetHistoryAsync).Produces<HistoryDto>();
        lists.MapGet("/{id:guid}/current", GetCurrentAsync).Produces<CurrentDto>();
    }

    private static async Task<IResult> GetListsAsync(HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var lists = await db.ShoppingLists.AsNoTracking()
            .Where(l => l.OwnerUserId == userId)
            .OrderByDescending(l => l.UpdatedAt)
            .Select(l => new ListSummaryDto(l.Id, l.Name, l.MarketCode, l.Requirements.Count, l.UpdatedAt))
            .ToListAsync(ct);
        return Results.Ok(lists);
    }

    private static async Task<IResult> CreateListAsync(CreateListRequest req, HttpContext http, AppDbContext db, ListPricingQuery pricing, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        if (Validation.Text("name", req.Name) is { } nameError)
            return nameError;
        if (!await db.Markets.AnyAsync(m => m.Code == req.MarketCode, ct))
            return Validation.Problem("marketCode", "Unknown market.");
        if (await ValidateShopsAsync(db, req.MarketCode, req.AllowedShopIds ?? [], ct) is { } shopError)
            return shopError;
        if (await db.ShoppingLists.CountAsync(l => l.OwnerUserId == userId, ct) >= MaxListsPerUser)
            return Validation.Problem("name", $"At most {MaxListsPerUser} lists.");

        var list = new ShoppingList
        {
            OwnerUserId = userId, MarketCode = req.MarketCode, Name = req.Name.Trim(), AllowedShopIds = req.AllowedShopIds ?? [],
        };
        db.ShoppingLists.Add(list);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/lists/{list.Id}", await ToDtoAsync(list, pricing, ct));
    }

    private static async Task<IResult> GetListAsync(Guid id, HttpContext http, AppDbContext db, ListPricingQuery pricing, CancellationToken ct)
    {
        var list = await LoadAsync(db, http, id, ct);
        return list is null ? Results.NotFound() : Results.Ok(await ToDtoAsync(list, pricing, ct));
    }

    private static async Task<IResult> UpdateListAsync(Guid id, UpdateListRequest req, HttpContext http, AppDbContext db, ListPricingQuery pricing, CancellationToken ct)
    {
        var list = await LoadAsync(db, http, id, ct);
        if (list is null)
            return Results.NotFound();
        if (Validation.Text("name", req.Name) is { } nameError)
            return nameError;
        if (await ValidateShopsAsync(db, list.MarketCode, req.AllowedShopIds, ct) is { } shopError)
            return shopError;

        list.Name = req.Name.Trim();
        list.AllowedShopIds = req.AllowedShopIds;
        list.Touch();
        await db.SaveChangesAsync(ct);
        return Results.Ok(await ToDtoAsync(list, pricing, ct));
    }

    private static async Task<IResult> DeleteListAsync(Guid id, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var userId = http.User.GetUserId();
        var deleted = await db.ShoppingLists.Where(l => l.Id == id && l.OwnerUserId == userId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> AddRequirementAsync(
        Guid id, RequirementRequest req, HttpContext http, AppDbContext db, MatchRebuilder matcher, ListPricingQuery pricing, CancellationToken ct)
    {
        var list = await LoadAsync(db, http, id, ct);
        if (list is null)
            return Results.NotFound();
        if (list.Requirements.Count >= MaxRequirementsPerList)
            return Validation.Problem("title", $"At most {MaxRequirementsPerList} items per list.");
        if (await ValidateRequirementAsync(db, req, ct) is { } error)
            return error;

        var requirement = new Requirement
        {
            ListId = list.Id, Title = req.Title.Trim(), CategoryId = req.CategoryId, Quantity = req.Quantity,
            Constraints = req.Constraints ?? [], PinnedProductId = req.PinnedProductId,
        };
        db.Requirements.Add(requirement);
        list.Touch();
        await db.SaveChangesAsync(ct);
        await matcher.RebuildAsync(requirement, ct);

        return Results.Ok(await ToDtoAsync((await LoadAsync(db, http, id, ct))!, pricing, ct));
    }

    private static async Task<IResult> UpdateRequirementAsync(
        Guid id, Guid requirementId, RequirementRequest req, HttpContext http, AppDbContext db, MatchRebuilder matcher, ListPricingQuery pricing, CancellationToken ct)
    {
        var list = await LoadAsync(db, http, id, ct);
        var requirement = list?.Requirements.SingleOrDefault(r => r.Id == requirementId);
        if (list is null || requirement is null)
            return Results.NotFound();
        if (await ValidateRequirementAsync(db, req, ct) is { } error)
            return error;

        requirement.Title = req.Title.Trim();
        requirement.CategoryId = req.CategoryId;
        requirement.Quantity = req.Quantity;
        requirement.Constraints = req.Constraints ?? [];
        requirement.PinnedProductId = req.PinnedProductId;
        list.Touch();
        await db.SaveChangesAsync(ct);
        await matcher.RebuildAsync(requirement, ct);

        return Results.Ok(await ToDtoAsync((await LoadAsync(db, http, id, ct))!, pricing, ct));
    }

    private static async Task<IResult> DeleteRequirementAsync(Guid id, Guid requirementId, HttpContext http, AppDbContext db, CancellationToken ct)
    {
        var list = await LoadAsync(db, http, id, ct);
        var requirement = list?.Requirements.SingleOrDefault(r => r.Id == requirementId);
        if (list is null || requirement is null)
            return Results.NotFound();

        db.Requirements.Remove(requirement);
        list.Touch();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetHistoryAsync(
        Guid id, DateOnly? from, DateOnly? to, HttpContext http, AppDbContext db, ListPricingQuery pricing, TimeProvider time, CancellationToken ct)
    {
        var list = await LoadAsync(db, http, id, ct, tracking: false);
        if (list is null)
            return Results.NotFound();

        var end = to ?? DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var start = from ?? end.AddDays(-89);
        if (start > end || end.DayNumber - start.DayNumber > MaxHistoryDays)
            return Validation.Problem("from", $"Range must be 1–{MaxHistoryDays} days.");

        // Revalidate every time, but answer 304 until prices, the list, or the user's discounts change.
        var discounts = await DiscountVersionAsync(db, list.OwnerUserId, ct);
        if (HttpCaching.IsNotModified(http, PrivateRevalidate, "history", list.Id, list.UpdatedAt, discounts, start, end,
                await pricing.GetLastCollectedAtAsync(ct)))
            return HttpCaching.NotModified();

        var currency = await CurrencyAsync(db, list.MarketCode, ct);
        var points = await pricing.GetHistoryAsync(list, start, end, ct);
        return Results.Ok(new HistoryDto(currency, start, end, points
            .Select(p => new HistoryPointDto(p.Day, p.Total, p.Complete,
                p.Items.Select(i => new HistoryItemDto(i.RequirementId, i.LinePrice, i.ShopId)).ToList()))
            .ToList()));
    }

    private static async Task<IResult> GetCurrentAsync(
        Guid id, HttpContext http, AppDbContext db, ListPricingQuery pricing, TimeProvider time, CancellationToken ct)
    {
        var list = await LoadAsync(db, http, id, ct, tracking: false);
        if (list is null)
            return Results.NotFound();

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var discounts = await DiscountVersionAsync(db, list.OwnerUserId, ct);
        if (HttpCaching.IsNotModified(http, PrivateRevalidate, "current", list.Id, list.UpdatedAt, discounts, today,
                await pricing.GetLastCollectedAtAsync(ct)))
            return HttpCaching.NotModified();

        var best = await pricing.GetCurrentAsync(list, today, ct);
        var items = list.Requirements.Select(r => new CurrentItemDto(r.Id, best.GetValueOrDefault(r.Id))).ToList();
        return Results.Ok(new CurrentDto(
            await CurrencyAsync(db, list.MarketCode, ct),
            items.Sum(i => i.Best?.LinePrice ?? 0),
            items.Count > 0 && items.All(i => i.Best is not null),
            items));
    }

    private static Task<ShoppingList?> LoadAsync(AppDbContext db, HttpContext http, Guid id, CancellationToken ct, bool tracking = true)
    {
        var userId = http.User.GetUserId();
        var query = db.ShoppingLists.Include(l => l.Requirements).Where(l => l.Id == id && l.OwnerUserId == userId);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
    }

    private static async Task<ListDto> ToDtoAsync(ShoppingList list, ListPricingQuery pricing, CancellationToken ct)
    {
        var counts = await pricing.GetMatchCountsAsync(list.Requirements.Select(r => r.Id), ct);
        return new ListDto(list.Id, list.Name, list.MarketCode, list.AllowedShopIds,
            list.Requirements
                .OrderBy(r => r.Id) // v7 GUIDs sort by creation time
                .Select(r => new RequirementDto(r.Id, r.Title, r.CategoryId, r.Quantity, r.Constraints, r.PinnedProductId, counts.GetValueOrDefault(r.Id)))
                .ToList(),
            list.UpdatedAt);
    }

    private static async Task<IResult?> ValidateRequirementAsync(AppDbContext db, RequirementRequest req, CancellationToken ct)
    {
        if (Validation.Text("title", req.Title) is { } titleError)
            return titleError;
        if (req.Quantity is < 1 or > 100)
            return Validation.Problem("quantity", "Quantity must be 1–100.");
        var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(c => c.Id == req.CategoryId, ct);
        if (category is null)
            return Validation.Problem("categoryId", "Unknown category.");
        if (category.Attributes.Count == 0)
            return Validation.Problem("categoryId", "Pick a specific category, not a group.");
        if (req.PinnedProductId is { } pinned && !await db.Products.AnyAsync(p => p.Id == pinned && p.CategoryId == category.Id, ct))
            return Validation.Problem("pinnedProductId", "Product not found in this category.");
        return Validation.Constraints(req.Constraints ?? [], category);
    }

    private static async Task<IResult?> ValidateShopsAsync(AppDbContext db, string market, List<Guid> shopIds, CancellationToken ct)
    {
        if (shopIds.Count == 0)
            return null;
        var known = await db.Shops.CountAsync(s => s.MarketCode == market && shopIds.Contains(s.Id), ct);
        return known == shopIds.Distinct().Count() ? null : Validation.Problem("allowedShopIds", "Unknown shop for this market.");
    }

    private static async Task<string> DiscountVersionAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var q = db.UserShopDiscounts.Where(d => d.UserId == userId);
        return $"{await q.CountAsync(ct)}:{(await q.MaxAsync(d => (DateTimeOffset?)d.UpdatedAt, ct))?.UtcTicks}";
    }

    private static Task<string> CurrencyAsync(AppDbContext db, string market, CancellationToken ct) =>
        db.Markets.Where(m => m.Code == market).Select(m => m.Currency).SingleAsync(ct);
}
