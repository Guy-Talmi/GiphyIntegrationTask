using GiphyIntegration.api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Services;

// Owns a dedicated cache so its size requirements do not affect unrelated framework entries.
public sealed class GiphyCache : IDisposable
{
    public MemoryCache Cache { get; }

    public GiphyCache(IOptions<GiphyOptions> options)
    {
        Cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = options.Value.CacheMaxEntries
        });
    }

    public void Dispose() => Cache.Dispose();
}
