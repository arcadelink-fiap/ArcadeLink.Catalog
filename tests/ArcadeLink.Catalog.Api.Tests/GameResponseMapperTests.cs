using ArcadeLink.Catalog.Api.Domain;
using ArcadeLink.Catalog.Api.Models;
using Xunit;

namespace ArcadeLink.Catalog.Api.Tests;

public class GameResponseMapperTests
{
    [Fact]
    public void Merges_details_when_present()
    {
        var game = new Game { Id = Guid.NewGuid(), Title = "T", Price = 10m, CreatedAtUtc = DateTime.UtcNow };
        var details = new GameDetails { GameId = game.Id, RichDescription = "rich", Tags = ["rpg"], Screenshots = ["a.png"] };

        var response = GameResponseMapper.ToResponse(game, details);

        Assert.Equal("rich", response.RichDescription);
        Assert.Equal(["rpg"], response.Tags);
        Assert.Equal(["a.png"], response.Screenshots);
    }

    [Fact]
    public void Defaults_when_details_absent()
    {
        var game = new Game { Id = Guid.NewGuid(), Title = "T", Price = 10m, CreatedAtUtc = DateTime.UtcNow };

        var response = GameResponseMapper.ToResponse(game);

        Assert.Null(response.RichDescription);
        Assert.Empty(response.Tags);
        Assert.Empty(response.Screenshots);
    }
}
