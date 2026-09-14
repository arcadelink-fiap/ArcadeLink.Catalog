using ArcadeLink.Catalog.Api.Domain;

namespace ArcadeLink.Catalog.Api.Data;

public interface IGameDetailsRepository
{
    Task<GameDetails?> GetByGameIdAsync(Guid gameId);
    Task UpsertAsync(GameDetails details);
}
