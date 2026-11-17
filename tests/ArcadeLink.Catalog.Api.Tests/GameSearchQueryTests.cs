using System.Text.Json;
using ArcadeLink.Catalog.Api.Search;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ArcadeLink.Catalog.Api.Tests;

public class GameSearchQueryTests
{
    private static JsonElement Serialize(string q, int page, int size)
    {
        var json = new OpenSearchClient().RequestResponseSerializer
            .SerializeToString(GameSearchQuery.Build(q, page, size));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void Builds_fuzzy_multi_match_with_boosts()
    {
        var root = Serialize("zelda", 2, 10);
        var mm = root.GetProperty("query").GetProperty("multi_match");

        Assert.Equal("AUTO", mm.GetProperty("fuzziness").GetString());
        Assert.Equal("zelda", mm.GetProperty("query").GetString());
        Assert.Equal(0, mm.GetProperty("prefix_length").GetInt32());
        Assert.Equal("or", mm.GetProperty("operator").GetString());
        var fields = mm.GetProperty("fields").EnumerateArray().Select(f => f.GetString()).ToArray();
        Assert.Equal(new[] { "title^3", "tags^2", "genre^2", "description" }, fields);
    }

    [Fact]
    public void Computes_pagination_and_sorts_by_score()
    {
        var root = Serialize("zelda", 2, 10);

        Assert.Equal(10, root.GetProperty("from").GetInt32());
        Assert.Equal(10, root.GetProperty("size").GetInt32());
        var sortText = root.GetProperty("sort").GetRawText();
        Assert.Contains("_score", sortText);
        Assert.Contains("desc", sortText);
    }
}
