using ArcadeLink.Catalog.Api.Models;

namespace ArcadeLink.Catalog.Api.Search;

public interface IGameSearchService
{
    bool IsEnabled { get; }
    Task EnsureIndexAsync(CancellationToken ct = default);
    Task IndexAsync(GameSearchDocument document, CancellationToken ct = default);
    Task DeleteAsync(Guid gameId, CancellationToken ct = default);
    Task<SearchResult> SearchAsync(string query, int page, int size, CancellationToken ct = default);
    Task<int> BulkIndexAsync(IEnumerable<GameSearchDocument> documents, CancellationToken ct = default);
}
