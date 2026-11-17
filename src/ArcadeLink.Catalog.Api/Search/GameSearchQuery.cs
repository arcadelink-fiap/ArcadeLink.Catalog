using OpenSearch.Client;

namespace ArcadeLink.Catalog.Api.Search;

public static class GameSearchQuery
{
    public static SearchDescriptor<GameSearchDocument> Build(string query, int page, int size) =>
        new SearchDescriptor<GameSearchDocument>()
            .From((page - 1) * size)
            .Size(size)
            .Query(q => q.MultiMatch(m => m
                .Query(query)
                .Fields(f => f
                    .Field(d => d.Title, 3)
                    .Field(d => d.Tags, 2)
                    .Field(d => d.Genre, 2)
                    .Field(d => d.Description))
                .Fuzziness(Fuzziness.Auto)
                .PrefixLength(0)
                .Operator(Operator.Or)))
            .Sort(s => s.Descending(SortSpecialField.Score));
}
