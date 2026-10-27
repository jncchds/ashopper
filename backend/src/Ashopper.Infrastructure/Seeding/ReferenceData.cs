using Ashopper.Domain.Catalog;
using Ashopper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static Ashopper.Domain.Catalog.AttributeType;

namespace Ashopper.Infrastructure.Seeding;

/// <summary>
/// Markets and the category tree with attribute definitions. This is code, not data entry:
/// changing a definition here and re-running the seeder updates the database.
/// </summary>
public static class ReferenceData
{
    public static readonly Market[] Markets =
    [
        new() { Code = "PL", Name = "Polska", Currency = "PLN", Locale = "pl-PL" },
        new() { Code = "UA", Name = "Україна", Currency = "UAH", Locale = "uk-UA" },
    ];

    public sealed record CategorySeed(string Slug, string Name, string? ParentSlug, AttributeDefinition[] Attributes);

    private static readonly string[] EnergyClasses = ["A", "B", "C", "D", "E", "F", "G"];

    public static readonly CategorySeed[] Categories =
    [
        new("electronics", "Electronics", null, []),
        new("pc-components", "PC components", "electronics", []),
        new("pc-cpu", "Processors", "pc-components",
        [
            new("socket", "Socket", AttributeType.Enum, AllowedValues: ["AM4", "AM5", "LGA1700", "LGA1851"]),
            new("cores", "Cores", Number),
            new("threads", "Threads", Number),
            new("boost_ghz", "Boost clock", Number, "GHz"),
            new("tdp_w", "TDP", Number, "W"),
            new("integrated_gpu", "Integrated graphics", Bool),
        ]),
        new("pc-motherboard", "Motherboards", "pc-components",
        [
            new("socket", "Socket", AttributeType.Enum, AllowedValues: ["AM4", "AM5", "LGA1700", "LGA1851"]),
            new("chipset", "Chipset", Text),
            new("form_factor", "Form factor", AttributeType.Enum, AllowedValues: ["ATX", "mATX", "ITX"]),
            new("memory_type", "Memory type", AttributeType.Enum, AllowedValues: ["DDR4", "DDR5"]),
            new("wifi", "Wi-Fi", Bool),
            new("m2_slots", "M.2 slots", Number),
        ]),
        new("pc-ram", "Memory (RAM)", "pc-components",
        [
            new("memory_type", "Type", AttributeType.Enum, AllowedValues: ["DDR4", "DDR5"]),
            new("capacity_gb", "Total capacity", Number, "GB"),
            new("modules", "Modules", Number),
            new("speed_mts", "Speed", Number, "MT/s"),
            new("cas_latency", "CAS latency", Number, "CL"),
            new("rgb", "RGB", Bool),
        ]),
        new("pc-gpu", "Graphics cards", "pc-components",
        [
            new("gpu_vendor", "GPU vendor", AttributeType.Enum, AllowedValues: ["NVIDIA", "AMD", "Intel"]),
            new("gpu_model", "GPU", Text),
            new("vram_gb", "Memory", Number, "GB"),
            new("length_mm", "Length", Number, "mm"),
            new("tdp_w", "Power", Number, "W"),
        ]),
        new("pc-ssd", "SSD drives", "pc-components",
        [
            new("interface", "Interface", AttributeType.Enum, AllowedValues: ["PCIe 3.0", "PCIe 4.0", "PCIe 5.0", "SATA"]),
            new("capacity_gb", "Capacity", Number, "GB"),
            new("read_mbs", "Sequential read", Number, "MB/s"),
            new("form_factor", "Form factor", AttributeType.Enum, AllowedValues: ["M.2 2280", "2.5\""]),
            new("dram_cache", "DRAM cache", Bool),
        ]),
        new("pc-psu", "Power supplies", "pc-components",
        [
            new("wattage", "Power", Number, "W"),
            new("efficiency", "Efficiency", AttributeType.Enum, AllowedValues: ["80+ Bronze", "80+ Gold", "80+ Platinum", "80+ Titanium"]),
            new("modular", "Modular", AttributeType.Enum, AllowedValues: ["Full", "Semi", "Non"]),
            new("atx_version", "ATX version", AttributeType.Enum, AllowedValues: ["2.x", "3.0", "3.1"]),
        ]),
        new("home", "Home", null, []),
        new("home-appliances", "Appliances", "home", []),
        new("home-fridge", "Fridges", "home-appliances",
        [
            new("capacity_l", "Capacity", Number, "l"),
            new("no_frost", "No Frost", Bool),
            new("energy_class", "Energy class", AttributeType.Enum, AllowedValues: EnergyClasses),
            new("height_cm", "Height", Number, "cm"),
            new("installation", "Installation", AttributeType.Enum, AllowedValues: ["Freestanding", "Built-in"]),
            new("color", "Color", Text),
        ]),
        new("home-washer", "Washing machines", "home-appliances",
        [
            new("capacity_kg", "Load", Number, "kg"),
            new("spin_rpm", "Spin speed", Number, "rpm"),
            new("energy_class", "Energy class", AttributeType.Enum, AllowedValues: EnergyClasses),
            new("depth_cm", "Depth", Number, "cm"),
            new("steam", "Steam", Bool),
        ]),
    ];

    /// <summary>Inserts or updates markets and categories. Idempotent; safe to run on every deploy.</summary>
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var markets = await db.Markets.ToDictionaryAsync(m => m.Code, ct);
        foreach (var m in Markets)
        {
            if (markets.TryGetValue(m.Code, out var existing))
            {
                existing.Name = m.Name;
                existing.Currency = m.Currency;
                existing.Locale = m.Locale;
            }
            else
            {
                db.Markets.Add(new Market { Code = m.Code, Name = m.Name, Currency = m.Currency, Locale = m.Locale });
            }
        }

        var categories = await db.Categories.ToDictionaryAsync(c => c.Slug, ct);
        foreach (var seed in Categories) // parents come before children
        {
            if (!categories.TryGetValue(seed.Slug, out var category))
            {
                category = new Category { Slug = seed.Slug, Name = seed.Name };
                db.Categories.Add(category);
                categories[seed.Slug] = category;
            }
            category.Name = seed.Name;
            category.ParentId = seed.ParentSlug is null ? null : categories[seed.ParentSlug].Id;
            category.Attributes = [.. seed.Attributes];
        }

        await db.SaveChangesAsync(ct);
    }
}
