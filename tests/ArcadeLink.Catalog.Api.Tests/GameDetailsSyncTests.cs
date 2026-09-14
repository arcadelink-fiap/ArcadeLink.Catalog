using ArcadeLink.Catalog.Api.Data;
using ArcadeLink.Catalog.Api.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ArcadeLink.Catalog.Api.Tests;

public class GameDetailsSyncTests
{
    private class ThrowingRepository : IGameDetailsRepository
    {
        public Task<GameDetails?> GetByGameIdAsync(Guid gameId) => throw new NotImplementedException();
        public Task UpsertAsync(GameDetails details) => throw new InvalidOperationException("mongo down");
    }

    private class InMemoryRepository : IGameDetailsRepository
    {
        public GameDetails? Saved { get; private set; }
        public Task<GameDetails?> GetByGameIdAsync(Guid gameId) => Task.FromResult(Saved);
        public Task UpsertAsync(GameDetails details) { Saved = details; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Failure_in_mongo_does_not_throw()
    {
        var repository = new ThrowingRepository();
        var details = new GameDetails { GameId = Guid.NewGuid(), RichDescription = "x" };

        var exception = await Record.ExceptionAsync(() =>
            GameDetailsSync.TryUpsertAsync(repository, NullLogger.Instance, details));

        Assert.Null(exception);
    }

    [Fact]
    public async Task Success_saves_details()
    {
        var repository = new InMemoryRepository();
        var details = new GameDetails { GameId = Guid.NewGuid(), RichDescription = "x" };

        await GameDetailsSync.TryUpsertAsync(repository, NullLogger.Instance, details);

        Assert.Equal(details, repository.Saved);
    }

    [Fact]
    public async Task Read_failure_in_mongo_does_not_throw_and_returns_null()
    {
        var repository = new ThrowingRepository();

        GameDetails? result = null;
        var exception = await Record.ExceptionAsync(async () =>
            result = await GameDetailsSync.TryGetAsync(repository, NullLogger.Instance, Guid.NewGuid()));

        Assert.Null(exception);
        Assert.Null(result);
    }

    [Fact]
    public async Task Read_success_returns_saved_details()
    {
        var repository = new InMemoryRepository();
        var details = new GameDetails { GameId = Guid.NewGuid(), RichDescription = "x" };
        await repository.UpsertAsync(details);

        var result = await GameDetailsSync.TryGetAsync(repository, NullLogger.Instance, details.GameId);

        Assert.Equal(details, result);
    }
}
