using GiphyIntegration.api.Controllers;
using GiphyIntegration.api.Services;
using GiphyIntegration.api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        var controller = new GiphyIntegrationController(service, NullLogger<GiphyIntegrationController>.Instance);
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

    // Covers forwarding ordinary text, special characters, surrounding spaces, and a long search term.
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
        var controller = new GiphyIntegrationController(service, NullLogger<GiphyIntegrationController>.Instance);
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
        var logger = new RecordingLogger();
        var controller = new GiphyIntegrationController(service, logger);

        var action = trending
            ? await controller.GetTrending(CancellationToken.None)
            : await controller.Search(new SearchGifsRequest { SearchTerm = "cats" }, CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(response.Value));
        Assert.Equal(trending ? 1 : 0, service.TrendingCallCount);
        Assert.Equal(trending ? 0 : 1, service.SearchCallCount);
        Assert.Empty(logger.Entries);
    }

    // Verifies safe responses and structured logs for both actions, without leaking diagnostic details.
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
    public async Task Actions_ServiceFailure_ReturnSafeProblemDetailsAndLogs(
        bool trending, string failure, int expectedStatus)
    {
        const string sensitiveMessage = "Private diagnostic: https://giphy.test/v1/gifs/search?api_key=secret-test-value";
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
        var logger = new RecordingLogger();
        var context = new DefaultHttpContext { TraceIdentifier = "test-request-trace" };
        context.Request.Path = "/api/GiphyIntegration/search";
        context.Request.QueryString = new QueryString("?q=private-search-term&api_key=secret-test-value");
        var controller = new GiphyIntegrationController(new FailingGifService(exception), logger)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        var action = trending
            ? await controller.GetTrending(CancellationToken.None)
            : await controller.Search(new SearchGifsRequest { SearchTerm = "private-search-term" }, CancellationToken.None);

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

        if (expectedStatus == StatusCodes.Status400BadRequest)
        {
            Assert.Empty(logger.Entries);
            return;
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(expectedStatus == 500 ? LogLevel.Error : LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal(4, entry.Fields.Count);
        Assert.Equal(trending ? "Trending" : "Search", entry.Fields["Operation"]);
        Assert.Equal("test-request-trace", entry.Fields["TraceId"]);
        Assert.Equal(failure switch
        {
            "http" => "UpstreamHttpFailure",
            "json" => "InvalidUpstreamResponse",
            "timeout" => "UpstreamTimeout",
            _ => "UnexpectedFailure"
        }, entry.Fields["Failure"]);
        Assert.Equal(
            "GIF operation {Operation} failed: {Failure}. TraceId: {TraceId}",
            entry.Fields["{OriginalFormat}"]);

        var logContents = entry.Message + System.Text.Json.JsonSerializer.Serialize(entry.Fields);
        Assert.DoesNotContain("secret-test-value", logContents);
        Assert.DoesNotContain("private-search-term", logContents);
        Assert.DoesNotContain("Private diagnostic", logContents);
        Assert.DoesNotContain("https://giphy.test", logContents);
        Assert.DoesNotContain("/api/GiphyIntegration/search", logContents);
    }

    // Client cancellation propagates without logging a failure, even with an inner timeout.
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
        var logger = new RecordingLogger();
        var controller = new GiphyIntegrationController(new FailingGifService(exception), logger);

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trending
            ? controller.GetTrending(cancellation.Token)
            : controller.Search(new SearchGifsRequest { SearchTerm = "cats" }, cancellation.Token));

        Assert.Same(exception, thrown);
        Assert.Empty(logger.Entries);
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception,
        IReadOnlyDictionary<string, object?> Fields);

    private sealed class RecordingLogger : ILogger<GiphyIntegrationController>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = Assert.IsAssignableFrom<IEnumerable<KeyValuePair<string, object?>>>(state);
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception,
                fields.ToDictionary(field => field.Key, field => field.Value)));
        }
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
