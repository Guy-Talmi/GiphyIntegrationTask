using GiphyIntegration.api.Options;
using GiphyIntegration.api.Services;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Extensions;

public static class GiphyHttpClientServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IHttpClientBuilder AddGiphyHttpClient()
        {
            return services.AddHttpClient<GiphyService>((serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<GiphyOptions>>()
                    .Value;

                client.BaseAddress = new Uri(options.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(options.HttpTimeoutSeconds);
            });
        }
    }
}
