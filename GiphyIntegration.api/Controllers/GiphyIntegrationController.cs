using GiphyIntegration.api.Services;
using Microsoft.AspNetCore.Mvc;

namespace GiphyIntegration.api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class GiphyIntegrationController : ControllerBase
{
    private readonly IGifService _gifService;

    public GiphyIntegrationController(IGifService gifService)
    {
        _gifService = gifService;
    }

    [HttpGet("trending")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetTrending(CancellationToken cancellationToken)
    {
        var urls = await _gifService.GetTrendingAsync(cancellationToken);

        return Ok(urls);
    }

    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<string>>> Search([FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("A search term is required.");
        }

        if (q.Length > 50)
        {
            return BadRequest("The search term must not exceed 50 characters.");
        }

        var urls = await _gifService.SearchAsync(q, cancellationToken);

        return Ok(urls);
    }
}
