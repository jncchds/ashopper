using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ashopper.Api.Auth;
using Ashopper.Infrastructure;
using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Persistence;
using Ashopper.Infrastructure.Scraping;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.MsSql;

namespace Ashopper.Tests.Integration;

/// <summary>
/// Real API + real SQL Server (Testcontainers), seeded with 30 days of fake prices.
/// One database for the whole test collection; each test registers its own users.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2025-latest").Build();

    /// <summary>The API's clock; tests move it to cross time windows (e.g. refresh-token reuse detection).</summary>
    public FakeTimeProvider Time { get; } = new(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Sql"] = _sql.GetConnectionString() })
            .Build();
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddAshopperData(config)
            .AddAshopperCollector(config)
            .BuildServiceProvider();

        await DatabaseMigrator.MigrateAndSeedAsync(services);
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CollectorService>()
            .RunAsync(new CollectorOptions { BackfillDays = 30 }, CancellationToken.None);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sql", _sql.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.UseSetting("Auth:Jwt:RefreshCookieSecure", "false");
        builder.UseSetting("Auth:RequestsPerMinutePerIp", "10000"); // every test client shares one "IP"
        builder.UseSetting("Auth:Jwt:ActiveKeyId", "test");
        builder.UseSetting("Auth:Jwt:SigningKeys:0:Id", "test");
        builder.UseSetting("Auth:Jwt:SigningKeys:0:Key", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)i).ToArray()));
        builder.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(Time));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    /// <summary>
    /// A scraper on the test database that talks to <paramref name="shop"/> instead of the internet, with no spacing
    /// between requests and the API's clock.
    /// </summary>
    public ScrapeService CreateScraper(HttpMessageHandler shop)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Sql"] = _sql.GetConnectionString(),
                ["Scraper:MinDelaySeconds"] = "0",
                ["Scraper:JitterSeconds"] = "0",
            })
            .Build();
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<IConfiguration>(config)
            .AddAshopperData(config)
            .AddSingleton<TimeProvider>(Time)
            .AddSingleton<IHostStateStore>(new InMemoryHostStateStore(Time))
            .AddAshopperScraper();
        services.AddHttpClient(PoliteFetcher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => shop);
        return services.BuildServiceProvider().GetRequiredService<ScrapeService>();
    }

    /// <summary>A client without automatic cookies, so tests control exactly which refresh token is sent.</summary>
    public HttpClient CreateBareClient() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    /// <summary>Registers a fresh user and returns a client authorized with their access token plus the refresh cookie value.</summary>
    public async Task<(HttpClient Client, string RefreshToken)> SignUpAsync()
    {
        var client = CreateBareClient();
        var credentials = new CredentialsRequest($"{Guid.NewGuid():N}@test.local", "Passw0rd-long");
        (await client.PostAsJsonAsync("/auth/register", credentials)).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/auth/login", credentials);
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return (client, RefreshCookieFrom(login)!);
    }

    public static string? RefreshCookieFrom(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Select(c => c.Split(';')[0]).Where(c => c.StartsWith(AuthEndpoints.RefreshCookie + "="))
                .Select(c => c[(AuthEndpoints.RefreshCookie.Length + 1)..]).FirstOrDefault(v => v.Length > 0)
            : null;

    public static HttpRequestMessage WithRefreshCookie(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", $"{AuthEndpoints.RefreshCookie}={token}");
        return request;
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}
