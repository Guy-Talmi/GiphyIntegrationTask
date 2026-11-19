using System.Net;
using System.Text;
using System.Text.Json;
using GiphyIntegration.api.Options;
using GiphyIntegration.api.Services;
using Microsoft.AspNetCore.WebUtilities;

namespace GiphyIntegration.api.Tests;

public class GiphyServiceTests
{
    private const string BaseAddress = "https://api.giphy.test/v1/";
    private const string ApiKey = "test-key+&=";
    private const string EmptyResponse = "{\"data\":[]}";

    // Captures the outgoing request before any network activity could occur.
    // Verifies GET, the complete endpoint path, and the exact required query parameters.
    [Theory]
    [InlineData(true, "/v1/gifs/trending", 1)]
    [InlineData(false, "/v1/gifs/search", 1)]
    [InlineData(true, "/v1/gifs/trending", 50)]
    [InlineData(false, "/v1/gifs/search", 50)]
    public async Task FetchAsync_UsesCorrectEndpointAndParameters(bool trending, string expectedPath, int resultsLimit)
    {
        Uri? capturedUri = null;
        HttpMethod? capturedMethod = null;
        var requests = 0;
        using var client = CreateClient((request, _) =>
        {
            capturedUri = request.RequestUri;
            capturedMethod = request.Method;
            requests++;
            return Task.FromResult(JsonResponse(EmptyResponse));
        });
        var service = CreateService(client, resultsLimit: resultsLimit);

        await FetchAsync(service, trending);

        Assert.Equal(1, requests);
        Assert.Equal(HttpMethod.Get, capturedMethod);
        Assert.NotNull(capturedUri);
        Assert.Equal("https", capturedUri.Scheme);
        Assert.Equal("api.giphy.test", capturedUri.Host);
        Assert.Equal(expectedPath, capturedUri.AbsolutePath);

        var query = QueryHelpers.ParseQuery(capturedUri.Query);
        Assert.Equal(ApiKey, Assert.Single(query["api_key"]));
        Assert.Equal(resultsLimit.ToString(), Assert.Single(query["limit"]));
        Assert.Equal(trending ? 2 : 3, query.Count);

        if (trending)
        {
            Assert.False(query.ContainsKey("q"));
        }
        else
        {
            Assert.Equal("cats", Assert.Single(query["q"]));
        }
    }

    // Decodes the captured query to verify that the original text survives exactly once.
    // Ampersands must not create extra parameters; plus and percent signs must remain literal text.
    [Theory]
    [InlineData("funny cats")]
    [InlineData("tom & jerry")]
    [InlineData("cats")]
    [InlineData("cats + dogs 100%")]
    public async Task SearchAsync_EncodesSearchTextWithoutChangingIt(string searchTerm)
    {
        Uri? capturedUri = null;
        using var client = CreateClient((request, _) =>
        {
            capturedUri = request.RequestUri;
            return Task.FromResult(JsonResponse(EmptyResponse));
        });
        var service = CreateService(client);

        await service.SearchAsync(searchTerm);

        Assert.NotNull(capturedUri);
        var query = QueryHelpers.ParseQuery(capturedUri.Query);
        Assert.Equal(3, query.Count);
        Assert.Equal(searchTerm, Assert.Single(query["q"]));
        Assert.Equal(ApiKey, Assert.Single(query["api_key"]));
        Assert.Equal("25", Assert.Single(query["limit"]));
        Assert.Equal(string.Empty, capturedUri.Fragment);
    }

    // Verifies that the service selects original image URLs rather than page or preview URLs,
    // preserves their query strings, and returns the GIFs in their original response order.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FetchAsync_ValidJson_ReturnsOriginalImageUrls(bool trending)
    {
        const string json = """
            {
              "data": [
                {
                  "id": "first",
                  "url": "https://example.test/page/first",
                  "images": {
                    "fixed_height": { "url": "https://example.test/preview.gif" },
                    "original": { "url": "https://example.test/first.gif?cid=test&rid=original" }
                  }
                },
                {
                  "id": "second",
                  "images": {
                    "original": { "url": "https://example.test/second.gif" }
                  }
                }
              ],
              "pagination": { "count": 2, "offset": 0 },
              "meta": { "status": 200 }
            }
            """;
        using var client = CreateClient((_, _) => Task.FromResult(JsonResponse(json)));
        var service = CreateService(client);

        var urls = await FetchAsync(service, trending);

        Assert.Equal(new[]
        {
            "https://example.test/first.gif?cid=test&rid=original",
            "https://example.test/second.gif"
        }, urls.ToArray());
    }

    // A valid, empty result set is a successful response rather than a parsing failure.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FetchAsync_EmptyData_ReturnsEmptyList(bool trending)
    {
        using var client = CreateClient((_, _) => Task.FromResult(JsonResponse(EmptyResponse)));
        var service = CreateService(client);

        var urls = await FetchAsync(service, trending);

        Assert.Empty(urls);
    }

