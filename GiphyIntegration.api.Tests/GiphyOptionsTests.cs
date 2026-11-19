using GiphyIntegration.api.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Tests;

public class GiphyOptionsTests
{
    // Missing configuration and blank values must fail before any upstream request is attempted.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void StartupValidation_MissingOrBlankApiKey_Fails(string? apiKey)
    {
        using var provider = CreateProviderWithSetting("ApiKey", apiKey);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Equal(new[] { "Giphy:ApiKey is required." }, exception.Failures);
    }

    // Base addresses must exist and be absolute HTTPS URLs, not relative paths or other schemes.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-url")]
    [InlineData("/v1/")]
    [InlineData("http://giphy.test/v1/")]
    [InlineData("ftp://giphy.test/v1/")]
    [InlineData("https://")]
    public void StartupValidation_InvalidBaseUrl_Fails(string? baseUrl)
    {
        using var provider = CreateProviderWithSetting("BaseUrl", baseUrl);

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Equal(new[]
        {
            "Giphy:BaseUrl must be an absolute HTTPS URL with a path ending in '/'."
        }, exception.Failures);
    }

    // Valid boundaries must not be rejected by an off-by-one error in startup validation.
    [Theory]
    [InlineData("TrendingCacheMinutes", 1)]
    [InlineData("SearchCacheMinutes", 1)]
    [InlineData("HttpTimeoutSeconds", 1)]
    [InlineData("HttpTimeoutSeconds", 2147483)]
    [InlineData("MaxSearchTermLength", 1)]
    [InlineData("MaxSearchTermLength", 50)]
    [InlineData("ResultsLimit", 1)]
    [InlineData("ResultsLimit", 50)]
    [InlineData("CacheMaxEntries", 1)]
    public void StartupValidation_ValidNumericBoundary_Succeeds(string setting, int value)
    {
        using var provider = CreateProviderWithSetting(setting, value.ToString());

        provider.GetRequiredService<IStartupValidator>().Validate();
    }

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
    [InlineData("TrendingApi", "../gifs/trending")]
    [InlineData("SearchApi", "../gifs/search")]
    [InlineData("TrendingApi", "gifs/../trending")]
    [InlineData("SearchApi", "gifs/search/..")]
    [InlineData("TrendingApi", "%2e%2e/gifs/trending")]
    [InlineData("SearchApi", ".%2E/gifs/search")]
    [InlineData("SearchApi", "%2E./gifs/search")]
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

    // Dots inside a segment's name are valid; only complete parent-directory segments are rejected.
    [Theory]
    [InlineData("TrendingApi", "gifs/trending..preview")]
    [InlineData("SearchApi", "gifs/search.v2")]
    public void StartupValidation_ApiPathWithDotsInName_Succeeds(string setting, string path)
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

        provider.GetRequiredService<IStartupValidator>().Validate();
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
    [InlineData("TrendingCacheMinutes", 0)]
    [InlineData("TrendingCacheMinutes", -1)]
    [InlineData("SearchCacheMinutes", 0)]
    [InlineData("SearchCacheMinutes", -1)]
    [InlineData("HttpTimeoutSeconds", 0)]
    [InlineData("HttpTimeoutSeconds", -1)]
    [InlineData("HttpTimeoutSeconds", 2147484)]
    [InlineData("HttpTimeoutSeconds", int.MaxValue)]
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

    private static ServiceProvider CreateProviderWithSetting(string setting, string? value)
    {
        var values = new Dictionary<string, string?>
        {
            ["Giphy:ApiKey"] = "test-only-key",
            ["Giphy:BaseUrl"] = "https://giphy.test/v1/"
        };

        if (value is null)
        {
            values.Remove($"Giphy:{setting}");
        }
        else
        {
            values[$"Giphy:{setting}"] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddGiphyOptions(configuration);
        return services.BuildServiceProvider();
    }
}
