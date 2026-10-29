namespace Ashopper.Infrastructure.Scraping;

/// <summary>Bound from the "Scraper" configuration section.</summary>
public sealed class ScraperOptions
{
    public const string Section = "Scraper";

    /// <summary>
    /// Sent with every request. It names the bot and how to reach its owner; set a real contact when you deploy.
    /// The first word (before "/") is the token matched against robots.txt groups.
    /// </summary>
    public string UserAgent { get; set; } = "AshopperBot/0.1 (personal price tracker; contact: set Scraper__UserAgent)";

    /// <summary>Minimum gap between two requests to the same host. A larger robots.txt Crawl-delay wins.</summary>
    public int MinDelaySeconds { get; set; } = 20;

    /// <summary>Random extra gap (0..this) added to every wait, so requests don't come at a fixed beat.</summary>
    public int JitterSeconds { get; set; } = 10;

    /// <summary>How often a tracked page is checked when all is well.</summary>
    public double RecheckHours { get; set; } = 12;

    /// <summary>Upper bound for the exponential backoff after failures.</summary>
    public double MaxBackoffHours { get; set; } = 24 * 7;

    /// <summary>Pages fetched per host in one run; the rest wait for the next run.</summary>
    public int MaxPerHostPerRun { get; set; } = 30;

    /// <summary>Hosts processed at the same time (each still gets one request at a time).</summary>
    public int MaxParallelHosts { get; set; } = 4;

    public int TimeoutSeconds { get; set; } = 30;
    public int MaxResponseBytes { get; set; } = 4 * 1024 * 1024;
    public double RobotsCacheHours { get; set; } = 24;

    /// <summary>How long a host stays blocked after it served a captcha/challenge page.</summary>
    public double ChallengeBackoffHours { get; set; } = 24;

    /// <summary>Used for 429/503 without a Retry-After header.</summary>
    public double RateLimitBackoffHours { get; set; } = 1;

    /// <summary>Per-store switches, e.g. Scraper:Stores:allegro:Enabled = false. Stores are enabled by default.</summary>
    public Dictionary<string, StoreSwitch> Stores { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(string storeKey) => !Stores.TryGetValue(storeKey, out var s) || s.Enabled;

    /// <summary>The robots.txt product token, e.g. "AshopperBot".</summary>
    public string RobotsToken => UserAgent.Split('/', ' ')[0];
}

public sealed class StoreSwitch
{
    public bool Enabled { get; set; } = true;
}
