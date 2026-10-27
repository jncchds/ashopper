using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Net.Http.Headers;

namespace Ashopper.Api.Http;

public static class HttpCaching
{
    public const string CatalogPolicy = "catalog";

    /// <summary>
    /// Sets a strong ETag built from the given version parts and Cache-Control.
    /// Returns true when the client already has this version (respond 304).
    /// </summary>
    public static bool IsNotModified(HttpContext http, string cacheControl, params object?[] versionParts)
    {
        var raw = string.Join('|', versionParts.Select(p => p switch
        {
            DateTimeOffset d => d.UtcTicks.ToString(),
            null => "",
            _ => p.ToString(),
        }));
        var etag = $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..16]}\"";

        http.Response.Headers.ETag = etag;
        http.Response.Headers.CacheControl = cacheControl;

        var ifNoneMatch = http.Request.Headers.IfNoneMatch;
        return ifNoneMatch.Count > 0 && EntityTagHeaderValue.TryParseList(ifNoneMatch, out var tags)
            && tags.Any(t => t.Tag == etag || t.Tag == "*");
    }

    public static IResult NotModified() => Results.StatusCode(StatusCodes.Status304NotModified);
}

/// <summary>
/// Server-side output cache for public, read-mostly catalog data. Unlike the default policy, it also
/// caches requests that carry an Authorization header: the response doesn't depend on the user.
/// </summary>
public sealed class PublicCatalogCachePolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken ct)
    {
        var method = context.HttpContext.Request.Method;
        var cacheable = HttpMethods.IsGet(method) || HttpMethods.IsHead(method);
        context.EnableOutputCaching = true;
        context.AllowCacheLookup = cacheable;
        context.AllowCacheStorage = cacheable;
        context.AllowLocking = true;
        context.CacheVaryByRules.QueryKeys = "*";
        context.ResponseExpirationTimeSpan = TimeSpan.FromMinutes(10);
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken ct)
    {
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
            context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }
}
