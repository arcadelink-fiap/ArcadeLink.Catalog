using Microsoft.Extensions.Caching.Distributed;

namespace ArcadeLink.Catalog.Api.Caching;

public enum CacheResult { Hit, Miss, Bypass }

public static class CachedQuery
{
    public const string HeaderName = "X-Cache";

    public static string ToHeaderValue(CacheResult result) => result switch
    {
        CacheResult.Hit => "HIT",
        CacheResult.Miss => "MISS",
        _ => "BYPASS"
    };

    public static async Task<(T? Value, CacheResult Result)> GetOrCreateAsync<T>(
        IDistributedCache cache, string key, TimeSpan ttl, Func<Task<T?>> factory, ILogger logger)
        where T : class
    {
        try
        {
            var cached = await JsonDistributedCache.GetAsync<T>(cache, key);
            if (cached is not null)
                return (cached, CacheResult.Hit);
        }
        catch (Exception ex)
        {
            LogBypass(logger, ex, key);
            return (await factory(), CacheResult.Bypass);
        }

        var value = await factory();
        if (value is null)
            return (null, CacheResult.Miss);

        try
        {
            await JsonDistributedCache.SetAsync(cache, key, value, ttl);
            return (value, CacheResult.Miss);
        }
        catch (Exception ex)
        {
            LogBypass(logger, ex, key);
            return (value, CacheResult.Bypass);
        }
    }

    private static void LogBypass(ILogger logger, Exception ex, string key) =>
        logger.LogWarning(ex,
            "Falha ao acessar o cache para a chave {CacheKey} — respondendo direto do banco (X-Cache: BYPASS)",
            key);

    public static async Task TryRemoveAsync(IDistributedCache cache, ILogger logger, params string[] keys)
    {
        foreach (var key in keys)
        {
            try
            {
                await cache.RemoveAsync(key);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Falha ao invalidar a chave {CacheKey} no cache — entrada expira sozinha pelo TTL",
                    key);
            }
        }
    }
}
