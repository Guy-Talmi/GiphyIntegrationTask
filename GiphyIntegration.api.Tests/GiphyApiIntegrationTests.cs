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

namespace GiphyIntegration.api.Tests;

public class GiphyApiIntegrationTests
{
    private const string RoutePrefix = "/api/GiphyIntegration";
    private const string ExpectedGifUrl = "https://example.test/integration.gif";
    private const string TestApiKey = "integration-test-only-key";

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
    [InlineData(RoutePrefix + "/trending", "/v1/gifs/trending", false)]
    [InlineData(RoutePrefix + "/search?q=cats", "/v1/gifs/search", true)]
    public async Task GetEndpoints_ReturnJsonAndReuseRegisteredCache(
        string path,
        string expectedUpstreamPath,
        bool search)
    {
        using var factory = new GiphyApiFactory();
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
        Assert.Equal("25", Assert.Single(query["limit"]));
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
    // The HTTP pipeline must return 400 without reaching the fake upstream.
    [Theory]
    [MemberData(nameof(InvalidSearchPaths))]
    public async Task SearchRoute_InvalidInput_ReturnsBadRequestWithoutUpstreamCall(string path)
    {
        using var factory = new GiphyApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
    private sealed class GiphyApiFactory : WebApplicationFactory<GiphyIntegrationController>
    {
        public ConcurrentQueue<UpstreamRequest> UpstreamRequests { get; } = new();

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
            // User Secrets are not loaded in this environment. Supply only test configuration.
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Giphy:ApiKey"] = TestApiKey,
                    ["Giphy:BaseUrl"] = "https://giphy.test/v1/"
                }));

            // Keep the application's real service registrations; replace only the HTTP transport.
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<GiphyService>()
                    .ConfigurePrimaryHttpMessageHandler(() => new FakeUpstreamHandler(UpstreamRequests)));
            builder.ConfigureLogging(logging => logging.ClearProviders());
        }
    }

    private sealed record UpstreamRequest(HttpMethod Method, Uri Uri);

    // All upstream responses are created in memory; no socket or real GIPHY key is needed.
    private sealed class FakeUpstreamHandler(ConcurrentQueue<UpstreamRequest> requests) : HttpMessageHandler
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
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
