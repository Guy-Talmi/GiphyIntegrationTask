using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using GiphyIntegration.api.Controllers;
using GiphyIntegration.api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace GiphyIntegration.api.Tests;

public class GiphyApiIntegrationTests
{
    private const string RoutePrefix = "/api/GiphyIntegration";
    private const string ExpectedGifUrl = "https://example.test/integration.gif";
    private const string TestApiKey = "integration-test-only-key";

    // Configuration controls the registered singleton cache, while overflow still returns HTTP 200.
    // Unrelated IMemoryCache users must remain free to store entries without specifying a size.
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task GetEndpoints_FullConfiguredCache_StillReturnResults(int capacity)
    {
        using var factory = new GiphyApiFactory(cacheMaxEntries: capacity)
        {
            RegisterUnrelatedCache = true
        };
        using var client = factory.CreateHttpsClient();
        var cache = factory.Services.GetRequiredService<GiphyCache>();
        Assert.Same(cache, factory.Services.GetRequiredService<GiphyCache>());

        using var trending = await client.GetAsync(RoutePrefix + "/trending");
        await AssertGifResponseAsync(trending);
        using var cachedTrending = await client.GetAsync(RoutePrefix + "/trending");
        await AssertGifResponseAsync(cachedTrending);
        Assert.Single(factory.UpstreamRequests);

        for (var index = 1; index < capacity; index++)
        {
            using var response = await client.GetAsync(RoutePrefix + $"/search?q=fill-{index}");
            await AssertGifResponseAsync(response);
        }
        Assert.Equal(capacity, cache.Cache.Count);

        for (var index = 0; index < 5; index++)
        {
            using var response = await client.GetAsync(RoutePrefix + $"/search?q=overflow-{index}");
            await AssertGifResponseAsync(response);
            Assert.InRange(cache.Cache.Count, 0, capacity);
        }
        Assert.Equal(capacity + 5, factory.UpstreamRequests.Count);

        var sharedCache = factory.Services.GetRequiredService<IMemoryCache>();
        Assert.NotSame(sharedCache, cache.Cache);
        sharedCache.Set("unrelated-entry", "value");
        Assert.Equal("value", sharedCache.Get<string>("unrelated-entry"));
    }

    public static TheoryData<string> InvalidSearchPaths => new()
    {
        RoutePrefix + "/search",
        RoutePrefix + "/search?q=",
        RoutePrefix + "/search?q=%20%20",
        RoutePrefix + "/search?q=%09%0D%0A",
        RoutePrefix + "/search?q=" + new string('a', 51)
    };

    // Runs the real application pipeline, including routing, DI, controller, cache, and GiphyService.
    // Repeating the HTTP request must return JSON while making only one fake upstream request.
    [Theory]
    [InlineData(RoutePrefix + "/trending", "/v1/custom/trending", false)]
    [InlineData(RoutePrefix + "/search?q=cats", "/v1/custom/search", true)]
    public async Task GetEndpoints_ReturnJsonAndReuseRegisteredCache(
        string path,
        string expectedUpstreamPath,
        bool search)
    {
        using var factory = new GiphyApiFactory(resultsLimit: 7,
            trendingApi: "custom/trending", searchApi: "custom/search");
        using var client = factory.CreateHttpsClient();

        using var first = await client.GetAsync(path);
        using var second = await client.GetAsync(path);

        await AssertGifResponseAsync(first);
        await AssertGifResponseAsync(second);

        var upstream = Assert.Single(factory.UpstreamRequests);
        Assert.Equal(HttpMethod.Get, upstream.Method);
        Assert.Equal("giphy.test", upstream.Uri.Host);
        Assert.Equal(expectedUpstreamPath, upstream.Uri.AbsolutePath);

        var query = QueryHelpers.ParseQuery(upstream.Uri.Query);
        Assert.Equal(TestApiKey, Assert.Single(query["api_key"]));
        Assert.Equal("7", Assert.Single(query["limit"]));
        Assert.Equal(search ? 3 : 2, query.Count);
        if (search)
        {
            Assert.Equal("cats", Assert.Single(query["q"]));
        }
        else
        {
            Assert.False(query.ContainsKey("q"));
        }
    }

