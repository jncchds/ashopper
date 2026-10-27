namespace Ashopper.Domain.Catalog;

/// <summary>A country market. Adding a country = a new row + price sources that support it.</summary>
public sealed class Market
{
    /// <summary>ISO 3166-1 alpha-2 code, e.g. "PL", "UA".</summary>
    public required string Code { get; init; }
    public required string Name { get; set; }
    /// <summary>ISO 4217 currency code, e.g. "PLN".</summary>
    public required string Currency { get; set; }
    /// <summary>BCP 47 locale used for formatting, e.g. "pl-PL".</summary>
    public required string Locale { get; set; }
}
