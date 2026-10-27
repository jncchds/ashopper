namespace Ashopper.Domain.Pricing;

public sealed class Shop
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string MarketCode { get; init; }
    public required string Name { get; set; }
    /// <summary>Host name, e.g. "example-shop.pl". Unique per market; sources identify shops by it.</summary>
    public required string Domain { get; init; }
}

/// <summary>A product sold by a shop, as reported by one price source.</summary>
public sealed class Offer
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid ProductId { get; set; }
    public Guid ShopId { get; set; }
    public required string SourceKey { get; init; }
    public required string ExternalId { get; init; }
    public required string Url { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastSeenAt { get; set; }
}

/// <summary>One price reading. Append-only.</summary>
public sealed class PriceObservation
{
    public long Id { get; init; }
    public Guid OfferId { get; init; }
    public DateTimeOffset ObservedAt { get; init; }
    public decimal Price { get; init; }
    public required string Currency { get; init; }
    public bool InStock { get; init; }
}

/// <summary>Lowest price per offer per day, kept up to date by the collector. Charts read only this.</summary>
public sealed class DailyPrice
{
    public Guid OfferId { get; init; }
    public DateOnly Day { get; init; }
    public decimal MinPrice { get; set; }
    public required string Currency { get; set; }
    public bool InStock { get; set; }

    /// <summary>Folds another observation of the same day in.</summary>
    public void Apply(decimal price, bool inStock)
    {
        // An in-stock price beats an out-of-stock one; otherwise the lower price wins.
        if (inStock && !InStock)
        {
            MinPrice = price;
            InStock = true;
        }
        else if (inStock == InStock && price < MinPrice)
        {
            MinPrice = price;
        }
    }
}
