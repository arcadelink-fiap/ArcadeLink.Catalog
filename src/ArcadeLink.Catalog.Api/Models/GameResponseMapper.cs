namespace ArcadeLink.Catalog.Api.Models;

public static class GameResponseMapper
{
    public static GameResponse ToResponse(Domain.Game game, Domain.GameDetails? details = null) =>
        new(
            game.Id, game.Title, game.Description, game.Genre, game.Price, game.CreatedAtUtc,
            details?.RichDescription,
            details?.Tags ?? new List<string>(),
            details?.Screenshots ?? new List<string>());
}
