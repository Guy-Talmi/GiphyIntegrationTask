using GiphyIntegration.api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Tests;

public class GiphyOptionsTests
{
    // A trailing slash preserves the version segment when resolving relative API paths.
    [Theory]
    [InlineData("https://giphy.test/v1/", true)]
    [InlineData("https://giphy.test/v1", false)]
    public void StartupValidation_BaseUrl_RequiresTrailingSlash(string baseUrl, bool isValid)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Giphy:ApiKey"] = "test-only-key",
                ["Giphy:BaseUrl"] = baseUrl
            }).Build();
        var services = new ServiceCollection();
        services.AddGiphyOptions(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Record.Exception(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        if (isValid)
        {
            Assert.Null(exception);
        }
        else
        {
            var validationException = Assert.IsType<OptionsValidationException>(exception);
            Assert.Contains(
                "Giphy:BaseUrl must be an absolute HTTPS URL with a path ending in '/'.",
                validationException.Failures);
        }
    }

    // These paths are appended to BaseUrl; they must not replace its host or embed query parameters.
    [Theory]
    [InlineData("TrendingApi", "")]
    [InlineData("SearchApi", "   ")]
    [InlineData("TrendingApi", "https://other.test/trending")]
    [InlineData("SearchApi", "//other.test/search")]
    [InlineData("TrendingApi", "/gifs/trending")]
    [InlineData("SearchApi", "gifs/search?q=cats")]
    [InlineData("TrendingApi", "gifs/trending#fragment")]
    [InlineData("SearchApi", "gifs\\search")]
    public void StartupValidation_InvalidApiPath_Fails(string setting, string path)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Giphy:ApiKey"] = "test-only-key",
                ["Giphy:BaseUrl"] = "https://giphy.test/v1/",
                [$"Giphy:{setting}"] = path
            }).Build();
        var services = new ServiceCollection();
        services.AddGiphyOptions(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains($"Giphy:{setting}"));
    }

    // Reject unusable limits and values beyond the upstream provider's supported maximum.
    [Theory]
    [InlineData("MaxSearchTermLength", -1)]
    [InlineData("MaxSearchTermLength", 0)]
    [InlineData("MaxSearchTermLength", 51)]
    [InlineData("ResultsLimit", -1)]
    [InlineData("ResultsLimit", 0)]
    [InlineData("ResultsLimit", 51)]
    [InlineData("CacheMaxEntries", 0)]
    [InlineData("CacheMaxEntries", -1)]
    public void StartupValidation_InvalidLimit_Fails(string setting, int limit)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Giphy:ApiKey"] = "test-only-key",
                ["Giphy:BaseUrl"] = "https://giphy.test/v1/",
                [$"Giphy:{setting}"] = limit.ToString()
            }).Build();
        var services = new ServiceCollection();
        services.AddGiphyOptions(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains($"Giphy:{setting}"));
    }
}
