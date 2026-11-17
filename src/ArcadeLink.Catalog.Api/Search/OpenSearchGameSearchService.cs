using ArcadeLink.Catalog.Api.Models;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using OpenSearch.Net;
using OpenSearch.Net.Auth.AwsSigV4;

namespace ArcadeLink.Catalog.Api.Search;

public class OpenSearchGameSearchService : IGameSearchService
{
    private readonly OpenSearchClient _client;
    private readonly string _index;
    private readonly ILogger<OpenSearchGameSearchService> _logger;

    public OpenSearchGameSearchService(IOptions<SearchOptions> options, ILogger<OpenSearchGameSearchService> logger)
    {
        var o = options.Value;
        _index = o.IndexName;
        _logger = logger;

        var pool = new SingleNodeConnectionPool(new Uri(o.Endpoint!));
        // Credenciais pela cadeia padrão da AWS (Pod Identity no EKS) — nunca configurar chaves.
        var settings = o.UseAwsSigV4
            ? new ConnectionSettings(pool, new AwsSigV4HttpConnection(Amazon.RegionEndpoint.GetBySystemName(o.Region)))
            : new ConnectionSettings(pool);
        settings.DefaultIndex(_index).RequestTimeout(TimeSpan.FromSeconds(5));
        _client = new OpenSearchClient(settings);
    }

    public bool IsEnabled => true;

    public OpenSearchClient Client => _client;

    public async Task EnsureIndexAsync(CancellationToken ct = default)
    {
        var exists = await _client.Indices.ExistsAsync(_index, ct: ct);
        if (exists.Exists)
            return;

        var created = await _client.Indices.CreateAsync(_index, c => c
            .Map<GameSearchDocument>(m => m.Properties(p => p
                .Text(t => t.Name(d => d.Title).Analyzer("standard")
                    .Fields(f => f.Keyword(k => k.Name("keyword"))))
                .Text(t => t.Name(d => d.Genre).Analyzer("standard")
                    .Fields(f => f.Keyword(k => k.Name("keyword"))))
                .Text(t => t.Name(d => d.Description).Analyzer("standard"))
                .Text(t => t.Name(d => d.Tags).Analyzer("standard"))
                .Number(n => n.Name(d => d.Price).Type(NumberType.Double)))), ct);

        // Outra réplica pode ter criado o índice entre o Exists e o Create — tolera a corrida.
        if (!created.IsValid && created.ServerError?.Error?.Type != "resource_already_exists_exception")
            throw created.OriginalException ?? new InvalidOperationException(created.DebugInformation);
    }

    public async Task IndexAsync(GameSearchDocument document, CancellationToken ct = default)
    {
        var response = await _client.IndexAsync(document, i => i.Index(_index).Id(document.Id), ct);
        if (!response.IsValid)
            throw response.OriginalException ?? new InvalidOperationException(response.DebugInformation);
    }

    public async Task DeleteAsync(Guid gameId, CancellationToken ct = default)
    {
        var response = await _client.DeleteAsync<GameSearchDocument>(gameId, d => d.Index(_index), ct);
        // 404 (documento inexistente) é aceitável.
        if (!response.IsValid && response.ApiCall.HttpStatusCode != 404)
            throw response.OriginalException ?? new InvalidOperationException(response.DebugInformation);
    }

    public async Task<SearchResult> SearchAsync(string query, int page, int size, CancellationToken ct = default)
    {
        var response = await _client.SearchAsync<GameSearchDocument>(
            GameSearchQuery.Build(query, page, size).Index(_index), ct);
        if (!response.IsValid)
            throw response.OriginalException ?? new InvalidOperationException(response.DebugInformation);

        var hits = response.Hits
            .Where(h => h.Source is not null)
            .Select(h => new GameSearchHit(h.Source.Id, h.Source.Title, h.Source.Genre, h.Source.Price, h.Score ?? 0))
            .ToList();
        return new SearchResult(response.Total, hits);
    }

    public async Task<int> BulkIndexAsync(IEnumerable<GameSearchDocument> documents, CancellationToken ct = default)
    {
        var docs = documents.ToList();
        if (docs.Count == 0)
            return 0;

        var response = await _client.BulkAsync(b => b
            .Index(_index)
            .IndexMany(docs, (op, d) => op.Id(d.Id))
            .Refresh(Refresh.WaitFor), ct);

        if (response.OriginalException is not null)
            throw response.OriginalException;

        var failed = response.ItemsWithErrors.Count();
        if (failed > 0)
            _logger.LogWarning("{Failed} de {Total} documentos falharam na indexação em lote", failed, docs.Count);
        return docs.Count - failed;
    }
}
