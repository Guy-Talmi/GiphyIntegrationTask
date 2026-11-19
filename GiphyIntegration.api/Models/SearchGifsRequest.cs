using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

public sealed class SearchGifsRequest
{
    [FromQuery(Name = "q")]
    [Required(ErrorMessage = "A search term is required.")]
    [StringLength(50, ErrorMessage = "The search term must not exceed 50 characters.")]
    public string SearchTerm { get; set; } = string.Empty;
}