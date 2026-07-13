namespace ArcadeLink.Catalog.Api.Models;

public record LibraryEntryResponse(Guid GameId, string Title, DateTime AcquiredAtUtc);

public record PurchaseResponse(Guid OrderId, Guid GameId, decimal Price);
