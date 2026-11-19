using Microsoft.Extensions.Caching.Memory;

namespace GiphyIntegration.api.Services;

public sealed class CachedGifService : IGifService
{
    private readonly IGifService _inner;
    private readonly IMemoryCache _cache;

    private readonly GifCacheLocks _locks;

    public CachedGifService(IGifService inner, IMemoryCache cache, GifCacheLocks locks)
    {
        _inner = inner;
        _cache = cache;
        _locks = locks;
    }

    public Task<IReadOnlyList<string>> GetTrendingAsync(CancellationToken cancellationToken = default)
    {
        return GetOrFetchAsync("giphy:trending", TimeSpan.FromMinutes(5), _inner.GetTrendingAsync,
                                cancellationToken);
    }

    public Task<IReadOnlyList<string>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        return GetOrFetchAsync($"giphy:search:{searchTerm}", TimeSpan.FromMinutes(30),
                               token => _inner.SearchAsync(searchTerm, token),
                               cancellationToken);
    }

    private async Task<IReadOnlyList<string>> GetOrFetchAsync(
        string cacheKey,
        TimeSpan expiration,
        Func<CancellationToken, Task<IReadOnlyList<string>>> fetch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_cache.TryGetValue(
                cacheKey,
                out IReadOnlyList<string>? cached)
            && cached is not null)
        {
            return cached;
        }

        var gate = _locks.GetLock(cacheKey);

        await gate.WaitAsync(cancellationToken);

        try
        {
            // Another request may have populated the cache while we waited.
            if (_cache.TryGetValue(cacheKey, out cached)
                && cached is not null)
            {
                return cached;
            }

            var urls = await fetch(cancellationToken);

            _cache.Set(cacheKey, urls, expiration);

            return urls;
        }
        finally
        {
            gate.Release();
        }
    }
}