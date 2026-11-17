using ArcadeLink.Catalog.Api.Search;

namespace ArcadeLink.Catalog.Api.Tests;

public class SearchRequestValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public void Invalid_q_is_rejected(string? q)
    {
        var (request, errors) = SearchRequestValidator.Validate(q, null, null);

        Assert.Null(request);
        Assert.NotNull(errors);
        Assert.True(errors!.ContainsKey("q"));
    }

    [Fact]
    public void Q_over_100_chars_is_rejected()
    {
        var (request, errors) = SearchRequestValidator.Validate(new string('a', 101), null, null);

        Assert.Null(request);
        Assert.True(errors!.ContainsKey("q"));
    }

    [Fact]
    public void Q_with_exactly_100_chars_is_valid()
    {
        var (request, errors) = SearchRequestValidator.Validate(new string('a', 100), null, null);

        Assert.Null(errors);
        Assert.NotNull(request);
    }

    [Theory]
    [InlineData(0, null, "page")]
    [InlineData(-1, null, "page")]
    [InlineData(101, null, "page")]
    [InlineData(null, 0, "size")]
    [InlineData(null, -5, "size")]
    [InlineData(null, 51, "size")]
    public void Out_of_range_page_or_size_is_rejected(int? page, int? size, string key)
    {
        var (request, errors) = SearchRequestValidator.Validate("zelda", page, size);

        Assert.Null(request);
        Assert.True(errors!.ContainsKey(key));
    }

    [Fact]
    public void Trims_query_and_applies_defaults()
    {
        var (request, errors) = SearchRequestValidator.Validate("  zelda  ", null, null);

        Assert.Null(errors);
        Assert.Equal("zelda", request!.Query);
        Assert.Equal(1, request.Page);
        Assert.Equal(10, request.Size);
    }

    [Fact]
    public void Special_characters_are_valid()
    {
        var (request, errors) = SearchRequestValidator.Validate("\"*\\", 100, 50);

        Assert.Null(errors);
        Assert.Equal("\"*\\", request!.Query);
        Assert.Equal(100, request.Page);
        Assert.Equal(50, request.Size);
    }
}
