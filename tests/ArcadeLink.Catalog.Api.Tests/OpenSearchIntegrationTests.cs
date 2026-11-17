using ArcadeLink.Catalog.Api.Search;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenSearch.Client;
using Xunit;

namespace ArcadeLink.Catalog.Api.Tests;

public class OpenSearchIntegrationTests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("OPENSEARCH_TEST_URL");

    private static async Task<(OpenSearchGameSearchService Service, OpenSearchClient Client, string Index)> CreateAsync()
    {
        Skip.If(string.IsNullOrWhiteSpace(Url), "OPENSEARCH_TEST_URL não definido");
        var index = $"games-test-{Guid.NewGuid():N}";
        var options = Options.Create(new SearchOptions { Endpoint = Url, IndexName = index });
        var service = new OpenSearchGameSearchService(options, NullLogger<OpenSearchGameSearchService>.Instance);
        await service.EnsureIndexAsync();
        return (service, service.Client, index);
    }

    private static GameSearchDocument Doc(string title, string? description = null, string? genre = null, params string[] tags) =>
        new(Guid.NewGuid(), title, description, genre, 10m, tags);

    [SkippableFact]
    public async Task Fuzzy_finds_typo()
    {
        var (service, client, index) = await CreateAsync();
        try
        {
            var zelda = Doc("The Legend of Zelda");
            await service.BulkIndexAsync(new[] { zelda, Doc("Hollow Knight"), Doc("Celeste") });
            await client.Indices.RefreshAsync(index);

            var result = await service.SearchAsync("zeldda", 1, 10);

            Assert.NotEmpty(result.Hits);
            Assert.Equal(zelda.Id, result.Hits[0].Id);
        }
        finally { await client.Indices.DeleteAsync(index); }
    }

    [SkippableFact]
    public async Task Orders_by_relevance()
    {
        var (service, client, index) = await CreateAsync();
        try
        {
            var souls = Doc("Dark Souls");
            var other = Doc("Some Game", "a dark adventure");
            await service.IndexAsync(other);
            await service.IndexAsync(souls);
            await client.Indices.RefreshAsync(index);

            var result = await service.SearchAsync("dark souls", 1, 10);

            Assert.Equal(souls.Id, result.Hits[0].Id);
            Assert.True(result.Hits.Count >= 2);
            Assert.True(result.Hits[0].Score >= result.Hits[1].Score);
        }
        finally { await client.Indices.DeleteAsync(index); }
    }

    [SkippableFact]
    public async Task Delete_removes_from_results()
    {
        var (service, client, index) = await CreateAsync();
        try
        {
            var celeste = Doc("Celeste");
            await service.IndexAsync(celeste);
            await client.Indices.RefreshAsync(index);
            Assert.NotEmpty((await service.SearchAsync("celeste", 1, 10)).Hits);

            await service.DeleteAsync(celeste.Id);
            await client.Indices.RefreshAsync(index);

            Assert.Empty((await service.SearchAsync("celeste", 1, 10)).Hits);
        }
        finally { await client.Indices.DeleteAsync(index); }
    }

    [SkippableFact]
    public async Task Ensure_index_is_idempotent_and_special_chars_do_not_throw()
    {
        var (service, client, index) = await CreateAsync();
        try
        {
            await service.EnsureIndexAsync();
            var result = await service.SearchAsync("\"*\\", 1, 10);
            Assert.Empty(result.Hits);
        }
        finally { await client.Indices.DeleteAsync(index); }
    }
}
