using System.ComponentModel.DataAnnotations;

namespace Ashopper.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Auth:Jwt";

    [Required] public string Issuer { get; set; } = "ashopper";
    [Required] public string Audience { get; set; } = "ashopper";
    [Range(1, 120)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 14;

    /// <summary>
    /// All keys that validate tokens. New tokens are signed with <see cref="ActiveKeyId"/>.
    /// To rotate: add a key, switch ActiveKeyId, remove the old key after AccessTokenMinutes.
    /// </summary>
    [MinLength(1)] public List<SigningKey> SigningKeys { get; set; } = [];
    [Required] public string ActiveKeyId { get; set; } = "";

    /// <summary>Secure flag on the refresh cookie. Off only for plain-http local dev.</summary>
    public bool RefreshCookieSecure { get; set; } = true;
}

public sealed class SigningKey
{
    [Required] public string Id { get; set; } = "";
    /// <summary>Base64, at least 32 bytes (HS256).</summary>
    [Required] public string Key { get; set; } = "";
}

public sealed class AuthOptions
{
    public const string Section = "Auth";
    public bool RequireConfirmedEmail { get; set; }
    /// <summary>Public URL of the UI, used to build links in emails.</summary>
    public string PublicUrl { get; set; } = "http://localhost:5173";
}
