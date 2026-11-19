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

        var action = await controller.Search(searchTerm, cancellation.Token);

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
            : await controller.Search("cats", CancellationToken.None);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(response.Value));
        Assert.Equal(trending ? 1 : 0, service.TrendingCallCount);
        Assert.Equal(trending ? 0 : 1, service.SearchCallCount);
    }

    // Null represents missing input at the action boundary; empty and whitespace values are also invalid.
    // Rejecting input must happen before either service operation is called.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public async Task Search_MissingOrWhitespaceTerm_ReturnsBadRequestWithoutCallingService(string? searchTerm)
    {
        var service = new RecordingGifService(Array.Empty<string>());
        var controller = new GiphyIntegrationController(service);

        var action = await controller.Search(searchTerm, CancellationToken.None);

        var response = Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal(0, service.SearchCallCount);
        Assert.Equal(0, service.TrendingCallCount);
    }

    // Tests the first rejected length (51), paired with the accepted length (50) above.
    [Fact]
    public async Task Search_TermLongerThan50Characters_ReturnsBadRequestWithoutCallingService()
    {
        var service = new RecordingGifService(Array.Empty<string>());
        var controller = new GiphyIntegrationController(service);
        var searchTerm = new string('a', 51);

        var action = await controller.Search(searchTerm, CancellationToken.None);

        var response = Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, response.StatusCode);
        Assert.Equal(0, service.SearchCallCount);
        Assert.Equal(0, service.TrendingCallCount);
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
