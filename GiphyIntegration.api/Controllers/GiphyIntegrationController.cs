using GiphyIntegration.api.Models;
using GiphyIntegration.api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace GiphyIntegration.api.Controllers;

[Route("api/[controller]")]
[ApiController]
public class GiphyIntegrationController : ControllerBase
{
    private readonly IGifService _gifService;
    private readonly ILogger<GiphyIntegrationController> _logger;

    public GiphyIntegrationController(IGifService gifService, ILogger<GiphyIntegrationController> logger)
    {
        _gifService = gifService;
        _logger = logger;
    }

    [HttpGet("trending")]
    [EndpointSummary("Get trending GIFs")]
    [EndpointDescription("Returns original GIF URLs from the first page of GIPHY trending results, up to the configured results limit. Results are cached for the configured duration.")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Task<ActionResult<IReadOnlyList<string>>> GetTrending(CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => _gifService.GetTrendingAsync(cancellationToken),
            cancellationToken);
    }

    [HttpGet("search")]
    [EndpointSummary("Search for GIFs")]
    [EndpointDescription("Returns original GIF URLs matching q, up to the configured results limit. The search term is required, cannot be whitespace, and must not exceed the configured length limit. No matches returns an empty array. Results are cached by search term.")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public Task<ActionResult<IReadOnlyList<string>>> Search([FromQuery] SearchGifsRequest request, CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => _gifService.SearchAsync(request.SearchTerm, cancellationToken),
            cancellationToken,
            isSearch: true);
    }

    // Execute the service call inside the try block to catch immediate and asynchronous failures.
    private async Task<ActionResult<IReadOnlyList<string>>> ExecuteAsync(Func<Task<IReadOnlyList<string>>> action, CancellationToken cancellationToken,
                                                                         bool isSearch = false)
    {
        try
        {
            var urls = await action();
            return Ok(urls);
        }
        catch (ArgumentException ex) when (isSearch && ex.ParamName == "searchTerm")
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest,
                title: "The search term is invalid.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is not an upstream timeout.
            throw;
        }
        catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            LogFailure(LogLevel.Warning, isSearch, "UpstreamTimeout");

            return Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "GIPHY did not respond in time.");
        }
        catch (HttpRequestException)
        {
            LogFailure(LogLevel.Warning, isSearch, "UpstreamHttpFailure");

            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Unable to retrieve results from GIPHY.");
        }
        catch (JsonException)
        {
            LogFailure(LogLevel.Warning, isSearch, "InvalidUpstreamResponse");

            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "GIPHY returned an invalid response.");
        }
        catch (Exception)
        {
            LogFailure(LogLevel.Error, isSearch, "UnexpectedFailure");

            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.");
        }
    }

    private void LogFailure(LogLevel level, bool isSearch, string failure)
    {
        // Log controlled fields only; exception details and request URLs may contain credentials.
        _logger.Log(
            level,
            "GIF operation {Operation} failed: {Failure}. TraceId: {TraceId}",
            isSearch ? "Search" : "Trending",
            failure,
            HttpContext.TraceIdentifier);
    }
}
