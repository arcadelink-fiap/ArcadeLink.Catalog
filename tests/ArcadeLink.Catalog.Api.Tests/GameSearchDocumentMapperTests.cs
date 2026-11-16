using ArcadeLink.Catalog.Api.Domain;
using ArcadeLink.Catalog.Api.Search;

namespace ArcadeLink.Catalog.Api.Tests;

public class GameSearchDocumentMapperTests
{
    private static Game NewGame() => new()
    {
        Id = Guid.NewGuid(), Title = "Hollow Knight", Description = "Metroidvania",
        Genre = "Ação", Price = 29.9m, CreatedAtUtc = DateTime.UtcNow
    };

    [Fact]
    public void Uses_details_tags_when_present()
    {
        var game = NewGame();
        var details = new GameDetails { GameId = game.Id, Tags = new List<string> { "indie", "2d" } };

        var doc = GameSearchDocumentMapper.From(game, details);

        Assert.Equal(game.Id, doc.Id);
        Assert.Equal("Hollow Knight", doc.Title);
        Assert.Equal(29.9m, doc.Price);
        Assert.Equal(new[] { "indie", "2d" }, doc.Tags);
    }

    [Fact]
    public void Empty_tags_when_details_null()
    {
        var doc = GameSearchDocumentMapper.From(NewGame(), null);

        Assert.Empty(doc.Tags);
    }
}
