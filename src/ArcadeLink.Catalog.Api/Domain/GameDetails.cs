using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace ArcadeLink.Catalog.Api.Domain;

public class GameDetails
{
    // Guid stored as a plain string: MongoDB.Driver 3.x throws
    // "GuidSerializer cannot serialize a Guid when GuidRepresentation is Unspecified" on any Guid
    // comparison (read or write) unless a representation is picked explicitly — String sidesteps the
    // legacy binary-subtype ambiguity entirely and needs no extra driver configuration.
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid GameId { get; set; }
    public string? RichDescription { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Screenshots { get; set; } = new();
}
