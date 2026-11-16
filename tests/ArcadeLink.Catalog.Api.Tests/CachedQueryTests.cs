using ArcadeLink.Catalog.Api.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArcadeLink.Catalog.Api.Tests;

public class CachedQueryTests
{
    private record Item(string Name);

    private class ThrowingDistributedCache : IDistributedCache
    {
        private static InvalidOperationException Down() => new("redis down");
        public byte[]? Get(string key) => throw Down();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Down();
        public void Refresh(string key) => throw Down();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Down();
        public void Remove(string key) => throw Down();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Down();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Down();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Down();
    }

    private static IDistributedCache NewCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task First_call_is_miss_and_second_is_hit()
    {
        var cache = NewCache();
        var calls = 0;
        Task<Item?> Factory() { calls++; return Task.FromResult<Item?>(new Item("a")); }

        var first = await CachedQuery.GetOrCreateAsync(cache, "k", TimeSpan.FromSeconds(60), Factory, NullLogger.Instance);
        var second = await CachedQuery.GetOrCreateAsync(cache, "k", TimeSpan.FromSeconds(60), Factory, NullLogger.Instance);

        Assert.Equal(1, calls);
        Assert.Equal(CacheResult.Miss, first.Result);
        Assert.Equal(CacheResult.Hit, second.Result);
        Assert.Equal(first.Value, second.Value);
    }

    [Fact]
    public async Task Null_result_is_not_cached()
    {
        var cache = NewCache();
        var calls = 0;
        Task<Item?> Factory() { calls++; return Task.FromResult<Item?>(null); }

        var first = await CachedQuery.GetOrCreateAsync(cache, "k", TimeSpan.FromSeconds(60), Factory, NullLogger.Instance);
        var second = await CachedQuery.GetOrCreateAsync(cache, "k", TimeSpan.FromSeconds(60), Factory, NullLogger.Instance);

        Assert.Equal(2, calls);
        Assert.Equal(CacheResult.Miss, first.Result);
        Assert.Equal(CacheResult.Miss, second.Result);
    }

    [Fact]
    public async Task Cache_failure_returns_factory_value_as_bypass()
    {
        var expected = new Item("a");

        var result = await CachedQuery.GetOrCreateAsync(
            new ThrowingDistributedCache(), "k", TimeSpan.FromSeconds(60),
            () => Task.FromResult<Item?>(expected), NullLogger.Instance);

        Assert.Equal(expected, result.Value);
        Assert.Equal(CacheResult.Bypass, result.Result);
    }

    [Fact]
    public async Task TryRemoveAsync_swallows_cache_failure()
    {
        var exception = await Record.ExceptionAsync(() =>
            CachedQuery.TryRemoveAsync(new ThrowingDistributedCache(), NullLogger.Instance, "a", "b"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(CacheResult.Hit, "HIT")]
    [InlineData(CacheResult.Miss, "MISS")]
    [InlineData(CacheResult.Bypass, "BYPASS")]
    public void ToHeaderValue_maps_results(CacheResult result, string expected) =>
        Assert.Equal(expected, CachedQuery.ToHeaderValue(result));
}