    // Exercises actual HTTP query binding, followed by the service's outgoing query encoding.
    // The fake upstream must receive the original text, without extra parameters or double encoding.
    [Theory]
    [InlineData("funny cats")]
    [InlineData("tom & jerry")]
    [InlineData("cats")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task SearchRoute_BindsEncodedQueryAndForwardsOriginalText(string searchTerm)
    {
        using var factory = new GiphyApiFactory();
        using var client = factory.CreateHttpsClient();
        var path = RoutePrefix + "/search?q=" + Uri.EscapeDataString(searchTerm);

        using var response = await client.GetAsync(path);

        await AssertGifResponseAsync(response);
        var upstream = Assert.Single(factory.UpstreamRequests);
        Assert.Equal("/v1/gifs/search", upstream.Uri.AbsolutePath);
        var query = QueryHelpers.ParseQuery(upstream.Uri.Query);
        Assert.Equal(searchTerm, Assert.Single(query["q"]));
        Assert.Equal(3, query.Count);
    }

    // Sends invalid queries through MVC rather than directly calling the action method.
    // Automatic model validation must return structured errors without reaching the fake upstream.
    [Theory]
    [MemberData(nameof(InvalidSearchPaths))]
    public async Task SearchRoute_InvalidInput_ReturnsBadRequestWithoutUpstreamCall(string path)
    {
        using var factory = new GiphyApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.NotEmpty(problem.Errors);
        Assert.Empty(factory.UpstreamRequests);
    }

    // Verifies the real routing attributes restrict these endpoints to GET requests.
    [Theory]
    [InlineData(RoutePrefix + "/trending")]
    [InlineData(RoutePrefix + "/search?q=cats")]
    public async Task PostToGetEndpoint_ReturnsMethodNotAllowed(string path)
    {
        using var factory = new GiphyApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(path, content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Empty(factory.UpstreamRequests);
    }

    // Ensures an unmatched path is rejected by routing without calling the GIF service upstream.
    [Fact]
    public async Task UnknownRoute_ReturnsNotFoundWithoutUpstreamCall()
    {
        using var factory = new GiphyApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/not-a-giphy-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.UpstreamRequests);
    }

    // Verifies real upstream HTTP failures become safe, serialized ProblemDetails responses.
    [Theory]
    [InlineData("/trending")]
    [InlineData("/search?q=cats")]
    public async Task GetEndpoints_UpstreamFailure_ReturnsBadGatewayProblem(string path)
    {
        using var factory = new GiphyApiFactory(HttpStatusCode.ServiceUnavailable);
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(RoutePrefix + path);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(502, problem.Status);
        Assert.Equal("Unable to retrieve results from GIPHY.", problem.Title);
        Assert.Null(problem.Detail);
        Assert.DoesNotContain(TestApiKey, await response.Content.ReadAsStringAsync());
        Assert.Single(factory.UpstreamRequests);
    }

    // Configuration must govern real MVC validation, not just direct service calls.
    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    public async Task SearchRoute_ConfiguredLimit_AcceptsBoundaryAndRejectsLongerInput(int limit)
    {
        using var factory = new GiphyApiFactory(maxSearchTermLength: limit);
        using var client = factory.CreateHttpsClient();

        using var accepted = await client.GetAsync(RoutePrefix + "/search?q=" + new string('a', limit));
        await AssertGifResponseAsync(accepted);
        using var rejected = await client.GetAsync(RoutePrefix + "/search?q=" + new string('a', limit + 1));

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var problem = await rejected.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains($"The search term must not exceed {limit} characters.",
            problem.Errors.Values.SelectMany(errors => errors));
        Assert.Single(factory.UpstreamRequests);
    }

    // Verify Swagger, MVC validation, and both upstream requests against the same configuration.
    [Theory]
    [InlineData(15, 7)]
    [InlineData(30, 12)]
    public async Task OpenApi_ConfiguredLimits_MatchHttpBehavior(int maxSearchTermLength, int resultsLimit)
    {
        using var factory = new GiphyApiFactory(maxSearchTermLength: maxSearchTermLength,
            environment: "Development", resultsLimit: resultsLimit);
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var parameter = paths.GetProperty(RoutePrefix + "/search")
            .GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Single(parameter => parameter.GetProperty("name").GetString() == "q");

        Assert.True(parameter.GetProperty("required").GetBoolean());
        Assert.Equal(1, parameter.GetProperty("schema").GetProperty("minLength").GetInt32());
        Assert.Equal(maxSearchTermLength, parameter.GetProperty("schema").GetProperty("maxLength").GetInt32());
        Assert.Contains($"1 to {maxSearchTermLength} characters", parameter.GetProperty("description").GetString());
        foreach (var endpoint in new[] { "/search", "/trending" })
        {
            Assert.Contains($"{resultsLimit} GIF URLs per request",
                paths.GetProperty(RoutePrefix + endpoint).GetProperty("get").GetProperty("description").GetString());
        }
        Assert.Empty(factory.UpstreamRequests);

        using var accepted = await client.GetAsync(RoutePrefix + "/search?q=" + new string('a', maxSearchTermLength));
        await AssertGifResponseAsync(accepted);
        using var rejected = await client.GetAsync(RoutePrefix + "/search?q=" + new string('a', maxSearchTermLength + 1));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Single(factory.UpstreamRequests);
        using var trending = await client.GetAsync(RoutePrefix + "/trending");
        await AssertGifResponseAsync(trending);

        Assert.Equal(2, factory.UpstreamRequests.Count);
        foreach (var upstream in factory.UpstreamRequests)
        {
            var query = QueryHelpers.ParseQuery(upstream.Uri.Query);
            Assert.Equal(resultsLimit.ToString(), Assert.Single(query["limit"]));
        }
    }

    private static async Task AssertGifResponseAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var urls = await response.Content.ReadFromJsonAsync<string[]>();
        Assert.NotNull(urls);
        Assert.Equal(ExpectedGifUrl, Assert.Single(urls));
    }

