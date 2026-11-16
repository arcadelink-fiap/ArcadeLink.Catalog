namespace ArcadeLink.Catalog.Api.Search;

public static class GameSearchSync
{
    public static async Task TryIndexAsync(IGameSearchService search, ILogger logger, GameSearchDocument document)
    {
        try
        {
            await search.IndexAsync(document);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao indexar o jogo {GameId} no OpenSearch — indexação é best-effort, requisição segue normalmente",
                document.Id);
        }
    }

    public static async Task TryDeleteAsync(IGameSearchService search, ILogger logger, Guid gameId)
    {
        try
        {
            await search.DeleteAsync(gameId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Falha ao remover o jogo {GameId} do OpenSearch — limpeza é best-effort, requisição segue normalmente",
                gameId);
        }
    }
}
