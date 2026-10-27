namespace Ashopper.Infrastructure.Sources.Fake;

/// <summary>
/// Demo products for the fake source. Models are real-ish so the UI looks familiar; prices and shops are made up.
/// </summary>
internal static class FakeCatalog
{
    public sealed record Item(string Id, string Brand, string Model, decimal BasePricePln, string? Ean, Dictionary<string, string> Attributes);

    /// <summary>Fictional shops (".example" domains) per market, with a price factor.</summary>
    public static readonly Dictionary<string, (string Name, string Domain, decimal Factor)[]> Shops = new()
    {
        ["PL"] =
        [
            ("TechMarket", "techmarket.example", 1.00m),
            ("PixelHub", "pixelhub.example", 0.97m),
            ("MegaKomp", "megakomp.example", 1.03m),
            ("AGD Centrum", "agdcentrum.example", 1.01m),
            ("Okazja24", "okazja24.example", 0.95m),
        ],
        ["UA"] =
        [
            ("Техносвіт", "tekhnosvit.example", 1.00m),
            ("КомпМаркет", "kompmarket.example", 1.04m),
            ("Дім і Техніка", "dimtekhnika.example", 0.98m),
        ],
    };

    /// <summary>Rough PLN→market currency rate, only for making up prices.</summary>
    public static readonly Dictionary<string, (string Currency, decimal Rate)> Currencies = new()
    {
        ["PL"] = ("PLN", 1m),
        ["UA"] = ("UAH", 10.5m),
    };

    private static Dictionary<string, string> A(params (string Key, object Value)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => p.Value switch
        {
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
            var v => v.ToString()!,
        });