    // Any public type from the API assembly can identify its entry-point assembly.
    // Using the controller avoids changing production Program.cs just to expose Program to tests.
    private sealed class GiphyApiFactory(HttpStatusCode upstreamStatus = HttpStatusCode.OK,
        int maxSearchTermLength = 50, string environment = "Testing", int resultsLimit = 25,
        string trendingApi = "gifs/trending", string searchApi = "gifs/search", int cacheMaxEntries = 1000)
        : WebApplicationFactory<GiphyIntegrationController>
    {
        public ConcurrentQueue<UpstreamRequest> UpstreamRequests { get; } = new();

        public bool RegisterUnrelatedCache { get; init; }

        public HttpClient CreateHttpsClient()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
            client.Timeout = TimeSpan.FromSeconds(5);
            return client;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Override external configuration with deterministic test values.
            builder.UseEnvironment(environment);
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Giphy:ApiKey"] = TestApiKey,
                    ["Giphy:BaseUrl"] = "https://giphy.test/v1/",
                    ["Giphy:MaxSearchTermLength"] = maxSearchTermLength.ToString(),
                    ["Giphy:ResultsLimit"] = resultsLimit.ToString(),
                    ["Giphy:TrendingApi"] = trendingApi,
                    ["Giphy:SearchApi"] = searchApi,
                    ["Giphy:CacheMaxEntries"] = cacheMaxEntries.ToString()
                }));

            // Only the cache-isolation scenario adds a separate, general-purpose cache.
            if (RegisterUnrelatedCache)
            {
                builder.ConfigureTestServices(services => services.AddMemoryCache());
            }

            // Keep the application's real service registrations; replace only the HTTP transport.
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<GiphyService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeUpstreamHandler(UpstreamRequests, upstreamStatus)));
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }

    private sealed record UpstreamRequest(HttpMethod Method, Uri Uri);

    // All upstream responses are created in memory; no socket or real GIPHY key is needed.
    private sealed class FakeUpstreamHandler(ConcurrentQueue<UpstreamRequest> requests,
        HttpStatusCode upstreamStatus) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            requests.Enqueue(new UpstreamRequest(request.Method, request.RequestUri!));

            const string json = """
                {"data":[{"images":{"original":{"url":"https://example.test/integration.gif"}}}]}
                """;
            return Task.FromResult(new HttpResponseMessage(upstreamStatus)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
