using GiphyIntegration.api.Options;
using GiphyIntegration.api.Services;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Extensions;

public static class CachedGifServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddCachedGifService()
        {
            services.AddSingleton<GiphyCache>();
            services.AddSingleton<GifCacheLocks>();

            return services.AddTransient<IGifService>(serviceProvider =>
                new CachedGifService(
                    serviceProvider.GetRequiredService<GiphyService>(),
                    serviceProvider.GetRequiredService<GiphyCache>().Cache,
                    serviceProvider.GetRequiredService<GifCacheLocks>(),
                    serviceProvider.GetRequiredService<IOptions<GiphyOptions>>()));
        }
    }
}
