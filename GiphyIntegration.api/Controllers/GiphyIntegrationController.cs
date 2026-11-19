using GiphyIntegration.api.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

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
    public Task<ActionResult<IReadOnlyList<string>>> GetTrending(CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => _gifService.GetTrendingAsync(cancellationToken),
            cancellationToken);
    }

    [HttpGet("search")]
    public Task<ActionResult<IReadOnlyList<string>>> Search(
        [FromQuery] SearchGifsRequest request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(
            () => _gifService.SearchAsync(request.SearchTerm, cancellationToken),
            cancellationToken,
            isSearch: true);
    }

    // Execute the service call inside the try block to catch immediate and asynchronous failures.
    private async Task<ActionResult<IReadOnlyList<string>>> ExecuteAsync(
        Func<Task<IReadOnlyList<string>>> action,
        CancellationToken cancellationToken,
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
                title: "The search term is required and must not exceed 50 characters.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancellation is not an upstream timeout.
            throw;
        }
        catch (OperationCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return Problem(statusCode: StatusCodes.Status504GatewayTimeout,
                title: "GIPHY did not respond in time.");
        }
        catch (HttpRequestException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "Unable to retrieve results from GIPHY.");
        }
        catch (JsonException)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway,
                title: "GIPHY returned an invalid response.");
        }
        catch (Exception)
        {
            return Problem(statusCode: StatusCodes.Status500InternalServerError,
                title: "An unexpected error occurred.");
        }
    }
}
