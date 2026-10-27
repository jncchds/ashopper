using System.Threading.RateLimiting;
using Ashopper.Api.Auth;
using Ashopper.Api.Endpoints;
using Ashopper.Api.Http;
using Ashopper.Infrastructure;
using Ashopper.Infrastructure.Identity;
using Ashopper.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddAshopperData(builder.Configuration);
services.ConfigureHttpJsonOptions(o => AshopperJson.Configure(o.SerializerOptions));
services.AddProblemDetails();
services.AddOpenApi();

// --- Identity: users, password hashing, lockout, email/reset tokens. No cookies: we issue JWTs ourselves.
services.AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
// Keys in the DB so email/reset tokens created on one replica validate on another.
services.AddDataProtection().PersistKeysToDbContext<AppDbContext>().SetApplicationName("ashopper");

// --- JWT access tokens + refresh tokens
services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.Section)
    .ValidateDataAnnotations()
    .Validate(o => o.SigningKeys.Any(k => k.Id == o.ActiveKeyId), "Auth:Jwt:ActiveKeyId must name one of SigningKeys.")
    .Validate(o => o.SigningKeys.All(k => IsStrongKey(k.Key)), "Each signing key must be base64 of at least 32 bytes.")
    .ValidateOnStart();
services.AddOptions<AuthOptions>().BindConfiguration(AuthOptions.Section);

services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((o, jwt) =>
    {
        o.MapInboundClaims = false; // keep "sub"/"email" as-is
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKeys = jwt.Value.SigningKeys.Select(AccessTokenService.ToSecurityKey),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Email,
        };
    });
services.AddAuthorization();
services.AddScoped<AccessTokenService>();
services.AddScoped<RefreshTokenService>();
services.AddSingleton<IAppEmailSender, LoggingEmailSender>();

// --- Abuse protection and caching
var authRequestsPerMinute = builder.Configuration.GetValue("Auth:RequestsPerMinutePerIp", 10);
services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AuthEndpoints.RateLimitPolicy, http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authRequestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});
services.AddOutputCache(o => o.AddPolicy(HttpCaching.CatalogPolicy, new PublicCatalogCachePolicy()));

// The API is only reachable through the UI's nginx proxy (internal ingress), which sets X-Forwarded-*.
services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await DatabaseMigrator.MigrateAndSeedAsync(app.Services);

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapOpenApi("/api/openapi/{documentName}.json");
// Liveness only. Deliberately no DB check: probes every few seconds would keep the
// free-tier Azure SQL database from ever auto-pausing.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).ExcludeFromDescription();

app.MapAuthEndpoints();
app.MapCatalogEndpoints();
app.MapListEndpoints();
app.MapDiscountEndpoints();

app.Run();

static bool IsStrongKey(string base64)
{
    var buffer = new byte[base64.Length];
    return Convert.TryFromBase64String(base64, buffer, out var length) && length >= 32;
}

/// <summary>Exposed for WebApplicationFactory in integration tests.</summary>
public partial class Program;
