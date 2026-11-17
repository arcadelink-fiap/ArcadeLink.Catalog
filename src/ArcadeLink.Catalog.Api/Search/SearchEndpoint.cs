using ArcadeLink.Catalog.Api.Data;
using ArcadeLink.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArcadeLink.Catalog.Api.Search;

public static class SearchEndpoint
{
    private const string UnavailableTitle = "Busca indisponível no momento.";

    public static async Task<IResult> HandleAsync(
        string? q, int? page, int? size, IGameSearchService search, ILogger logger, CancellationToken ct = default)
    {
        var (request, errors) = SearchRequestValidator.Validate(q, page, size);
        if (errors is not null)
            return Results.ValidationProblem(errors);

        if (!search.IsEnabled)
            return Results.Problem(title: UnavailableTitle, statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            var result = await search.SearchAsync(request!.Query, request.Page, request.Size, ct);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao consultar o OpenSearch para a busca — respondendo 503");
            return Results.Problem(title: UnavailableTitle, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    public static async Task<IResult> ReindexAsync(
        CatalogDbContext db, IGameDetailsRepository detailsRepo, IGameSearchService search, ILogger logger)
    {
        if (!search.IsEnabled)
            return Results.Problem(title: UnavailableTitle, statusCode: StatusCodes.Status503ServiceUnavailable);

        try
        {
            var games = await db.Games.AsNoTracking().OrderBy(g => g.Title).ToListAsync();
            var documents = new List<GameSearchDocument>(games.Count);
            foreach (var game in games)
            {
                var details = await GameDetailsSync.TryGetAsync(detailsRepo, logger, game.Id);
                documents.Add(GameSearchDocumentMapper.From(game, details));
            }

            await search.EnsureIndexAsync();
            var indexed = await search.BulkIndexAsync(documents);
            return Results.Ok(new { indexed });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao reindexar os jogos no OpenSearch — respondendo 503");
            return Results.Problem(title: UnavailableTitle, statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
