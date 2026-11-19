using GiphyIntegration.api.Extensions;
using GiphyIntegration.api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Tests;

public class GiphyHttpClientRegistrationTests
{
    // Resolve the client registered by the production extension and inspect its settings.
    // No HTTP request is sent, so even the maximum timeout is tested without waiting.
    [Theory]
    [InlineData(null, 10)]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(2147483, 2147483)]
    public void RegisteredClient_UsesConfiguredTimeoutAndBaseAddress(
        int? configuredSeconds, int expectedSeconds)
    {
        const string baseUrl = "https://configured-giphy.test/custom/v2/";
        var values = new Dictionary<string, string?>
        {
            ["Giphy:ApiKey"] = "test-only-key",
            ["Giphy:BaseUrl"] = baseUrl
        };
        if (configuredSeconds is { } seconds)
        {
            values["Giphy:HttpTimeoutSeconds"] = seconds.ToString();
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddGiphyOptions(configuration);
        var registration = services.AddGiphyHttpClient();
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IStartupValidator>().Validate();

        var factory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = factory.CreateClient(registration.Name);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), client.Timeout);
        Assert.Equal(new Uri(baseUrl), client.BaseAddress);
        Assert.IsType<GiphyService>(provider.GetRequiredService<GiphyService>());
    }
}
