using ArcadeLink.Catalog.Api.Domain;
using MongoDB.Driver;

namespace ArcadeLink.Catalog.Api.Data;

public class GameDetailsRepository : IGameDetailsRepository
{
    private readonly IMongoCollection<GameDetails> _collection;

    public GameDetailsRepository(IMongoClient client, IConfiguration configuration)
    {
        var database = client.GetDatabase(configuration["Mongo:Database"]);
        _collection = database.GetCollection<GameDetails>("gameDetails");
    }

    public async Task<GameDetails?> GetByGameIdAsync(Guid gameId) =>
        await _collection.Find(d => d.GameId == gameId).FirstOrDefaultAsync();

    public async Task UpsertAsync(GameDetails details) =>
        await _collection.ReplaceOneAsync(
            d => d.GameId == details.GameId,
            details,
            new ReplaceOptions { IsUpsert = true });
}
