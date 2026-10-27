using Microsoft.AspNetCore.Identity;

namespace Ashopper.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public AppUser() => Id = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// A refresh token. Only its SHA-256 hash is stored. Tokens issued from one login form a family;
/// presenting an already-rotated token revokes the whole family (likely theft).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; init; }
    public Guid FamilyId { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
