namespace ArcadeLink.Catalog.Api.Domain;

public class Game
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Genre { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
