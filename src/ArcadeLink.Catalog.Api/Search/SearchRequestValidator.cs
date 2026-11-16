using ArcadeLink.Catalog.Api.Models;

namespace ArcadeLink.Catalog.Api.Search;

public static class SearchRequestValidator
{
    public const int MinQueryLength = 2;
    public const int MaxQueryLength = 100;
    public const int MaxPage = 100;
    public const int DefaultSize = 10;
    public const int MaxSize = 50;

    public static (SearchRequest? Request, Dictionary<string, string[]>? Errors) Validate(string? q, int? page, int? size)
    {
        var errors = new Dictionary<string, string[]>();

        var query = q?.Trim() ?? string.Empty;
        if (query.Length < MinQueryLength || query.Length > MaxQueryLength)
            errors["q"] = [$"O termo de busca deve ter entre {MinQueryLength} e {MaxQueryLength} caracteres."];

        var effectivePage = page ?? 1;
        if (effectivePage < 1 || effectivePage > MaxPage)
            errors["page"] = [$"A página deve estar entre 1 e {MaxPage}."];

        var effectiveSize = size ?? DefaultSize;
        if (effectiveSize < 1 || effectiveSize > MaxSize)
            errors["size"] = [$"O tamanho da página deve estar entre 1 e {MaxSize}."];

        return errors.Count > 0
            ? (null, errors)
            : (new SearchRequest(query, effectivePage, effectiveSize), null);
    }
}
