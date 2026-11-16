using ArcadeLink.Catalog.Api.Domain;

namespace ArcadeLink.Catalog.Api.Search;

public static class GameSearchDocumentMapper
{
    public static GameSearchDocument From(Game game, GameDetails? details) =>
        new(game.Id, game.Title, game.Description, game.Genre, game.Price,
            details?.Tags is { Count: > 0 } tags ? tags.ToList() : new List<string>());
}
