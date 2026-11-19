using GiphyIntegration.api.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GiphyIntegration.api.Services;

public sealed class CachedGifService : IGifService
{
    private readonly IGifService _inner;
    private readonly IMemoryCache _cache;
    private readonly GifCacheLocks _locks;
    private readonly GiphyOptions _options;

    public CachedGifService(IGifService inner, IMemoryCache cache, GifCacheLocks locks,
                            IOptions<GiphyOptions> options)
    {
        _inner = inner;
        _cache = cache;
        _locks = locks;
        _options = options.Value;
    }

    public Task<IReadOnlyList<string>> GetTrendingAsync(CancellationToken cancellationToken = default)
    {
        return GetOrFetchAsync("giphy:trending", TimeSpan.FromMinutes(_options.TrendingCacheMinutes), _inner.GetTrendingAsync,
                                cancellationToken);
    }

    public Task<IReadOnlyList<string>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        return GetOrFetchAsync($"giphy:search:{searchTerm}", TimeSpan.FromMinutes(_options.SearchCacheMinutes),
                               token => _inner.SearchAsync(searchTerm, token),
                               cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetOrFetchAsync(string cacheKey, TimeSpan expiration,
                                                              Func<CancellationToken, Task<IReadOnlyList<string>>> fetch,
                                                              CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<string>? cached) && cached is not null)
        {
            return cached;
        }

        var gate = _locks.GetLock(cacheKey);

        await gate.WaitAsync(cancellationToken);

        try
        {
            // Another request may have populated the cache while we waited.
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var urls = await fetch(cancellationToken);

            // Each result list consumes one entry. A full cache may decline to retain it;
            // the caller still receives the successfully fetched result.
            _cache.Set(cacheKey, urls, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Size = 1
            });

            return urls;
        }
        finally
        {
            gate.Release();
        }
    }
}
