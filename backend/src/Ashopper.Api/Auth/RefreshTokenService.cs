using System.Security.Cryptography;
using Ashopper.Infrastructure.Identity;
using Ashopper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ashopper.Api.Auth;

public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

public enum RefreshOutcome
{
    Ok,
    /// <summary>Unknown or expired token.</summary>
    Invalid,
    /// <summary>A rotated token was presented again: the family is revoked (possible theft).</summary>
    Reused,
    /// <summary>
    /// The token was rotated moments ago by a parallel request (e.g. two tabs). Not treated as theft:
    /// the browser already has the new cookie, so the client just retries.
    /// </summary>
    Race,
}

public sealed record RefreshResult(RefreshOutcome Outcome, Guid UserId = default, IssuedRefreshToken? Token = null);

/// <summary>Opaque, rotating refresh tokens. Only SHA-256 hashes are stored.</summary>
public sealed class RefreshTokenService(AppDbContext db, IOptions<JwtOptions> options, TimeProvider time)
{
    private static readonly TimeSpan RaceWindow = TimeSpan.FromSeconds(30);

    public async Task<IssuedRefreshToken> IssueAsync(Guid userId, CancellationToken ct)
    {
        var (entity, issued) = Create(userId, Guid.CreateVersion7());
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return issued;
    }

    public async Task<RefreshResult> RotateAsync(string rawToken, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == Hash(rawToken), ct);
        if (token is null)
            return new(RefreshOutcome.Invalid);

        if (token.RevokedAt is { } revokedAt)
        {
            if (token.ReplacedById is not null && now - revokedAt < RaceWindow)
                return new(RefreshOutcome.Race);

            await RevokeFamilyAsync(token.FamilyId, ct);
            return new(RefreshOutcome.Reused);
        }

        if (token.ExpiresAt <= now)
            return new(RefreshOutcome.Invalid);

        var (next, issued) = Create(token.UserId, token.FamilyId);
        token.RevokedAt = now;
        token.ReplacedById = next.Id;
        db.RefreshTokens.Add(next);
        try
        {
            await db.SaveChangesAsync(ct); // RowVersion makes concurrent rotations of the same token fail here
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(RefreshOutcome.Race);
        }

        return new(RefreshOutcome.Ok, token.UserId, issued);
    }

    /// <summary>Logout: revokes the whole login session (family) the token belongs to.</summary>
    public async Task RevokeAsync(string rawToken, CancellationToken ct)
    {
        var familyId = await db.RefreshTokens
            .Where(t => t.TokenHash == Hash(rawToken))
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);
        if (familyId is { } id)
            await RevokeFamilyAsync(id, ct);
    }

    /// <summary>Signs the user out everywhere (e.g. after a password reset).</summary>
    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }

    private (RefreshToken Entity, IssuedRefreshToken Issued) Create(Guid userId, Guid familyId)
    {
        var raw = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var now = time.GetUtcNow();
        var expires = now.AddDays(options.Value.RefreshTokenDays);
        var entity = new RefreshToken { UserId = userId, FamilyId = familyId, TokenHash = Hash(raw), CreatedAt = now, ExpiresAt = expires };
        return (entity, new IssuedRefreshToken(raw, expires));
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
