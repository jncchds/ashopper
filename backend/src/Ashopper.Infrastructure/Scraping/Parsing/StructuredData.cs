using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ashopper.Infrastructure.Scraping.Parsing;

/// <summary>What a product page says about the product and its price. Every field is optional.</summary>
public sealed record ScrapedProduct(
    string? Name,
    string? Brand,
    string? Sku,
    string? Mpn,
    string? Ean,
    decimal? Price,
    string? Currency,
    bool? InStock,
    string? Seller);

/// <summary>
/// Reads schema.org product data that most shops embed for search engines: JSON-LD first (anywhere in the tree, so
/// <c>@graph</c> and <c>BuyAction.object</c> work too), then OpenGraph/product meta tags as a fallback for the price.
/// </summary>
public static partial class StructuredData
{
    public static ScrapedProduct? Extract(string html)
    {
        var fromJsonLd = JsonLdBlocks(html)
            .SelectMany(FindProducts)
            .Select(ReadProduct)
            .OrderByDescending(p => p.Price is not null) // a Product with a price beats e.g. a related-product stub
            .FirstOrDefault();
        var meta = ReadMeta(html);

        if (fromJsonLd is null)
            return meta.Price is null ? null : meta;

        return fromJsonLd with
        {
            Name = fromJsonLd.Name ?? meta.Name,
            Price = fromJsonLd.Price ?? meta.Price,
            Currency = fromJsonLd.Currency ?? meta.Currency,
            InStock = fromJsonLd.InStock ?? meta.InStock,
        };
    }

