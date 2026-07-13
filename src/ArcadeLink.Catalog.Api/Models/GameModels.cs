namespace ArcadeLink.Catalog.Api.Models;

public record GameRequest(string Title, string? Description, string? Genre, decimal Price);

public record GameResponse(Guid Id, string Title, string? Description, string? Genre, decimal Price, DateTime CreatedAtUtc);
