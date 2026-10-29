using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashopper.Infrastructure.Scraping;

public enum FetchKind
{
    Ok,
    NotFound,
    /// <summary>This request got a challenge page or a 429/503; the host is now backing off.</summary>
    Blocked,
    /// <summary>The host was already backing off, so nothing was sent.</summary>
    HostBackingOff,
    RobotsDisallowed,
    Error,
}

public sealed record FetchResult(FetchKind Kind, string? Html, string? Detail = null);

/// <summary>
/// The only way the scraper talks to shops. Every request goes through the same rules: honest User-Agent, robots.txt,
/// one request per host at a time with spacing reserved in the database, and backing off when a host pushes back.
/// It never tries to get around a challenge; it records it and stays away.
/// </summary>
public sealed class PoliteFetcher(
    HttpClient http,
    IHostStateStore hosts,
    IOptions<ScraperOptions> options,
    TimeProvider time,
    ILogger<PoliteFetcher> logger)
{
    public const string HttpClientName = "scraper";

    private const int MaxRobotsBytes = 512 * 1024;
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromDays(7);

    /// <summary>Waits longer than this for a host's slot are not worth holding a run open for.</summary>
    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(5);

    private ScraperOptions Options => options.Value;

    public async Task<FetchResult> FetchAsync(Uri url, CancellationToken ct)
    {
        var state = await hosts.GetAsync(url.Host, ct);
        if (state.BlockedUntil > time.GetUtcNow())
            return new FetchResult(FetchKind.HostBackingOff, null, $"{state.BlockReason} Backing off until {state.BlockedUntil:u}.");

        var (robots, robotsProblem) = await GetRobotsAsync(url, state, ct);
        if (robots is null)
            return robotsProblem!;
        if (!robots.IsAllowed(url.PathAndQuery))
            return new FetchResult(FetchKind.RobotsDisallowed, null, "robots.txt disallows this path for our user agent.");

        if (!await WaitTurnAsync(url.Host, robots.CrawlDelay, ct))
            return new FetchResult(FetchKind.HostBackingOff, null, "Another scraper process holds this host's slot; will retry later.");

        try
        {
            using var response = await SendAsync(url, "text/html,application/xhtml+xml;q=0.9,*/*;q=0.5", ct);
            var body = await ReadBodyAsync(response, Options.MaxResponseBytes, ct);

            if (await PushedBackAsync(response, body, state, ct) is { } pushback)
                return pushback;

            return response.StatusCode switch
            {
                HttpStatusCode.OK => new FetchResult(FetchKind.Ok, body),
                HttpStatusCode.NotFound or HttpStatusCode.Gone => new FetchResult(FetchKind.NotFound, null, $"HTTP {(int)response.StatusCode}"),
                _ => new FetchResult(FetchKind.Error, null, $"HTTP {(int)response.StatusCode}"),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException && !ct.IsCancellationRequested)
        {
            return new FetchResult(FetchKind.Error, null, ex.Message);
        }
    }

    /// <summary>The parsed robots.txt, or null plus the result to report when it can't be used.</summary>
    private async Task<(RobotsTxt?, FetchResult?)> GetRobotsAsync(Uri url, ScrapeHost state, CancellationToken ct)
    {
        var fresh = state.RobotsFetchedAt is { } at && time.GetUtcNow() - at < TimeSpan.FromHours(Options.RobotsCacheHours);
        if (fresh && state.RobotsTxt is not null)
            return (Parse(state.RobotsTxt), null);

        if (!await WaitTurnAsync(url.Host, null, ct))
            return (null, new FetchResult(FetchKind.HostBackingOff, null, "Another scraper process holds this host's slot; will retry later."));

        var robotsUrl = new Uri(url, "/robots.txt");
        try
        {
            using var response = await SendAsync(robotsUrl, "text/plain,*/*;q=0.5", ct);
            var body = await ReadBodyAsync(response, MaxRobotsBytes, ct);
            if (await PushedBackAsync(response, body, state, ct) is { } pushback)
                return (null, pushback);

            var code = (int)response.StatusCode;
            // RFC 9309: 2xx → use it; other 4xx → no restrictions; 5xx → assume everything is disallowed for now.
            string? text = code is >= 200 and < 300 ? body : code is >= 400 and < 500 ? "" : null;
            if (text is null)
                return (null, new FetchResult(FetchKind.Error, null, $"robots.txt unavailable (HTTP {code}); will retry later."));

            state.RobotsTxt = text;
            state.RobotsFetchedAt = time.GetUtcNow();
            await hosts.SaveAsync(state, ct);
            logger.LogInformation("Fetched robots.txt for {Host} (HTTP {Status}, {Length} chars)", url.Host, code, text.Length);
            return (Parse(text), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException && !ct.IsCancellationRequested)
        {
            return (null, new FetchResult(FetchKind.Error, null, $"robots.txt unreachable: {ex.Message}"));
        }
    }

    private RobotsTxt Parse(string text) => text.Length == 0 ? RobotsTxt.AllowAll : RobotsTxt.Parse(text, Options.RobotsToken);

    /// <summary>Waits for and claims this host's next request slot. False if the wait would be unreasonably long.</summary>
    private async Task<bool> WaitTurnAsync(string host, TimeSpan? crawlDelay, CancellationToken ct)
    {
        var minimum = TimeSpan.FromSeconds(Options.MinDelaySeconds);
        var spacing = (crawlDelay > minimum ? crawlDelay.Value : minimum)
            + TimeSpan.FromSeconds(Random.Shared.NextDouble() * Options.JitterSeconds);

        while (true)
        {
            var busyUntil = await hosts.TryReserveAsync(host, spacing, ct);
            if (busyUntil is null)
                return true;
            var wait = busyUntil.Value - time.GetUtcNow();
            if (wait > MaxWait)
                return false;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, time, ct);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, string accept, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", Options.UserAgent);
        request.Headers.TryAddWithoutValidation("Accept", accept);
        request.Headers.TryAddWithoutValidation("Accept-Language", "pl-PL,pl;q=0.9,en;q=0.5");
        logger.LogDebug("GET {Url}", url);
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
    }

    /// <summary>A captcha/challenge, 429 or 503 → record the back-off for the whole host and report Blocked.</summary>
    private async Task<FetchResult?> PushedBackAsync(HttpResponseMessage response, string body, ScrapeHost state, CancellationToken ct)
    {
        var code = (int)response.StatusCode;
        TimeSpan backoff;
        string reason;
        if (IsChallenge(response, body))
        {
            backoff = TimeSpan.FromHours(Options.ChallengeBackoffHours);
            reason = $"The store served a bot challenge (HTTP {code}).";
        }
        else if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
        {
            backoff = RetryAfter(response) ?? TimeSpan.FromHours(Options.RateLimitBackoffHours);
            reason = $"The store asked us to slow down (HTTP {code}).";
        }
        else
        {
            return null;
        }

        if (backoff > MaxBackoff)
            backoff = MaxBackoff;
        state.BlockedUntil = time.GetUtcNow() + backoff;
        state.BlockReason = reason;
        await hosts.SaveAsync(state, ct);
        logger.LogWarning("{Host}: {Reason} Backing off for {Backoff}.", response.RequestMessage?.RequestUri?.Host, reason, backoff);
        return new FetchResult(FetchKind.Blocked, null, reason);
    }

    private static readonly string[] ChallengeMarkers =
    [
        "captcha-delivery.com", // DataDome
        "/cdn-cgi/challenge-platform/", "cf-chl-", "cf_chl_", // Cloudflare
        "_Incapsula_Resource", "Incapsula incident", // Imperva
        "px-captcha", "_pxCaptcha", // PerimeterX / HUMAN
        "g-recaptcha", "h-captcha", "hcaptcha.com", "captcha",
    ];

    /// <summary>
    /// A bot check instead of the page. Only looked for on error statuses, or on 200 pages that are suspiciously small,
    /// so a product page that merely mentions "captcha" in a script isn't mistaken for one.
    /// </summary>
    public static bool IsChallenge(HttpResponseMessage response, string body)
    {
        if (response.Headers.TryGetValues("cf-mitigated", out var mitigated) && mitigated.Contains("challenge"))
            return true;
        var suspicious = response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable
            || (response.IsSuccessStatusCode && body.Length < 20_000);
        return suspicious && ChallengeMarkers.Any(m => body.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        if (retry?.Delta is { } delta)
            return delta;
        if (retry?.Date is { } date)
            return date - time.GetUtcNow() is var d && d > TimeSpan.Zero ? d : null;
        return null;
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, int maxBytes, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException($"Response is larger than {maxBytes / 1024} KB.");
            buffer.Write(chunk, 0, read);
        }

        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        Encoding encoding;
        try
        {
            encoding = charset is { Length: > 0 } ? Encoding.GetEncoding(charset) : Encoding.UTF8;
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }
        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
