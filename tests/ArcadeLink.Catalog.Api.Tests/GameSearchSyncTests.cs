using ArcadeLink.Catalog.Api.Models;
using ArcadeLink.Catalog.Api.Search;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArcadeLink.Catalog.Api.Tests;

public class GameSearchSyncTests
{
    private class ThrowingService : IGameSearchService
    {
        public bool IsEnabled => true;
        public Task EnsureIndexAsync(CancellationToken ct = default) => throw new InvalidOperationException("down");
        public Task IndexAsync(GameSearchDocument document, CancellationToken ct = default) => throw new InvalidOperationException("down");
        public Task DeleteAsync(Guid gameId, CancellationToken ct = default) => throw new InvalidOperationException("down");
        public Task<SearchResult> SearchAsync(string query, int page, int size, CancellationToken ct = default) => throw new InvalidOperationException("down");
        public Task<int> BulkIndexAsync(IEnumerable<GameSearchDocument> documents, CancellationToken ct = default) => throw new InvalidOperationException("down");
    }

    [Fact]
    public async Task Index_failure_does_not_throw()
    {
        var doc = new GameSearchDocument(Guid.NewGuid(), "x", null, null, 1m, Array.Empty<string>());

        var ex = await Record.ExceptionAsync(() => GameSearchSync.TryIndexAsync(new ThrowingService(), NullLogger.Instance, doc));

        Assert.Null(ex);
    }

    [Fact]
    public async Task Delete_failure_does_not_throw()
    {
        var ex = await Record.ExceptionAsync(() => GameSearchSync.TryDeleteAsync(new ThrowingService(), NullLogger.Instance, Guid.NewGuid()));

        Assert.Null(ex);
    }
}
