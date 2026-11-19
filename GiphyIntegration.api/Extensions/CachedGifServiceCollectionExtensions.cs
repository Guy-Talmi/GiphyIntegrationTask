using GiphyIntegration.api.Options;
using GiphyIntegration.api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Extensions;

public static class CachedGifServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCachedGifService()
        {
            return services.AddTransient<IGifService>(serviceProvider =>
                new CachedGifService(
                    serviceProvider.GetRequiredService<GiphyService>(),
                    serviceProvider.GetRequiredService<IMemoryCache>(),
                    serviceProvider.GetRequiredService<GifCacheLocks>(),
                    serviceProvider.GetRequiredService<IOptions<GiphyOptions>>()));
        }
    }
}
