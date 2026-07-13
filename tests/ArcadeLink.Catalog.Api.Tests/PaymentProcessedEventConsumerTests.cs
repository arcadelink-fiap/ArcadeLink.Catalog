using ArcadeLink.Catalog.Api.Consumers;
using ArcadeLink.Catalog.Api.Data;
using ArcadeLink.Catalog.Api.Domain;
using ArcadeLink.Contracts;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ArcadeLink.Catalog.Api.Tests;

public class PaymentProcessedEventConsumerTests : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ITestHarness _harness;

    public PaymentProcessedEventConsumerTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        // Captured once outside the lambda — AddDbContext invokes the options action on every new
        // scope, so generating the Guid inside it would give each test scope its own separate
        // in-memory database instead of one shared database for the whole test.
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<PaymentProcessedEventConsumer>();
        });

        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
    }

    public async ValueTask DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task Approved_adds_game_to_library()
    {
        await _harness.Start();

        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        await _harness.Bus.Publish(new PaymentProcessedEvent(Guid.NewGuid(), userId, gameId, "Approved"));

        Assert.True(await _harness.Consumed.Any<PaymentProcessedEvent>());

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.True(await db.LibraryEntries.AnyAsync(e => e.UserId == userId && e.GameId == gameId));
    }

    [Fact]
    public async Task Rejected_does_not_add_game_to_library()
    {
        await _harness.Start();

        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        await _harness.Bus.Publish(new PaymentProcessedEvent(Guid.NewGuid(), userId, gameId, "Rejected"));

        Assert.True(await _harness.Consumed.Any<PaymentProcessedEvent>());

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        Assert.False(await db.LibraryEntries.AnyAsync(e => e.UserId == userId && e.GameId == gameId));
    }

    [Fact]
    public async Task Reprocessing_an_already_owned_game_does_not_duplicate()
    {
        await _harness.Start();

        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();

        using (var seedScope = _provider.CreateScope())
        {
            var seedDb = seedScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            seedDb.LibraryEntries.Add(new UserLibraryEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                GameId = gameId,
                AcquiredAtUtc = DateTime.UtcNow
            });
            await seedDb.SaveChangesAsync();
        }

        await _harness.Bus.Publish(new PaymentProcessedEvent(Guid.NewGuid(), userId, gameId, "Approved"));

        Assert.True(await _harness.Consumed.Any<PaymentProcessedEvent>());

        using var scope = _provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var count = await db.LibraryEntries.CountAsync(e => e.UserId == userId && e.GameId == gameId);
        Assert.Equal(1, count);
    }
}
