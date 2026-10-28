using System.Text;
using Ashopper.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Ashopper.Api.Auth;

public sealed record CredentialsRequest(string Email, string Password);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string Code, string NewPassword);
public sealed record UserResponse(Guid Id, string Email, bool EmailConfirmed);
public sealed record TokenResponse(string AccessToken, int ExpiresIn, UserResponse User);

public static class AuthEndpoints
{
    public const string RefreshCookie = "ashopper_rt";
    public const string RateLimitPolicy = "auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/auth").WithTags("Auth");

        auth.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimitPolicy).Produces<UserResponse>();
        auth.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimitPolicy).Produces<TokenResponse>();
        auth.MapPost("/refresh", RefreshAsync).Produces<TokenResponse>();
        auth.MapPost("/logout", LogoutAsync).Produces(StatusCodes.Status204NoContent);
        auth.MapGet("/confirm-email", ConfirmEmailAsync).Produces<UserResponse>();
        auth.MapPost("/forgot-password", ForgotPasswordAsync).RequireRateLimiting(RateLimitPolicy).Produces(StatusCodes.Status202Accepted);
        auth.MapPost("/reset-password", ResetPasswordAsync).RequireRateLimiting(RateLimitPolicy).Produces(StatusCodes.Status204NoContent);
        auth.MapGet("/me", MeAsync).RequireAuthorization().Produces<UserResponse>();
    }

    private static async Task<IResult> RegisterAsync(
        CredentialsRequest req, UserManager<AppUser> users, IAppEmailSender email, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var user = new AppUser { UserName = req.Email, Email = req.Email };
        var result = await users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            return ToValidationProblem(result);

        await SendConfirmationAsync(user, users, email, options.Value, ct);
        return Results.Ok(ToUser(user));
    }

    private static async Task<IResult> LoginAsync(
        CredentialsRequest req, HttpContext http, UserManager<AppUser> users, AccessTokenService access,
        RefreshTokenService refresh, IOptions<AuthOptions> authOptions, IOptions<JwtOptions> jwtOptions, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        // Same response for unknown email and wrong password: don't reveal which accounts exist.
        if (user is null)
            return Results.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
        if (await users.IsLockedOutAsync(user))
            return Results.Problem("Too many failed attempts. Try again later.", statusCode: StatusCodes.Status423Locked);
        if (!await users.CheckPasswordAsync(user, req.Password))
        {
            await users.AccessFailedAsync(user);
            return Results.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
        }
        if (authOptions.Value.RequireConfirmedEmail && !user.EmailConfirmed)
            return Results.Problem("Confirm your email first.", statusCode: StatusCodes.Status403Forbidden);

        await users.ResetAccessFailedCountAsync(user);
        var token = await refresh.IssueAsync(user.Id, ct);
        SetRefreshCookie(http, token, jwtOptions.Value);
        var at = access.Create(user);
        return Results.Ok(new TokenResponse(at.Token, at.ExpiresInSeconds, ToUser(user)));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http, UserManager<AppUser> users, AccessTokenService access, RefreshTokenService refresh,
        IOptions<JwtOptions> jwtOptions, CancellationToken ct)
    {
        if (!http.Request.Cookies.TryGetValue(RefreshCookie, out var raw) || string.IsNullOrEmpty(raw))
            return Results.Problem("Not signed in.", statusCode: StatusCodes.Status401Unauthorized);

        var result = await refresh.RotateAsync(raw, ct);
        switch (result.Outcome)
        {
            case RefreshOutcome.Race:
                return Results.Problem("Token was just refreshed by another request; retry.", statusCode: StatusCodes.Status409Conflict);
            case RefreshOutcome.Ok:
                var user = await users.FindByIdAsync(result.UserId.ToString());
                if (user is null || await users.IsLockedOutAsync(user))
                    break;
                SetRefreshCookie(http, result.Token!, jwtOptions.Value);
                var at = access.Create(user);
                return Results.Ok(new TokenResponse(at.Token, at.ExpiresInSeconds, ToUser(user)));
        }

        ClearRefreshCookie(http, jwtOptions.Value);
        return Results.Problem("Session expired. Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http, RefreshTokenService refresh, IOptions<JwtOptions> jwtOptions, CancellationToken ct)
    {
        if (http.Request.Cookies.TryGetValue(RefreshCookie, out var raw) && !string.IsNullOrEmpty(raw))
            await refresh.RevokeAsync(raw, ct);
        ClearRefreshCookie(http, jwtOptions.Value);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmEmailAsync(Guid userId, string code, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
            return Results.Problem("Invalid link.", statusCode: StatusCodes.Status400BadRequest);

        var result = await users.ConfirmEmailAsync(user, DecodeCode(code));
        return result.Succeeded ? Results.Ok(ToUser(user)) : ToValidationProblem(result);
    }

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest req, UserManager<AppUser> users, IAppEmailSender email, IOptions<AuthOptions> options, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is not null)
        {
            var code = EncodeCode(await users.GeneratePasswordResetTokenAsync(user));
            var link = $"{options.Value.PublicUrl}/reset-password?email={Uri.EscapeDataString(req.Email)}&code={code}";
            await email.SendAsync(req.Email, "Reset your ashopper password", $"Reset your password: {link}", ct);
        }
        // Always the same answer, whether or not the account exists.
        return Results.Accepted();
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest req, UserManager<AppUser> users, RefreshTokenService refresh, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null)
            return Results.Problem("Invalid reset code.", statusCode: StatusCodes.Status400BadRequest);

        var result = await users.ResetPasswordAsync(user, DecodeCode(req.Code), req.NewPassword);
        if (!result.Succeeded)
            return ToValidationProblem(result);

        await refresh.RevokeAllForUserAsync(user.Id, ct); // sign out other sessions
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, UserManager<AppUser> users)
    {
        var user = await users.FindByIdAsync(http.User.GetUserId().ToString());
        return user is null ? Results.Unauthorized() : Results.Ok(ToUser(user));
    }

    private static async Task SendConfirmationAsync(
        AppUser user, UserManager<AppUser> users, IAppEmailSender email, AuthOptions options, CancellationToken ct)
    {
        var code = EncodeCode(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = $"{options.PublicUrl}/confirm-email?userId={user.Id}&code={code}";
        await email.SendAsync(user.Email!, "Confirm your ashopper account", $"Confirm your email: {link}", ct);
    }

    private static void SetRefreshCookie(HttpContext http, IssuedRefreshToken token, JwtOptions options) =>
        http.Response.Cookies.Append(RefreshCookie, token.Token, CookieOptions(options, token.ExpiresAt));

    private static void ClearRefreshCookie(HttpContext http, JwtOptions options) =>
        http.Response.Cookies.Delete(RefreshCookie, CookieOptions(options, null));

    /// <summary>HttpOnly (JS can't read it), SameSite=Strict (no cross-site sends = CSRF-safe), scoped to /auth.</summary>
    private static CookieOptions CookieOptions(JwtOptions options, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = options.RefreshCookieSecure,
        SameSite = SameSiteMode.Strict,
        Path = "/auth",
        Expires = expires,
        IsEssential = true,
    };

    private static string EncodeCode(string code) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));

    private static string DecodeCode(string code)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return "";
        }
    }

    private static UserResponse ToUser(AppUser u) => new(u.Id, u.Email!, u.EmailConfirmed);

    private static IResult ToValidationProblem(IdentityResult result) =>
        Results.ValidationProblem(result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
}