    private static IEnumerable<JsonElement> JsonLdBlocks(string html)
    {
        foreach (Match m in JsonLdScript().Matches(html))
        {
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(m.Groups[1].Value.Trim(), new JsonDocumentOptions
                {
                    AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip,
                });
            }
            catch (JsonException)
            {
                continue; // shops do ship broken JSON-LD now and then; skip that block
            }
            using (doc)
                yield return doc.RootElement.Clone();
        }
    }

    /// <summary>Every object whose @type is (or includes) Product, at any depth.</summary>
    private static IEnumerable<JsonElement> FindProducts(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
                foreach (var p in FindProducts(item))
                    yield return p;
            yield break;
        }
        if (e.ValueKind != JsonValueKind.Object)
            yield break;

        if (HasType(e, "Product") || HasType(e, "ProductGroup"))
        {
            yield return e;
            yield break;
        }
        foreach (var prop in e.EnumerateObject())
            foreach (var p in FindProducts(prop.Value))
                yield return p;
    }

    private static bool HasType(JsonElement e, string type)
    {
        if (!e.TryGetProperty("@type", out var t))
            return false;
        return t.ValueKind switch
        {
            JsonValueKind.String => IsType(t.GetString(), type),
            JsonValueKind.Array => t.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && IsType(x.GetString(), type)),
            _ => false,
        };
    }

    // "Product", "schema:Product" and "http://schema.org/Product" all count.
    private static bool IsType(string? value, string type) =>
        value is not null && (value == type || value.EndsWith("/" + type, StringComparison.Ordinal) || value.EndsWith(":" + type, StringComparison.Ordinal));

    private static ScrapedProduct ReadProduct(JsonElement p)
    {
        var offer = BestOffer(p);
        // ProductGroup (variants): take the first variant's offer if the group has none.
        if (offer is null && p.TryGetProperty("hasVariant", out var variants))
            offer = FindProducts(variants).Select(BestOffer).FirstOrDefault(o => o is not null);

        return new ScrapedProduct(
            Name: Text(p, "name"),
            Brand: p.TryGetProperty("brand", out var brand) ? (brand.ValueKind == JsonValueKind.String ? Clean(brand.GetString()) : Text(brand, "name")) : null,
            Sku: Text(p, "sku") ?? Text(p, "productID"),
            Mpn: Text(p, "mpn"),
            Ean: Gtin(p),
            Price: offer?.Price,
            Currency: offer?.Currency,
            InStock: offer?.InStock,
            Seller: offer?.Seller);
    }

    private sealed record OfferInfo(decimal? Price, string? Currency, bool? InStock, string? Seller);

    /// <summary>Of all offers on the product, the cheapest in-stock one (or the cheapest at all).</summary>
    private static OfferInfo? BestOffer(JsonElement product)
    {
        if (!product.TryGetProperty("offers", out var offers))
            return null;
        var all = (offers.ValueKind == JsonValueKind.Array ? offers.EnumerateArray().ToList() : [offers])
            .Where(o => o.ValueKind == JsonValueKind.Object)
            .SelectMany(ExpandAggregate)
            .Select(ReadOffer)
            .Where(o => o.Price is not null)
            .ToList();
        return all
            .OrderByDescending(o => o.InStock != false)
            .ThenBy(o => o.Price)
            .FirstOrDefault();
    }

    /// <summary>An AggregateOffer may list its offers, or just a lowPrice.</summary>
    private static IEnumerable<JsonElement> ExpandAggregate(JsonElement offer)
    {
        if (HasType(offer, "AggregateOffer") && offer.TryGetProperty("offers", out var inner))
        {
            var list = inner.ValueKind == JsonValueKind.Array ? inner.EnumerateArray().ToList() : [inner];
            if (list.Count > 0)
                return list.Where(o => o.ValueKind == JsonValueKind.Object);
        }
        return [offer];
    }

    private static OfferInfo ReadOffer(JsonElement o)
    {
        var price = Number(o, "price") ?? Number(o, "lowPrice");
        string? currency = Text(o, "priceCurrency");
        if (price is null && o.TryGetProperty("priceSpecification", out var spec))
        {
            var specs = spec.ValueKind == JsonValueKind.Array ? spec.EnumerateArray().ToList() : [spec];
            var first = specs.FirstOrDefault(s => s.ValueKind == JsonValueKind.Object && Number(s, "price") is not null);
            if (first.ValueKind == JsonValueKind.Object)
            {
                price = Number(first, "price");
                currency ??= Text(first, "priceCurrency");
            }
        }
        var seller = o.TryGetProperty("seller", out var s2) && s2.ValueKind == JsonValueKind.Object ? Text(s2, "name") : null;
        return new OfferInfo(price, currency?.ToUpperInvariant(), Availability(Text(o, "availability")), seller);
    }

    /// <summary>schema.org ItemAvailability → in stock? Null when the page doesn't say.</summary>
    public static bool? Availability(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var name = value[(value.LastIndexOfAny(['/', ':']) + 1)..].Trim();
        return name.ToLowerInvariant() switch
        {
            "instock" or "limitedavailability" or "onlineonly" or "instoreonly" or "presale" => true,
            "outofstock" or "soldout" or "discontinued" or "preorder" or "backorder" or "madetoorder" or "reserved" => false,
            "in stock" => true,
            "out of stock" or "oos" => false,
            _ => null,
        };
    }

    private static string? Gtin(JsonElement p)
    {
        foreach (var key in new[] { "gtin13", "gtin", "gtin14", "gtin12", "gtin8", "ean" })
        {
            var digits = Text(p, key) is { } raw ? new string(raw.Where(char.IsAsciiDigit).ToArray()) : null;
            if (digits is { Length: 8 or 12 or 13 or 14 })
                return digits.Length == 14 && digits[0] == '0' ? digits[1..] : digits;
        }
        return null;
    }

    private static string? Text(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => Clean(v.GetString()),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.Array => v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? Clean(x.GetString()) : null).FirstOrDefault(x => x is not null),
            _ => null,
        };
    }

    private static decimal? Number(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
            return d > 0 ? d : null;
        return v.ValueKind == JsonValueKind.String ? PriceText.Parse(v.GetString()) : null;
    }

    private static string? Clean(string? s)
    {
        if (s is null)
            return null;
        s = WebUtility.HtmlDecode(s).Trim();
        return s.Length == 0 ? null : s;
    }

    private static ScrapedProduct ReadMeta(string html)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in MetaTag().Matches(html))
        {
            var attrs = Attributes(m.Value);
            var key = attrs.GetValueOrDefault("property") ?? attrs.GetValueOrDefault("name") ?? attrs.GetValueOrDefault("itemprop");
            if (key is not null && attrs.TryGetValue("content", out var content))
                tags.TryAdd(key, WebUtility.HtmlDecode(content).Trim());
        }

        var price = PriceText.Parse(tags.GetValueOrDefault("product:price:amount") ?? tags.GetValueOrDefault("og:price:amount") ?? tags.GetValueOrDefault("price"));
        var currency = tags.GetValueOrDefault("product:price:currency") ?? tags.GetValueOrDefault("og:price:currency") ?? tags.GetValueOrDefault("priceCurrency");
        return new ScrapedProduct(
            Name: tags.GetValueOrDefault("og:title"),
            Brand: tags.GetValueOrDefault("product:brand"),
            Sku: null, Mpn: null, Ean: null,
            Price: price,
            Currency: currency?.ToUpperInvariant(),
            InStock: Availability(tags.GetValueOrDefault("product:availability") ?? tags.GetValueOrDefault("og:availability")),
            Seller: null);
    }

    private static Dictionary<string, string> Attributes(string tag)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match a in HtmlAttribute().Matches(tag))
            result.TryAdd(a.Groups["name"].Value, a.Groups["dq"].Success ? a.Groups["dq"].Value : a.Groups["sq"].Value);
        return result;
    }

    [GeneratedRegex("""<script\b[^>]*\btype\s*=\s*["']?application/ld\+json["']?[^>]*>(.*?)</script\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex JsonLdScript();

    [GeneratedRegex("""<meta\b[^>]*>""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTag();

    [GeneratedRegex("""(?<name>[a-zA-Z:_-]+)\s*=\s*(?:"(?<dq>[^"]*)"|'(?<sq>[^']*)')""")]
    private static partial Regex HtmlAttribute();
}

/// <summary>Turns shop price text ("4699.00", "1 299,99 zł", "1.299,99") into a number.</summary>
public static class PriceText
{
    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var s = new string(text.Where(c => char.IsAsciiDigit(c) || c is '.' or ',').ToArray());
        if (s.Length == 0)
            return null;

        int lastDot = s.LastIndexOf('.'), lastComma = s.LastIndexOf(',');
        if (lastDot >= 0 && lastComma >= 0)
        {
            // Both present: whichever comes last is the decimal separator.
            var decimalSep = lastDot > lastComma ? '.' : ',';
            var groupSep = decimalSep == '.' ? ',' : '.';
            s = s.Replace(groupSep.ToString(), "").Replace(decimalSep, '.');
        }
        else if (lastComma >= 0)
        {
            // Only commas: "1,299" with exactly three digits after is a thousands group; "1299,9" / "1299,99" is decimal.
            var after = s.Length - lastComma - 1;
            s = after is 1 or 2 && s.Count(c => c == ',') == 1 ? s.Replace(',', '.') : s.Replace(",", "");
        }
        else if (s.Count(c => c == '.') > 1)
        {
            s = s.Replace(".", ""); // "1.299.000"
        }

        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) && d > 0 ? d : null;
    }
}
