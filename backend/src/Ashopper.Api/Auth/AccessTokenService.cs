using System.Security.Claims;
using Ashopper.Infrastructure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ashopper.Api.Auth;

public sealed record AccessToken(string Token, int ExpiresInSeconds);

/// <summary>Issues short-lived JWT access tokens. Validation is stateless (no DB), on any replica.</summary>
public sealed class AccessTokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    public AccessToken Create(AppUser user)
    {
        var o = options.Value;
        var key = o.SigningKeys.Single(k => k.Id == o.ActiveKeyId);
        var now = time.GetUtcNow().UtcDateTime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(o.AccessTokenMinutes),
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ]),
            SigningCredentials = new SigningCredentials(ToSecurityKey(key), SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), o.AccessTokenMinutes * 60);
    }

    public static SymmetricSecurityKey ToSecurityKey(SigningKey key) =>
        new(Convert.FromBase64String(key.Key)) { KeyId = key.Id };
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("No 'sub' claim; endpoint must require authorization."));
}
