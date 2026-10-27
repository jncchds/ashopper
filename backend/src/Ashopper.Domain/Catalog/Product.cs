namespace Ashopper.Domain.Catalog;

/// <summary>
/// A canonical product, independent of shops and sources. Offers from different sources are merged
/// onto the same product by EAN/MPN or brand+model.
/// </summary>
public sealed class Product
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid CategoryId { get; set; }
    public required string Brand { get; set; }
    public required string Model { get; set; }
    public required string Name { get; set; }
    public string? Ean { get; set; }
    public string? Mpn { get; set; }

    /// <summary>
    /// Specs keyed by <see cref="AttributeDefinition.Key"/>. Values are invariant-culture strings
    /// ("32", "true", "DDR5"), so numbers and enums compare the same way everywhere.
    /// </summary>
    public Dictionary<string, string> Attributes { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Links a product to its id in a price source, so repeated discovery finds the same product.</summary>
public sealed class ProductSourceRef
{
    public required string SourceKey { get; init; }
    public required string ExternalId { get; init; }
    public Guid ProductId { get; set; }
}
