namespace ArcadeLink.Catalog.Api.Search;

public record GameSearchDocument(
    Guid Id,
    string Title,
    string? Description,
    string? Genre,
    decimal Price,
    IReadOnlyList<string> Tags);
