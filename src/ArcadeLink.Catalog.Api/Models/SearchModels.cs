namespace ArcadeLink.Catalog.Api.Models;

public record SearchRequest(string Query, int Page, int Size);

public record GameSearchHit(Guid Id, string Title, string? Genre, decimal Price, double Score);

public record SearchResult(long Total, IReadOnlyList<GameSearchHit> Hits);
