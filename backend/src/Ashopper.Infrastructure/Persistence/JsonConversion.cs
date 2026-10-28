using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ashopper.Infrastructure.Persistence;

public static class AshopperJson
{
    /// <summary>Shared options: camelCase, enums as strings. Used for JSON columns and the API.</summary>
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        // Numbers are numbers: keeps the OpenAPI contract (and generated TS types) "number", not "number | string".
        options.NumberHandling = JsonNumberHandling.Strict;
        return options;
    }
}

internal static class JsonConversionExtensions
{
    /// <summary>
    /// Stores the property as a JSON string column. Change tracking compares the serialized form,
    /// so mutating a list/dictionary in place is detected.
    /// </summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> property) where T : class
    {
        var comparer = new ValueComparer<T>(
            (a, b) => Serialize(a) == Serialize(b),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<T>(Serialize(v), AshopperJson.Options)!);

        property.HasConversion(
                v => Serialize(v),
                s => JsonSerializer.Deserialize<T>(s, AshopperJson.Options)!,
                comparer)
            .HasColumnType("nvarchar(max)");
        return property;
    }

    private static string Serialize<T>(T? value) => JsonSerializer.Serialize(value, AshopperJson.Options);
}
