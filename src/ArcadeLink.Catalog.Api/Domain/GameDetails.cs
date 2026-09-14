using MongoDB.Bson.Serialization.Attributes;

namespace ArcadeLink.Catalog.Api.Domain;

public class GameDetails
{
    [BsonId]
    public Guid GameId { get; set; }
    public string? RichDescription { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Screenshots { get; set; } = new();
}
