using Ashopper.Domain.Matching;

namespace Ashopper.Domain.Lists;

/// <summary>A set of things to buy: a PC build, a flat's appliances...</summary>
public sealed class ShoppingList
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid OwnerUserId { get; init; }
    public required string MarketCode { get; init; }
    public required string Name { get; set; }
    /// <summary>Shops whose offers count. Empty = all shops of the market.</summary>
    public List<Guid> AllowedShopIds { get; set; } = [];
    public List<Requirement> Requirements { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>Bumped on any change to the list or its requirements; part of the history ETag.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}

/// <summary>"Any product in this category that satisfies these constraints", or one pinned product.</summary>
public sealed class Requirement
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid ListId { get; init; }
    public Guid CategoryId { get; set; }
    public required string Title { get; set; }
    public int Quantity { get; set; } = 1;
    public List<SpecConstraint> Constraints { get; set; } = [];
    public Guid? PinnedProductId { get; set; }
}

/// <summary>Precomputed requirement → product match, so price queries are plain joins.</summary>
public sealed class RequirementMatch
{
    public Guid RequirementId { get; init; }
    public Guid ProductId { get; init; }
}

/// <summary>A user's personal discount at a shop, applied to every price from it.</summary>
public sealed class UserShopDiscount
{
    public Guid UserId { get; init; }
    public Guid ShopId { get; init; }
    public decimal Percent { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
