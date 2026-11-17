using ArcadeLink.Catalog.Api.Domain;

namespace ArcadeLink.Catalog.Api.Data;

public static class GameDetailsSync
{
    public static async Task TryUpsertAsync(
        IGameDetailsRepository repository,
        ILogger logger,
        GameDetails details)
    {
        try
        {
            await repository.UpsertAsync(details);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao gravar GameDetails no Mongo para o jogo {GameId} — enriquecimento é best-effort, requisição segue normalmente",
                details.GameId);
        }
    }

    public static async Task<GameDetails?> TryGetAsync(
        IGameDetailsRepository repository,
        ILogger logger,
        Guid gameId)
    {
        try
        {
            return await repository.GetByGameIdAsync(gameId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao ler GameDetails do Mongo para o jogo {GameId} — respondendo só com dados do Postgres",
                gameId);
            return null;
        }
    }

    public static async Task TryDeleteAsync(
        IGameDetailsRepository repository,
        ILogger logger,
        Guid gameId)
    {
        try
        {
            await repository.DeleteByGameIdAsync(gameId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao remover GameDetails do Mongo para o jogo {GameId} — limpeza é best-effort, requisição segue normalmente",
                gameId);
        }
    }
}
