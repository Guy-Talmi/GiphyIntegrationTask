using GiphyIntegration.api.Controllers;
using GiphyIntegration.api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GiphyIntegration.api.Tests;

public class GiphyIntegrationControllerTests
{
    public static TheoryData<string> ValidSearchTerms => new()
    {
        "cats",
        "tom & jerry",
        " cats ",
        new string('a', 50)
    };

    // Calls the controller directly and verifies its HTTP result and service interaction.
    // The caller's cancellation token must reach the trending service unchanged.
    [Fact]
    public async Task GetTrending_ReturnsOkWithServiceResultsAndForwardsCancellation()
    {
        string[] expectedUrls =
        {
            "https://example.test/trending-first.gif",
            "https://example.test/trending-second.gif"
        };
        var service = new RecordingGifService(expectedUrls);
        var controller = new GiphyIntegrationController(service);
        using var cancellation = new CancellationTokenSource();

        var action = await controller.GetTrending(cancellation.Token);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var urls = Assert.IsAssignableFrom<IReadOnlyList<string>>(response.Value);
        Assert.Equal<string>(expectedUrls, urls);
        Assert.Equal(1, service.TrendingCallCount);
        Assert.Equal(0, service.SearchCallCount);
        Assert.Equal(cancellation.Token, service.TrendingCancellationToken);
    }

    // Covers ordinary text, special characters, surrounding spaces, and the accepted 50-character boundary.
    // The controller must pass the original search text and cancellation token to the service.
    [Theory]
    [MemberData(nameof(ValidSearchTerms))]
    public async Task Search_ValidTerm_ReturnsOkAndForwardsArguments(string searchTerm)
    {
        string[] expectedUrls =
        {
            "https://example.test/search-first.gif",
            "https://example.test/search-second.gif"
        };
        var service = new RecordingGifService(expectedUrls);
        var controller = new GiphyIntegrationController(service);
        using var cancellation = new CancellationTokenSource();

        var action = await controller.Search(new SearchGifsRequest { SearchTerm = searchTerm }, cancellation.Token);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        var urls = Assert.IsAssignableFrom<IReadOnlyList<string>>(response.Value);
        Assert.Equal<string>(expectedUrls, urls);
        Assert.Equal(1, service.SearchCallCount);
        Assert.Equal(0, service.TrendingCallCount);
        Assert.Equal(searchTerm, service.SearchTerm);
        Assert.Equal(cancellation.Token, service.SearchCancellationToken);
    }

    // Finding no GIFs is still a successful request: both actions return 200 with an empty list.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actions_EmptyServiceResult_ReturnOkWithEmptyList(bool trending)
    {
        var service = new RecordingGifService(Array.Empty<string>());
        var controller = new GiphyIntegrationController(service);

        var action = trending
            ? await controller.GetTrending(CancellationToken.None)
            : await controller.Search(new SearchGifsRequest { SearchTerm = "cats" }, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(response.Value));
        Assert.Equal(trending ? 1 : 0, service.TrendingCallCount);
        Assert.Equal(trending ? 0 : 1, service.SearchCallCount);
    }

    // Verifies both actions map failures without returning exception messages or stack traces.
    [Theory]
    [InlineData(true, "http", 502)]
    [InlineData(false, "http", 502)]
    [InlineData(true, "json", 502)]
    [InlineData(false, "json", 502)]
    [InlineData(true, "timeout", 504)]
    [InlineData(false, "timeout", 504)]
    [InlineData(true, "unexpected", 500)]
    [InlineData(false, "unexpected", 500)]
    [InlineData(true, "unknownCancellation", 500)]
    [InlineData(false, "unknownCancellation", 500)]
    [InlineData(true, "argument", 500)]
    [InlineData(false, "argument", 400)]
    [InlineData(false, "internalArgument", 500)]
    public async Task Actions_ServiceFailure_ReturnSafeProblemDetails(
        bool trending, string failure, int expectedStatus)
    {
        const string sensitiveMessage = "Private diagnostic: api_key=secret-test-value";
        Exception exception = failure switch
        {
            "http" => new HttpRequestException(sensitiveMessage),
            "json" => new System.Text.Json.JsonException(sensitiveMessage),
            "timeout" => new TaskCanceledException(sensitiveMessage, new TimeoutException(sensitiveMessage)),
            "unknownCancellation" => new OperationCanceledException(sensitiveMessage),
            "argument" => new ArgumentException(sensitiveMessage, "searchTerm"),
            "internalArgument" => new ArgumentException(sensitiveMessage, "internalSetting"),
            _ => new InvalidOperationException(sensitiveMessage)
        };
        var controller = new GiphyIntegrationController(new FailingGifService(exception))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var action = trending
            ? await controller.GetTrending(CancellationToken.None)
            : await controller.Search(new SearchGifsRequest { SearchTerm = "cats" }, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(expectedStatus, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(expectedStatus, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));
        Assert.Null(problem.Detail);
        var json = System.Text.Json.JsonSerializer.Serialize(problem);
        Assert.DoesNotContain("secret-test-value", json);
        Assert.DoesNotContain("Private diagnostic", json);
        Assert.DoesNotContain("StackTrace", json);
    }

    // Client cancellation must propagate, even when the exception also contains a timeout.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Actions_CallerCancelled_PropagateCancellation(bool trending, bool hasTimeout)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var exception = new OperationCanceledException("Cancelled",
            hasTimeout ? new TimeoutException() : null, cancellation.Token);
        var controller = new GiphyIntegrationController(new FailingGifService(exception));

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trending
            ? controller.GetTrending(cancellation.Token)
            : controller.Search(new SearchGifsRequest { SearchTerm = "cats" }, cancellation.Token));

        Assert.Same(exception, thrown);
    }

    // Argument guards throw synchronously; transport failures arrive through a faulted task.
    private sealed class FailingGifService(Exception exception) : IGifService
    {
        public Task<IReadOnlyList<string>> GetTrendingAsync(CancellationToken cancellationToken = default)
            => Fail();

        public Task<IReadOnlyList<string>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
            => Fail();

        private Task<IReadOnlyList<string>> Fail()
        {
            if (exception is ArgumentException)
            {
                throw exception;
            }

            return Task.FromException<IReadOnlyList<string>>(exception);
        }
    }

    // Records which operation and arguments the controller uses; no HTTP or GIPHY calls are made.
    private sealed class RecordingGifService(IReadOnlyList<string> urls) : IGifService
    {
        public int TrendingCallCount { get; private set; }
        public int SearchCallCount { get; private set; }
        public string? SearchTerm { get; private set; }
        public CancellationToken TrendingCancellationToken { get; private set; }
        public CancellationToken SearchCancellationToken { get; private set; }

        public Task<IReadOnlyList<string>> GetTrendingAsync(CancellationToken cancellationToken = default)
        {
            TrendingCallCount++;
            TrendingCancellationToken = cancellationToken;
            return Task.FromResult(urls);
        }

        public Task<IReadOnlyList<string>> SearchAsync(
            string searchTerm,
            CancellationToken cancellationToken = default)
        {
            SearchCallCount++;
            SearchTerm = searchTerm;
            SearchCancellationToken = cancellationToken;
            return Task.FromResult(urls);
        }
    }
}
