using ArcadeLink.Catalog.Api.Search;

namespace ArcadeLink.Catalog.Api.Tests;

public class DisabledGameSearchServiceTests
{
    private readonly DisabledGameSearchService _service = new();

    [Fact]
    public void Is_not_enabled() => Assert.False(_service.IsEnabled);

    [Fact]
    public async Task Index_delete_ensure_and_bulk_are_noops()
    {
        var doc = new GameSearchDocument(Guid.NewGuid(), "x", null, null, 1m, Array.Empty<string>());

        await _service.EnsureIndexAsync();
        await _service.IndexAsync(doc);
        await _service.DeleteAsync(doc.Id);
        Assert.Equal(0, await _service.BulkIndexAsync(new[] { doc }));
    }

    [Fact]
    public async Task Search_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.SearchAsync("zelda", 1, 10));
    }
}
