using Ashopper.Api.Auth;
using Ashopper.Domain.Lists;
using Ashopper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ashopper.Api.Endpoints;

public sealed record ShopDiscountDto(Guid ShopId, decimal Percent);
public sealed record SetDiscountRequest(decimal Percent);

public static class DiscountEndpoints
{
    public static void MapDiscountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/shop-discounts").WithTags("Discounts").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.GetUserId();
            return Results.Ok(await db.UserShopDiscounts.AsNoTracking().Where(d => d.UserId == userId)
                .Select(d => new ShopDiscountDto(d.ShopId, d.Percent)).ToListAsync(ct));
        });

        group.MapPut("/{shopId:guid}", async (Guid shopId, SetDiscountRequest req, HttpContext http, AppDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (req.Percent is <= 0 or > 90)
                return Validation.Problem("percent", "Discount must be between 0 and 90%.");
            if (!await db.Shops.AnyAsync(s => s.Id == shopId, ct))
                return Results.NotFound();

            var userId = http.User.GetUserId();
            var discount = await db.UserShopDiscounts.FindAsync([userId, shopId], ct);
            if (discount is null)
            {
                discount = new UserShopDiscount { UserId = userId, ShopId = shopId };
                db.UserShopDiscounts.Add(discount);
            }
            discount.Percent = Math.Round(req.Percent, 2);
            discount.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new ShopDiscountDto(shopId, discount.Percent));
        });

        group.MapDelete("/{shopId:guid}", async (Guid shopId, HttpContext http, AppDbContext db, CancellationToken ct) =>
        {
            var userId = http.User.GetUserId();
            await db.UserShopDiscounts.Where(d => d.UserId == userId && d.ShopId == shopId).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        });
    }
}
