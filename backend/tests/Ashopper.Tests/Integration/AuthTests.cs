using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ashopper.Api.Auth;

namespace Ashopper.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthTests(ApiFactory api)
{
    [Fact]
    public async Task Access_token_grants_access_and_its_absence_does_not()
    {
        var (client, _) = await api.SignUpAsync();

        var me = await client.GetFromJsonAsync<UserResponse>("/auth/me");
        Assert.EndsWith("@test.local", me!.Email);

        var anonymous = await api.CreateBareClient().GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_revealing_whether_the_account_exists()
    {
        var client = api.CreateBareClient();
        var unknown = await client.PostAsJsonAsync("/auth/login", new CredentialsRequest("nobody@test.local", "whatever-123"));
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_issues_a_working_access_token()
    {
        var (_, refresh) = await api.SignUpAsync();
        var client = api.CreateBareClient();

        var response = await client.SendAsync(ApiFactory.WithRefreshCookie(HttpMethod.Post, "/auth/refresh", refresh));
        response.EnsureSuccessStatusCode();
        var rotated = ApiFactory.RefreshCookieFrom(response);
        Assert.NotNull(rotated);
        Assert.NotEqual(refresh, rotated);

        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Replaying_a_rotated_token_soon_is_a_race_later_it_revokes_the_whole_family()
    {
        var (_, original) = await api.SignUpAsync();
        var client = api.CreateBareClient();

        var first = await client.SendAsync(ApiFactory.WithRefreshCookie(HttpMethod.Post, "/auth/refresh", original));
        first.EnsureSuccessStatusCode();
        var current = ApiFactory.RefreshCookieFrom(first)!;

        // A parallel tab sending the old token right away: benign, client should retry.
        var race = await client.SendAsync(ApiFactory.WithRefreshCookie(HttpMethod.Post, "/auth/refresh", original));
        Assert.Equal(HttpStatusCode.Conflict, race.StatusCode);

        // The old token showing up much later looks like theft: the whole session is revoked.
        api.Time.Advance(TimeSpan.FromMinutes(1));
        var reuse = await client.SendAsync(ApiFactory.WithRefreshCookie(HttpMethod.Post, "/auth/refresh", original));
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);

        var legit = await client.SendAsync(ApiFactory.WithRefreshCookie(HttpMethod.Post, "/auth/refresh", current));
        Assert.Equal(HttpStatusCode.Unauthorized, legit.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var (_, refresh) = await api.SignUpAsync();
        var client = api.CreateBareClient();

        var logout = await client.SendAsync(ApiFactory.WithRefreshCookie(HttpMethod.Post, "/auth/logout", refresh));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refreshed = await client.SendAsync(ApiFactory.WithRefreshCookie(HttpMethod.Post, "/auth/refresh", refresh));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
    }

    [Fact]
    public async Task Refresh_cookie_is_http_only_strict_and_scoped_to_auth()
    {
        var client = api.CreateBareClient();
        var credentials = new CredentialsRequest($"{Guid.NewGuid():N}@test.local", "Passw0rd-long");
        await client.PostAsJsonAsync("/auth/register", credentials);
        var login = await client.PostAsJsonAsync("/auth/login", credentials);

        var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(AuthEndpoints.RefreshCookie));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
