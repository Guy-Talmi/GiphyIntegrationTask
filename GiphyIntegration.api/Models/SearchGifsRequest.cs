using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using GiphyIntegration.api.Options;
using Microsoft.Extensions.Options;

public sealed class SearchGifsRequest : IValidatableObject
{
    [FromQuery(Name = "q")]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
    [System.ComponentModel.Description("Search text within the configured length limit, not whitespace-only.")]
    [Required(ErrorMessage = "A search term is required.")]
    public string SearchTerm { get; set; } = string.Empty;

    // Attribute arguments must be constants; read the configurable limit during validation.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var options = validationContext.GetRequiredService<IOptions<GiphyOptions>>().Value;

        if (SearchTerm is not null && SearchTerm.Length > options.MaxSearchTermLength)
        {
            yield return new ValidationResult(
                $"The search term must not exceed {options.MaxSearchTermLength} characters.",
                new[] { nameof(SearchTerm) });
        }
    }
}
