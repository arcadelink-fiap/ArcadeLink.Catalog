using ArcadeLink.Catalog.Api.Models;

namespace ArcadeLink.Catalog.Api.Search;

/// <summary>Usado quando Search:Endpoint está vazio — a busca fica desligada e o resto do Catalog segue normal.</summary>
public class DisabledGameSearchService : IGameSearchService
{
    public bool IsEnabled => false;

    public Task EnsureIndexAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task IndexAsync(GameSearchDocument document, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid gameId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> BulkIndexAsync(IEnumerable<GameSearchDocument> documents, CancellationToken ct = default) => Task.FromResult(0);

    public Task<SearchResult> SearchAsync(string query, int page, int size, CancellationToken ct = default) =>
        throw new InvalidOperationException("A busca está desligada (Search:Endpoint não configurado).");
}
