namespace ArcadeLink.Catalog.Api.Search;

public class SearchOptions
{
    public const string SectionName = "Search";

    public string? Endpoint { get; set; }
    public string IndexName { get; set; } = "games";
    public bool UseAwsSigV4 { get; set; }
    public string Region { get; set; } = "us-east-2";
}
