using System.ComponentModel.DataAnnotations;
using GiphyIntegration.api.Options;
using GiphyIntegration.api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GiphyIntegration.api.Tests;

public class SearchGifsRequestTests
{
    // Runs DataAnnotations explicitly: direct controller calls do not run MVC validation.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void SearchTerm_MissingOrWhitespace_FailsValidation(string? searchTerm)
    {
        var request = new SearchGifsRequest { SearchTerm = searchTerm! };

        var errors = Validate(request);

        var error = Assert.Single(errors);
        Assert.Contains(nameof(SearchGifsRequest.SearchTerm), error.MemberNames);
        Assert.Equal("A search term is required.", error.ErrorMessage);
    }

    // A newly constructed request must not silently accept a missing search term.
    [Fact]
    public void DefaultRequest_FailsValidation()
    {
        var error = Assert.Single(Validate(new SearchGifsRequest()));

        Assert.Contains(nameof(SearchGifsRequest.SearchTerm), error.MemberNames);
        Assert.Equal("A search term is required.", error.ErrorMessage);
    }

    // With the test fixture's 50-character limit, the boundary is allowed and the next length is rejected.
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void SearchTerm_WithinLengthLimit_PassesValidation(int length)
    {
        var request = new SearchGifsRequest { SearchTerm = new string('a', length) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void SearchTerm_ExceedsDefaultLimit_FailsValidation()
    {
        var request = new SearchGifsRequest { SearchTerm = new string('a', 51) };

        var error = Assert.Single(Validate(request));

        Assert.Contains(nameof(SearchGifsRequest.SearchTerm), error.MemberNames);
        Assert.Equal("The search term must not exceed 50 characters.", error.ErrorMessage);
    }

    // A configured limit must replace the default at both sides of the boundary.
    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    public void SearchTerm_UsesConfiguredLengthLimit(int limit)
    {
        Assert.Empty(Validate(new SearchGifsRequest { SearchTerm = new string('a', limit) }, limit));

        var error = Assert.Single(Validate(
            new SearchGifsRequest { SearchTerm = new string('a', limit + 1) }, limit));

        Assert.Contains(nameof(SearchGifsRequest.SearchTerm), error.MemberNames);
        Assert.Equal($"The search term must not exceed {limit} characters.", error.ErrorMessage);
    }

    private static List<ValidationResult> Validate(SearchGifsRequest request, int limit = 50)
    {
        var services = new ServiceCollection();
        services.Configure<GiphyOptions>(options => options.MaxSearchTermLength = limit);
        using var provider = services.BuildServiceProvider();
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request, provider, null), errors,
            validateAllProperties: true);
        return errors;
    }
}
