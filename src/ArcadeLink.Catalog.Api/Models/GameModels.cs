namespace ArcadeLink.Catalog.Api.Models;

public record GameRequest(
    string Title,
    string? Description,
    string? Genre,
    decimal Price,
    string? RichDescription = null,
    List<string>? Tags = null,
    List<string>? Screenshots = null);

public record GameResponse(
    Guid Id,
    string Title,
    string? Description,
    string? Genre,
    decimal Price,
    DateTime CreatedAtUtc,
    string? RichDescription,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Screenshots);
