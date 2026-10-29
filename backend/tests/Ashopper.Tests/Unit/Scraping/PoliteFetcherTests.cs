using System.Net;
using Ashopper.Infrastructure.Scraping;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Ashopper.Tests.Unit.Scraping;

public class PoliteFetcherTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeShop _shop = new();
    private readonly InMemoryHostStateStore _hosts;
    private readonly ScraperOptions _options = new() { MinDelaySeconds = 0, JitterSeconds = 0, UserAgent = "AshopperBot/0.1 (test)" };

    public PoliteFetcherTests() => _hosts = new InMemoryHostStateStore(_time);

    private PoliteFetcher Fetcher() =>
        new(new HttpClient(_shop), _hosts, Options.Create(_options), _time, NullLogger<PoliteFetcher>.Instance);

    private static readonly Uri Page = new("https://www.x-kom.pl/p/1-item.html");

    [Fact]
    public async Task Fetches_page_with_honest_user_agent_after_reading_robots()
    {
        _shop.Respond("/robots.txt", HttpStatusCode.OK, "User-agent: *\nDisallow: /koszyk\n");
        _shop.Respond("/p/1-item.html", HttpStatusCode.OK, StructuredDataTests.Fixture("x-kom.html"));

        var result = await Fetcher().FetchAsync(Page, CancellationToken.None);

        Assert.Equal(FetchKind.Ok, result.Kind);
        Assert.Equal(["/robots.txt", "/p/1-item.html"], _shop.Requests.Select(r => r.RequestUri!.AbsolutePath));
        Assert.All(_shop.Requests, r => Assert.Equal("AshopperBot/0.1 (test)", r.Headers.UserAgent.ToString()));
    }

    [Fact]
    public async Task Robots_txt_is_cached_between_pages()
    {
        _shop.Respond("/robots.txt", HttpStatusCode.NotFound, "");
        _shop.Respond("/p/1-item.html", HttpStatusCode.OK, "<html></html>");
        var fetcher = Fetcher();

        await fetcher.FetchAsync(Page, CancellationToken.None);
        await fetcher.FetchAsync(Page, CancellationToken.None);

        Assert.Equal(1, _shop.Requests.Count(r => r.RequestUri!.AbsolutePath == "/robots.txt"));
    }

    [Fact]
    public async Task Disallowed_page_is_never_requested()
    {
        _shop.Respond("/robots.txt", HttpStatusCode.OK, "User-agent: AshopperBot\nDisallow: /p/\n");

        var result = await Fetcher().FetchAsync(Page, CancellationToken.None);

        Assert.Equal(FetchKind.RobotsDisallowed, result.Kind);
        Assert.DoesNotContain(_shop.Requests, r => r.RequestUri!.AbsolutePath.StartsWith("/p/"));
    }

    [Fact]
    public async Task Unreachable_robots_txt_means_no_crawling()
    {
        _shop.Respond("/robots.txt", HttpStatusCode.InternalServerError, "oops");

        var result = await Fetcher().FetchAsync(Page, CancellationToken.None);

        Assert.Equal(FetchKind.Error, result.Kind);
        Assert.Single(_shop.Requests);
    }

    [Fact]
    public async Task Challenge_page_blocks_the_host_and_later_calls_send_nothing()
    {
        _shop.Respond("/robots.txt", HttpStatusCode.OK, "User-agent: *\nDisallow:\n");
        _shop.Respond("/p/1-item.html", HttpStatusCode.Forbidden, StructuredDataTests.Fixture("allegro-datadome.html"));
        var fetcher = Fetcher();

        var first = await fetcher.FetchAsync(Page, CancellationToken.None);
        var second = await fetcher.FetchAsync(Page, CancellationToken.None);

        Assert.Equal(FetchKind.Blocked, first.Kind);
        Assert.Equal(FetchKind.HostBackingOff, second.Kind);
        Assert.Equal(2, _shop.Requests.Count); // robots.txt + the challenged page, nothing after
        Assert.Equal(_time.GetUtcNow().AddHours(24), (await _hosts.GetAsync(Page.Host, default)).BlockedUntil);

        _time.Advance(TimeSpan.FromHours(25));
        _shop.Respond("/p/1-item.html", HttpStatusCode.OK, "<html></html>");
        Assert.Equal(FetchKind.Ok, (await fetcher.FetchAsync(Page, CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task Too_many_requests_honors_retry_after()
    {
        _shop.Respond("/robots.txt", HttpStatusCode.OK, "");
        _shop.Respond("/p/1-item.html", HttpStatusCode.TooManyRequests, "slow down", r => r.Headers.RetryAfter = new(TimeSpan.FromMinutes(30)));

        var result = await Fetcher().FetchAsync(Page, CancellationToken.None);

        Assert.Equal(FetchKind.Blocked, result.Kind);
        Assert.Equal(_time.GetUtcNow().AddMinutes(30), (await _hosts.GetAsync(Page.Host, default)).BlockedUntil);
    }

    [Fact]
    public async Task Requests_to_one_host_are_spaced_by_crawl_delay()
    {
        _options.MinDelaySeconds = 5;
        _shop.Respond("/robots.txt", HttpStatusCode.OK, "User-agent: *\nCrawl-delay: 30\n");
        _shop.Respond("/p/1-item.html", HttpStatusCode.OK, "<html></html>");
        var fetcher = Fetcher();

        // robots.txt (fresh slot) → page has to wait the default 5 s (robots isn't known yet when it's fetched)…
        var first = fetcher.FetchAsync(Page, CancellationToken.None);
        Assert.False(first.IsCompleted);
        _time.Advance(TimeSpan.FromSeconds(5));
        await first;

        // …and the next page waits the 30 s crawl-delay.
        var second = fetcher.FetchAsync(Page, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(second.IsCompleted);
        Assert.Equal(2, _shop.Requests.Count);
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(FetchKind.Ok, (await second).Kind);
        Assert.Equal(3, _shop.Requests.Count);
    }

    [Fact]
    public async Task Oversized_response_is_an_error()
    {
        _options.MaxResponseBytes = 1000;
        _shop.Respond("/robots.txt", HttpStatusCode.OK, "");
        _shop.Respond("/p/1-item.html", HttpStatusCode.OK, new string('x', 5000));

        Assert.Equal(FetchKind.Error, (await Fetcher().FetchAsync(Page, CancellationToken.None)).Kind);
    }

    /// <summary>A shop that answers from a table and records every request.</summary>
    private sealed class FakeShop : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body, Action<HttpResponseMessage>? Configure)> _routes = [];
        public List<HttpRequestMessage> Requests { get; } = [];

        public void Respond(string path, HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null) =>
            _routes[path] = (status, body, configure);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var (status, body, configure) = _routes.TryGetValue(request.RequestUri!.AbsolutePath, out var r) ? r : (HttpStatusCode.NotFound, "", null);
            var response = new HttpResponseMessage(status) { Content = new StringContent(body), RequestMessage = request };
            configure?.Invoke(response);
            return Task.FromResult(response);
        }
    }
}
