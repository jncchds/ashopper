namespace Ashopper.Domain.Catalog;

/// <summary>
/// A product category (tree). Its attribute definitions describe which specs products in it have,
/// and drive the requirement editor in the UI.
/// </summary>
public sealed class Category
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid? ParentId { get; set; }
    /// <summary>Stable identifier, e.g. "pc-ram". Price sources map their own categories to it.</summary>
    public required string Slug { get; init; }
    public required string Name { get; set; }
    public List<AttributeDefinition> Attributes { get; set; } = [];
}

public enum AttributeType
{
    Number,
    Enum,
    Bool,
    Text,
}

/// <param name="Key">Canonical key, e.g. "capacity_gb". Product attributes use these keys.</param>
/// <param name="AllowedValues">For <see cref="AttributeType.Enum"/>: the known values.</param>
public sealed record AttributeDefinition(
    string Key,
    string Label,
    AttributeType Type,
    string? Unit = null,
    IReadOnlyList<string>? AllowedValues = null);
