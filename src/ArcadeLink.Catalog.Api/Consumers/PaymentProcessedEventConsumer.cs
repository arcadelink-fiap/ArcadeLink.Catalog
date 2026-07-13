using ArcadeLink.Catalog.Api.Data;
using ArcadeLink.Catalog.Api.Domain;
using ArcadeLink.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace ArcadeLink.Catalog.Api.Consumers;

public class PaymentProcessedEventConsumer(CatalogDbContext db, ILogger<PaymentProcessedEventConsumer> logger)
    : IConsumer<PaymentProcessedEvent>
{
    public async Task Consume(ConsumeContext<PaymentProcessedEvent> context)
    {
        var message = context.Message;

        if (message.Status != "Approved")
        {
            logger.LogInformation(
                "Pedido {OrderId} não aprovado (status {Status}), jogo {GameId} não adicionado à biblioteca do usuário {UserId}",
                message.OrderId, message.Status, message.GameId, message.UserId);
            return;
        }

        var alreadyOwned = await db.LibraryEntries
            .AnyAsync(e => e.UserId == message.UserId && e.GameId == message.GameId);

        if (alreadyOwned)
        {
            logger.LogInformation(
                "Jogo {GameId} já estava na biblioteca do usuário {UserId}, evento do pedido {OrderId} ignorado (reprocessamento)",
                message.GameId, message.UserId, message.OrderId);
            return;
        }

        db.LibraryEntries.Add(new UserLibraryEntry
        {
            Id = Guid.NewGuid(),
            UserId = message.UserId,
            GameId = message.GameId,
            AcquiredAtUtc = DateTime.UtcNow
        });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index (UserId, GameId) caught a race with a concurrent redelivery — already added, safe to ignore.
            logger.LogInformation(
                "Jogo {GameId} já adicionado à biblioteca do usuário {UserId} por outra entrega concorrente do pedido {OrderId}",
                message.GameId, message.UserId, message.OrderId);
            return;
        }

        logger.LogInformation(
            "Jogo {GameId} adicionado à biblioteca do usuário {UserId} (pedido {OrderId})",
            message.GameId, message.UserId, message.OrderId);
    }
}