    // Both operations use the same parser. These cases exercise its invalid response shapes,
    // distinguishing missing or malformed data from the valid empty array tested above.
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[{}]}")]
    [InlineData("{\"data\":[{\"images\":{}}]}")]
    [InlineData("{\"data\":[{\"images\":{\"original\":{}}}]}")]
    [InlineData("{\"data\":[{\"images\":{\"original\":{\"url\":null}}}]}")]
    [InlineData("{\"data\":[{\"images\":{\"original\":{\"url\":\"\"}}}]}")]
    [InlineData("{\"data\":[{\"images\":{\"original\":{\"url\":\"   \"}}}]}")]
    public async Task FetchAsync_InvalidJsonOrMissingFields_ThrowsJsonException(string json)
    {
        using var client = CreateClient((_, _) => Task.FromResult(JsonResponse(json)));
        var service = CreateService(client);

        await Assert.ThrowsAnyAsync<JsonException>(() => service.GetTrendingAsync());
    }

    // HTTP failure status codes must propagate instead of becoming empty successful results.
    // The non-JSON error body also verifies that status is checked before JSON parsing.
    [Theory]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    [InlineData(true, HttpStatusCode.TooManyRequests)]
    [InlineData(false, HttpStatusCode.TooManyRequests)]
    [InlineData(true, HttpStatusCode.InternalServerError)]
    [InlineData(false, HttpStatusCode.InternalServerError)]
    [InlineData(true, HttpStatusCode.ServiceUnavailable)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task FetchAsync_UnsuccessfulHttpStatus_PropagatesStatus(
        bool trending,
        HttpStatusCode statusCode)
    {
        using var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("Simulated upstream error")
        }));
        var service = CreateService(client);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => FetchAsync(service, trending));

        Assert.Equal(statusCode, exception.StatusCode);
    }

    // Simulates a transport failure before any HTTP response exists.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FetchAsync_NetworkFailure_PropagatesException(bool trending)
    {
        var failure = new HttpRequestException("Simulated network failure.");
        using var client = CreateClient((_, _) => Task.FromException<HttpResponseMessage>(failure));
        var service = CreateService(client);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => FetchAsync(service, trending));

        Assert.Same(failure, exception);
    }

    // Keeps the fake transport pending until HttpClient's actual timeout cancels it.
    // The outer five-second limit is only a watchdog; it cannot satisfy the expected exception.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FetchAsync_HttpClientTimeout_PropagatesTimeoutCancellation(bool trending)
    {
        CancellationToken transportToken = default;
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            transportToken = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(EmptyResponse);
        });
        client.Timeout = TimeSpan.FromMilliseconds(250);
        var service = CreateService(client);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => FetchAsync(service, trending).WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.True(transportToken.IsCancellationRequested);
    }

    // Cancels only after the request reaches the fake transport, proving cancellation
    // propagates through the service and HttpClient to an already-pending operation.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FetchAsync_CallerCancels_PropagatesCancellationToTransport(bool trending)
    {
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken transportToken = default;
        using var client = CreateClient(async (_, cancellationToken) =>
        {
            transportToken = cancellationToken;
            started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(EmptyResponse);
        });
        using var callerCancellation = new CancellationTokenSource();
        var service = CreateService(client);

        var pending = FetchAsync(service, trending, callerCancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callerCancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.True(transportToken.IsCancellationRequested);
        Assert.False(exception.InnerException is TimeoutException);
    }

    // Enforces the configured limit even when callers bypass the HTTP request model.
    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    public async Task SearchAsync_ConfiguredLengthLimit_RejectsOversizedInputBeforeHttp(int limit)
    {
        var calls = 0;
        using var client = CreateClient((_, _) =>
        {
            calls++;
            return Task.FromResult(JsonResponse(EmptyResponse));
        });
        var service = CreateService(client, limit);

        await service.SearchAsync(new string('a', limit));
        var exception = Assert.Throws<ArgumentException>(
            () => { _ = service.SearchAsync(new string('a', limit + 1)); });

        Assert.Equal("searchTerm", exception.ParamName);
        Assert.Contains($"must not exceed {limit} characters", exception.Message);
        Assert.Equal(1, calls);
    }

    private static Task<IReadOnlyList<string>> FetchAsync(
        GiphyService service,
        bool trending,
        CancellationToken cancellationToken = default) => trending
        ? service.GetTrendingAsync(cancellationToken)
        : service.SearchAsync("cats", cancellationToken);

    private static GiphyService CreateService(HttpClient client, int maxSearchTermLength = 50,
        int resultsLimit = 25) => new(
        client,
        Microsoft.Extensions.Options.Options.Create(new GiphyOptions
        {
            BaseUrl = BaseAddress,
            ApiKey = ApiKey,
            MaxSearchTermLength = maxSearchTermLength,
            ResultsLimit = resultsLimit
        }));

    private static HttpClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => new(
        new StubHttpMessageHandler(send))
        {
            BaseAddress = new Uri(BaseAddress)
        };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    // Replaces HttpClient's network transport; all responses and failures are generated in memory.
    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
