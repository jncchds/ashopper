using System.Security.Cryptography;
using System.Text;

namespace Ashopper.Infrastructure.Scraping;

public enum ScrapeStatus
{
    /// <summary>Not checked yet.</summary>
    Pending,
    Ok,
    /// <summary>The page loaded but had no price we could read.</summary>
    NoPrice,
    NotFound,
    /// <summary>The store answered with a captcha/challenge or asked us to slow down.</summary>
    Blocked,
    RobotsDisallowed,
    Error,
}

/// <summary>A product page some users asked to track. Fetched once no matter how many users track it.</summary>
public sealed class TrackedUrl
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Canonical URL (tracking parameters removed), as produced by the store adapter.</summary>
    public required string Url { get; init; }

    /// <summary>SHA-256 of <see cref="Url"/>; unique. URLs are too long for a unique index of their own.</summary>
    public required string UrlHash { get; init; }

    public required string StoreKey { get; init; }

    /// <summary>Category for the product created from this page.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Set when the user said "same item as" an existing product: prices are attached to it.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>The offer this page feeds; set after the first successful check.</summary>
    public Guid? OfferId { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset NextCheckAt { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public ScrapeStatus LastStatus { get; set; } = ScrapeStatus.Pending;

    /// <summary>Who got the last good reading: <see cref="ScrapeRecorder.ViaScraper"/> or <see cref="ScrapeRecorder.ViaBrowser"/>.</summary>
    public string? LastSuccessVia { get; set; }
    public string? LastError { get; set; }
    public int ConsecutiveFailures { get; set; }

    /// <summary>Product name from the last successful check, shown before the product is loaded.</summary>
    public string? Title { get; set; }

    public static string Hash(string url) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));
}

public sealed class TrackedUrlSubscription
{
    public Guid UserId { get; init; }
    public Guid TrackedUrlId { get; init; }
    public DateTimeOffset AddedAt { get; init; }
}

/// <summary>
/// Politeness state per host, shared by every scraper process through the database: when the next request may go out,
/// the cached robots.txt, and whether the host told us to back off.
/// </summary>
public sealed class ScrapeHost
{
    public required string Host { get; init; }
    public DateTimeOffset NextRequestAt { get; set; }
    public DateTimeOffset? BlockedUntil { get; set; }
    public string? BlockReason { get; set; }

    /// <summary>Body of robots.txt; empty when the host has none (404). Null when never fetched or unreadable.</summary>
    public string? RobotsTxt { get; set; }
    public DateTimeOffset? RobotsFetchedAt { get; set; }
}
