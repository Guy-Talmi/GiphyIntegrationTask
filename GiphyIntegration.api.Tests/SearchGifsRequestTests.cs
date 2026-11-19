using System.ComponentModel.DataAnnotations;

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

    // Fifty characters is allowed; the next length is rejected.
    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    public void SearchTerm_WithinLengthLimit_PassesValidation(int length)
    {
        var request = new SearchGifsRequest { SearchTerm = new string('a', length) };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void SearchTerm_LongerThan50Characters_FailsValidation()
    {
        var request = new SearchGifsRequest { SearchTerm = new string('a', 51) };

        var error = Assert.Single(Validate(request));

        Assert.Contains(nameof(SearchGifsRequest.SearchTerm), error.MemberNames);
        Assert.Equal("The search term must not exceed 50 characters.", error.ErrorMessage);
    }

    private static List<ValidationResult> Validate(SearchGifsRequest request)
    {
        var errors = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), errors,
            validateAllProperties: true);
        return errors;
    }
}
