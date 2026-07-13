namespace ArcadeLink.Catalog.Api.Domain;

public class UserLibraryEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }
    public DateTime AcquiredAtUtc { get; set; }
}
