using ArcadeLink.Catalog.Api.Models;
using ArcadeLink.Catalog.Api.Search;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArcadeLink.Catalog.Api.Tests;

public class SearchEndpointTests
{
    private class FakeService : IGameSearchService
    {
        public bool Enabled { get; set; } = true;
        public bool Throws { get; set; }
        public bool IsEnabled => Enabled;
        public Task EnsureIndexAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task IndexAsync(GameSearchDocument document, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid gameId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> BulkIndexAsync(IEnumerable<GameSearchDocument> documents, CancellationToken ct = default) => Task.FromResult(0);
        public Task<SearchResult> SearchAsync(string query, int page, int size, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("opensearch down");
            return Task.FromResult(new SearchResult(1, new[] { new GameSearchHit(Guid.NewGuid(), "Zelda", null, 1m, 1.0) }));
        }
    }

    [Fact]
    public async Task Invalid_query_returns_400()
    {
        var result = await SearchEndpoint.HandleAsync("", null, null, new FakeService(), NullLogger.Instance);

        Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Disabled_returns_503()
    {
        var result = await SearchEndpoint.HandleAsync("zelda", null, null, new FakeService { Enabled = false }, NullLogger.Instance);

        Assert.Equal(503, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Opensearch_failure_returns_503()
    {
        var result = await SearchEndpoint.HandleAsync("zelda", null, null, new FakeService { Throws = true }, NullLogger.Instance);

        Assert.Equal(503, ((IStatusCodeHttpResult)result).StatusCode);
    }

    [Fact]
    public async Task Valid_query_returns_200_with_result()
    {
        var result = await SearchEndpoint.HandleAsync("zelda", null, null, new FakeService(), NullLogger.Instance);

        var ok = Assert.IsType<Ok<SearchResult>>(result);
        Assert.Equal(1, ok.Value!.Total);
    }
}