    public static readonly Dictionary<string, Item[]> Items = new()
    {
        ["pc-cpu"] =
        [
            new("cpu-r5-7600", "AMD", "Ryzen 5 7600", 899m, null, A(("socket", "AM5"), ("cores", 6), ("threads", 12), ("boost_ghz", 5.1m), ("tdp_w", 65), ("integrated_gpu", true))),
            new("cpu-r7-7800x3d", "AMD", "Ryzen 7 7800X3D", 1799m, null, A(("socket", "AM5"), ("cores", 8), ("threads", 16), ("boost_ghz", 5.0m), ("tdp_w", 120), ("integrated_gpu", true))),
            new("cpu-r7-9700x", "AMD", "Ryzen 7 9700X", 1449m, null, A(("socket", "AM5"), ("cores", 8), ("threads", 16), ("boost_ghz", 5.5m), ("tdp_w", 65), ("integrated_gpu", true))),
            new("cpu-r9-9900x", "AMD", "Ryzen 9 9900X", 1899m, null, A(("socket", "AM5"), ("cores", 12), ("threads", 24), ("boost_ghz", 5.6m), ("tdp_w", 120), ("integrated_gpu", true))),
            new("cpu-i5-14600k", "Intel", "Core i5-14600K", 1149m, null, A(("socket", "LGA1700"), ("cores", 14), ("threads", 20), ("boost_ghz", 5.3m), ("tdp_w", 125), ("integrated_gpu", true))),
            new("cpu-u7-265k", "Intel", "Core Ultra 7 265K", 1549m, null, A(("socket", "LGA1851"), ("cores", 20), ("threads", 20), ("boost_ghz", 5.5m), ("tdp_w", 125), ("integrated_gpu", true))),
        ],
        ["pc-motherboard"] =
        [
            new("mb-msi-b650-tomahawk", "MSI", "MAG B650 Tomahawk WiFi", 899m, null, A(("socket", "AM5"), ("chipset", "B650"), ("form_factor", "ATX"), ("memory_type", "DDR5"), ("wifi", true), ("m2_slots", 3))),
            new("mb-asus-tuf-b650-plus", "ASUS", "TUF Gaming B650-Plus", 749m, null, A(("socket", "AM5"), ("chipset", "B650"), ("form_factor", "ATX"), ("memory_type", "DDR5"), ("wifi", false), ("m2_slots", 3))),
            new("mb-gb-b650m-elite-ax", "Gigabyte", "B650M Aorus Elite AX", 799m, null, A(("socket", "AM5"), ("chipset", "B650"), ("form_factor", "mATX"), ("memory_type", "DDR5"), ("wifi", true), ("m2_slots", 2))),
            new("mb-asrock-x870-pro", "ASRock", "X870 Pro RS WiFi", 1049m, null, A(("socket", "AM5"), ("chipset", "X870"), ("form_factor", "ATX"), ("memory_type", "DDR5"), ("wifi", true), ("m2_slots", 3))),
            new("mb-msi-z790-a", "MSI", "PRO Z790-A WiFi", 999m, null, A(("socket", "LGA1700"), ("chipset", "Z790"), ("form_factor", "ATX"), ("memory_type", "DDR5"), ("wifi", true), ("m2_slots", 4))),
            new("mb-asus-b860-i", "ASUS", "ROG Strix B860-I Gaming WiFi", 1199m, null, A(("socket", "LGA1851"), ("chipset", "B860"), ("form_factor", "ITX"), ("memory_type", "DDR5"), ("wifi", true), ("m2_slots", 2))),
        ],
        ["pc-ram"] =
        [
            new("ram-kfb-2x16-6000-30", "Kingston", "Fury Beast 2x16GB DDR5 6000 CL30", 449m, null, A(("memory_type", "DDR5"), ("capacity_gb", 32), ("modules", 2), ("speed_mts", 6000), ("cas_latency", 30), ("rgb", false))),
            new("ram-gskill-flarex5-2x16-6000-30", "G.Skill", "Flare X5 2x16GB DDR5 6000 CL30", 479m, null, A(("memory_type", "DDR5"), ("capacity_gb", 32), ("modules", 2), ("speed_mts", 6000), ("cas_latency", 30), ("rgb", false))),
            new("ram-corsair-veng-rgb-2x16-6000-30", "Corsair", "Vengeance RGB 2x16GB DDR5 6000 CL30", 529m, null, A(("memory_type", "DDR5"), ("capacity_gb", 32), ("modules", 2), ("speed_mts", 6000), ("cas_latency", 30), ("rgb", true))),
            new("ram-kfb-2x16-5600-36", "Kingston", "Fury Beast 2x16GB DDR5 5600 CL36", 389m, null, A(("memory_type", "DDR5"), ("capacity_gb", 32), ("modules", 2), ("speed_mts", 5600), ("cas_latency", 36), ("rgb", false))),
            new("ram-gskill-tz5-2x32-6000-30", "G.Skill", "Trident Z5 2x32GB DDR5 6000 CL30", 899m, null, A(("memory_type", "DDR5"), ("capacity_gb", 64), ("modules", 2), ("speed_mts", 6000), ("cas_latency", 30), ("rgb", true))),
            new("ram-corsair-lpx-2x16-3200-16", "Corsair", "Vengeance LPX 2x16GB DDR4 3200 CL16", 279m, null, A(("memory_type", "DDR4"), ("capacity_gb", 32), ("modules", 2), ("speed_mts", 3200), ("cas_latency", 16), ("rgb", false))),
        ],
        ["pc-gpu"] =
        [
            new("gpu-rtx5070-12", "Gigabyte", "GeForce RTX 5070 Windforce OC 12GB", 2699m, null, A(("gpu_vendor", "NVIDIA"), ("gpu_model", "RTX 5070"), ("vram_gb", 12), ("length_mm", 261), ("tdp_w", 250))),
            new("gpu-rtx5070ti-16", "MSI", "GeForce RTX 5070 Ti Ventus 3X 16GB", 3999m, null, A(("gpu_vendor", "NVIDIA"), ("gpu_model", "RTX 5070 Ti"), ("vram_gb", 16), ("length_mm", 303), ("tdp_w", 300))),
            new("gpu-rtx5060ti-16", "ASUS", "Dual GeForce RTX 5060 Ti OC 16GB", 2149m, null, A(("gpu_vendor", "NVIDIA"), ("gpu_model", "RTX 5060 Ti"), ("vram_gb", 16), ("length_mm", 229), ("tdp_w", 180))),
            new("gpu-rx9070xt-16", "Sapphire", "Pulse Radeon RX 9070 XT 16GB", 3149m, null, A(("gpu_vendor", "AMD"), ("gpu_model", "RX 9070 XT"), ("vram_gb", 16), ("length_mm", 320), ("tdp_w", 304))),
            new("gpu-rx9060xt-16", "PowerColor", "Reaper Radeon RX 9060 XT 16GB", 1799m, null, A(("gpu_vendor", "AMD"), ("gpu_model", "RX 9060 XT"), ("vram_gb", 16), ("length_mm", 199), ("tdp_w", 160))),
            new("gpu-arc-b580-12", "Intel", "Arc B580 Limited Edition 12GB", 1249m, null, A(("gpu_vendor", "Intel"), ("gpu_model", "Arc B580"), ("vram_gb", 12), ("length_mm", 272), ("tdp_w", 190))),
        ],
        ["pc-ssd"] =
        [
            new("ssd-990pro-2tb", "Samsung", "990 Pro 2TB", 749m, null, A(("interface", "PCIe 4.0"), ("capacity_gb", 2000), ("read_mbs", 7450), ("form_factor", "M.2 2280"), ("dram_cache", true))),
            new("ssd-sn850x-2tb", "WD", "Black SN850X 2TB", 699m, null, A(("interface", "PCIe 4.0"), ("capacity_gb", 2000), ("read_mbs", 7300), ("form_factor", "M.2 2280"), ("dram_cache", true))),
            new("ssd-kc3000-2tb", "Kingston", "KC3000 2TB", 649m, null, A(("interface", "PCIe 4.0"), ("capacity_gb", 2048), ("read_mbs", 7000), ("form_factor", "M.2 2280"), ("dram_cache", true))),
            new("ssd-t500-1tb", "Crucial", "T500 1TB", 359m, null, A(("interface", "PCIe 4.0"), ("capacity_gb", 1000), ("read_mbs", 7300), ("form_factor", "M.2 2280"), ("dram_cache", true))),
            new("ssd-nm790-4tb", "Lexar", "NM790 4TB", 1149m, null, A(("interface", "PCIe 4.0"), ("capacity_gb", 4000), ("read_mbs", 7400), ("form_factor", "M.2 2280"), ("dram_cache", false))),
            new("ssd-mx500-1tb", "Crucial", "MX500 1TB", 279m, null, A(("interface", "SATA"), ("capacity_gb", 1000), ("read_mbs", 560), ("form_factor", "2.5\""), ("dram_cache", true))),
        ],
        ["pc-psu"] =
        [
            new("psu-rm850e", "Corsair", "RM850e (2025)", 499m, null, A(("wattage", 850), ("efficiency", "80+ Gold"), ("modular", "Full"), ("atx_version", "3.1"))),
            new("psu-pp12m-750", "be quiet!", "Pure Power 12 M 750W", 449m, null, A(("wattage", 750), ("efficiency", "80+ Gold"), ("modular", "Full"), ("atx_version", "3.0"))),
            new("psu-focus-gx-850", "Seasonic", "Focus GX-850 ATX 3", 589m, null, A(("wattage", 850), ("efficiency", "80+ Gold"), ("modular", "Full"), ("atx_version", "3.0"))),
            new("psu-a650bn", "MSI", "MAG A650BN", 239m, null, A(("wattage", 650), ("efficiency", "80+ Bronze"), ("modular", "Non"), ("atx_version", "2.x"))),
            new("psu-rm1000x", "Corsair", "RM1000x Shift", 899m, null, A(("wattage", 1000), ("efficiency", "80+ Gold"), ("modular", "Full"), ("atx_version", "3.1"))),
        ],
        ["home-fridge"] =
        [
            new("fr-bosch-kgn39vlct", "Bosch", "KGN39VLCT Serie 4", 2899m, null, A(("capacity_l", 363), ("no_frost", true), ("energy_class", "C"), ("height_cm", 203), ("installation", "Freestanding"), ("color", "Inox"))),
            new("fr-samsung-rb38c7b6as9", "Samsung", "RB38C7B6AS9", 3199m, null, A(("capacity_l", 390), ("no_frost", true), ("energy_class", "A"), ("height_cm", 203), ("installation", "Freestanding"), ("color", "Silver"))),
            new("fr-beko-b5rcna406hxb", "Beko", "B5RCNA406HXB", 2299m, null, A(("capacity_l", 355), ("no_frost", true), ("energy_class", "D"), ("height_cm", 203), ("installation", "Freestanding"), ("color", "Inox"))),
            new("fr-lg-gbp62pzncc1", "LG", "GBP62PZNCC1", 2799m, null, A(("capacity_l", 384), ("no_frost", true), ("energy_class", "C"), ("height_cm", 203), ("installation", "Freestanding"), ("color", "Silver"))),
            new("fr-electrolux-lnt7me34x2", "Electrolux", "LNT7ME34X2", 2499m, null, A(("capacity_l", 330), ("no_frost", true), ("energy_class", "E"), ("height_cm", 185), ("installation", "Freestanding"), ("color", "Inox"))),
            new("fr-amica-bk3165", "Amica", "BK3165.4", 1699m, null, A(("capacity_l", 262), ("no_frost", false), ("energy_class", "E"), ("height_cm", 177), ("installation", "Built-in"), ("color", "White"))),
        ],
        ["home-washer"] =
        [
            new("wm-bosch-wgg2440mpl", "Bosch", "WGG2440MPL Serie 6", 2399m, null, A(("capacity_kg", 9), ("spin_rpm", 1400), ("energy_class", "A"), ("depth_cm", 59), ("steam", true))),
            new("wm-samsung-ww90t534dae", "Samsung", "WW90T534DAE", 1899m, null, A(("capacity_kg", 9), ("spin_rpm", 1400), ("energy_class", "A"), ("depth_cm", 55), ("steam", true))),
            new("wm-lg-f4wr509sbw", "LG", "F4WR509SBW", 2099m, null, A(("capacity_kg", 9), ("spin_rpm", 1400), ("energy_class", "A"), ("depth_cm", 56), ("steam", true))),
            new("wm-electrolux-ew6sn426bp", "Electrolux", "EW6SN426BP", 1599m, null, A(("capacity_kg", 6), ("spin_rpm", 1200), ("energy_class", "C"), ("depth_cm", 38), ("steam", false))),
            new("wm-beko-b3wfu57413", "Beko", "B3WFU57413W", 1399m, null, A(("capacity_kg", 7), ("spin_rpm", 1400), ("energy_class", "B"), ("depth_cm", 49), ("steam", true))),
        ],
    };
}
