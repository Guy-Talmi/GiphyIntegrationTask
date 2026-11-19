using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

public sealed class SearchGifsRequest
{
    [FromQuery(Name = "q")]
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired]
    [System.ComponentModel.Description("Search text: 1 to 50 characters, not whitespace-only.")]
    [Required(ErrorMessage = "A search term is required.")]
    [StringLength(50, ErrorMessage = "The search term must not exceed 50 characters.")]
    public string SearchTerm { get; set; } = string.Empty;
}
