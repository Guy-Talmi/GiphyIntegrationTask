using GiphyIntegration.api.Options;
using GiphyIntegration.api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace GiphyIntegration.api.Tests;

public class CachedGifServiceTests
{
    // Searches for the same term twice in sequence using a fake service, without calling GIPHY.
    // Verifies that both calls return the expected URL and the inner service is called only once:
    // the first call populates the cache, and the second call reuses the cached result.
    [Fact]
    public async Task SearchAsync_RepeatedTerm_FetchesOnlyOnce()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();

        var inner = new CountingGifService();
        var service = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        // Act
        var first = await service.SearchAsync("cats");
        var second = await service.SearchAsync("cats");

        // Assert
        Assert.Equal(1, inner.SearchCallCount);
        Assert.Equal("https://example.test/cat.gif", Assert.Single(first));
        Assert.Equal("https://example.test/cat.gif", Assert.Single(second));
    }

    // Starts a second identical search while the first fetch is still pending.
    // Verifies that separate decorator instances share the locks and cache,
    // so both requests receive the result after only one inner-service call.
    [Fact]
    public async Task SearchAsync_OverlappingRequests_FetchesOnlyOnce()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var inner = new ControlledGifService();

        var firstService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));
        var secondService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        // Act: keep the first fetch pending while starting the second request.
        var first = firstService.SearchAsync("cats", timeout.Token);
        await inner.Started.WaitAsync(timeout.Token);

        var second = secondService.SearchAsync("cats", timeout.Token);

        inner.Complete();

        var results = await Task.WhenAll(first, second);

        // Assert
        Assert.Equal(1, inner.SearchCallCount);

        foreach (var result in results)
        {
            Assert.Equal(
                "https://example.test/cat.gif",
                Assert.Single(result));
        }
    }

    // Makes the first fetch fail, then verifies that a retry can succeed.
    // Confirms that the lock is released after failure, failed calls are not
    // cached, and a later request reuses the successfully cached result.
    [Fact]
    public async Task SearchAsync_AfterFailure_RetriesAndCachesSuccess()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var inner = new FailOnceGifService();
        var service = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        // Act: the first fetch fails.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => service.SearchAsync("cats", timeout.Token));

        // A retry succeeds; the following request should use the cache.
        var retry = await service.SearchAsync("cats", timeout.Token);
        var cached = await service.SearchAsync("cats", timeout.Token);

        // Assert: one failed fetch + one successful fetch.
        Assert.Equal(2, inner.SearchCallCount);
        Assert.Equal("https://example.test/cat.gif", Assert.Single(retry));
        Assert.Equal("https://example.test/cat.gif", Assert.Single(cached));
    }

    // Cancels a request waiting for the semaphore while another fetch is active.
    // Verifies that cancellation does not cancel the active fetch or release
    // its lock, and another waiting request can reuse the cached result.
    [Fact]
    public async Task SearchAsync_CancelledWaiter_DoesNotInterruptActiveFetch()
    {
        // Arrange
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var waiterCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        var inner = new ControlledGifService();

        var firstService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));
        var secondService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        // Start the first fetch and keep its response pending.
        var first = firstService.SearchAsync("cats", timeout.Token);
        await inner.Started.WaitAsync(timeout.Token);

        // Start a second request, then cancel only that request.
        var cancelled = secondService.SearchAsync(
            "cats", waiterCancellation.Token);

        waiterCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancelled);

        // A third request should wait for the original fetch.
        var third = secondService.SearchAsync("cats", timeout.Token);

        inner.Complete();

        var results = await Task.WhenAll(first, third);

        // Assert
        Assert.Equal(1, inner.SearchCallCount);

        foreach (var result in results)
        {
            Assert.Equal(
                "https://example.test/cat.gif",
                Assert.Single(result));
        }
    }

    // Cancels the request that currently owns the semaphore and performs the fetch.
    // Verifies that a waiting request acquires the released lock, fetches successfully,
    // and caches its result instead of inheriting the first request's cancellation.
    [Fact]
    public async Task SearchAsync_CancelledActiveFetch_AllowsWaitingRequestToSucceed()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var activeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);

        var inner = new ControlledGifService();
        var firstService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));
        var secondService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        var active = firstService.SearchAsync("cats", activeCancellation.Token);
        await inner.Started.WaitAsync(timeout.Token);

        var waiting = secondService.SearchAsync("cats", timeout.Token);
        activeCancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);

        // The replacement fetch must start before the fake returns any successful result.
        await inner.SecondStarted.WaitAsync(timeout.Token);
        inner.Complete();

        var result = await waiting;
        var cached = await firstService.SearchAsync("cats", timeout.Token);

        Assert.Equal(2, inner.SearchCallCount);
        Assert.Equal("https://example.test/cat.gif", Assert.Single(result));
        Assert.Equal(Assert.Single(result), Assert.Single(cached));
    }

    // Holds both upstream fetches open and waits for both to start before releasing either.
    // Selects keys mapped to different actual semaphores because string hashes vary by process.
    // This fails if unrelated cache misses are accidentally serialized behind one global lock.
    [Fact]
    public async Task SearchAsync_DifferentSemaphores_FetchesConcurrently()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        const string firstTerm = "cats";
        var firstGate = locks.GetLock($"giphy:search:{firstTerm}");
        var secondTerm = Enumerable.Range(0, 1024)
            .Select(index => $"dogs-{index}")
            .First(term => !ReferenceEquals(
                firstGate, locks.GetLock($"giphy:search:{term}")));

        var inner = new ControlledGifService();
        var firstService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));
        var secondService = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        var first = firstService.SearchAsync(firstTerm, timeout.Token);
        await inner.Started.WaitAsync(timeout.Token);

        var second = secondService.SearchAsync(secondTerm, timeout.Token);
        await inner.SecondStarted.WaitAsync(timeout.Token);

        var firstWasPending = !first.IsCompleted;
        var secondWasPending = !second.IsCompleted;
        inner.Complete();

        var results = await Task.WhenAll(first, second);

        Assert.True(firstWasPending, "The first fetch must still be pending when the second starts.");
        Assert.True(secondWasPending, "The second fetch must start before either response is released.");
        Assert.Equal(2, inner.SearchCallCount);

        foreach (var result in results)
        {
            Assert.Equal("https://example.test/cat.gif", Assert.Single(result));
        }
    }

    // Searches for two different terms and repeats both searches.
    // Verifies that each term has its own cached result and is fetched only once.
    [Fact]
    public async Task SearchAsync_DifferentTerms_UseSeparateCacheEntries()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        var inner = new RecordingGifService();
        var service = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        var cats = await service.SearchAsync("cats");
        var dogs = await service.SearchAsync("dogs");
        var cachedCats = await service.SearchAsync("cats");
        var cachedDogs = await service.SearchAsync("dogs");

        Assert.Equal("https://example.test/search/cats/1.gif", Assert.Single(cats));
        Assert.Equal("https://example.test/search/dogs/1.gif", Assert.Single(dogs));
        Assert.Equal(Assert.Single(cats), Assert.Single(cachedCats));
        Assert.Equal(Assert.Single(dogs), Assert.Single(cachedDogs));
        Assert.Equal(1, inner.GetSearchCallCount("cats"));
        Assert.Equal(1, inner.GetSearchCallCount("dogs"));
    }

    // Requests trending GIFs twice and verifies that the second request
    // returns the cached result without calling the inner service again.
    [Fact]
    public async Task GetTrendingAsync_RepeatedRequests_FetchesOnlyOnce()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        var inner = new RecordingGifService();
        var service = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        var first = await service.GetTrendingAsync();
        var second = await service.GetTrendingAsync();

        Assert.Equal("https://example.test/trending/1.gif", Assert.Single(first));
        Assert.Equal(Assert.Single(first), Assert.Single(second));
        Assert.Equal(1, inner.TrendingCallCount);
    }

    // Uses the search term "trending" to check that the operation is part of the cache key.
    // Exercises both request orders so neither operation can overwrite the other's result.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TrendingAndSearch_UseSeparateCacheEntries(bool trendingFirst)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var locks = new GifCacheLocks();
        var inner = new RecordingGifService();
        var service = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions()));

        IReadOnlyList<string> trending;
        IReadOnlyList<string> search;

        if (trendingFirst)
        {
            trending = await service.GetTrendingAsync();
            search = await service.SearchAsync("trending");
        }
        else
        {
            search = await service.SearchAsync("trending");
            trending = await service.GetTrendingAsync();
        }

        var cachedTrending = await service.GetTrendingAsync();
        var cachedSearch = await service.SearchAsync("trending");

        Assert.Equal("https://example.test/trending/1.gif", Assert.Single(trending));
        Assert.Equal("https://example.test/search/trending/1.gif", Assert.Single(search));
        Assert.Equal(Assert.Single(trending), Assert.Single(cachedTrending));
        Assert.Equal(Assert.Single(search), Assert.Single(cachedSearch));
        Assert.Equal(1, inner.TrendingCallCount);
        Assert.Equal(1, inner.GetSearchCallCount("trending"));
    }

    // Uses configured lifetimes different from the defaults and advances a fake clock.
    // Verifies reuse just before expiration, a fresh fetch at expiration,
    // and reuse of the replacement entry. A cache hit must not extend the absolute lifetime.
    [Theory]
    [InlineData(true, 7)]
    [InlineData(false, 11)]
    public async Task CachedResult_AtExpiration_FetchesAndCachesFreshResult(
        bool trending,
        int expirationMinutes)
    {
        var clock = new ManualClock();
        using var cache = new MemoryCache(new MemoryCacheOptions { Clock = clock });
        using var locks = new GifCacheLocks();
        var inner = new RecordingGifService();
        var service = new CachedGifService(inner, cache, locks, Microsoft.Extensions.Options.Options.Create(new GiphyOptions { TrendingCacheMinutes = 7, SearchCacheMinutes = 11 }));

        Task<IReadOnlyList<string>> FetchAsync() => trending
            ? service.GetTrendingAsync()
            : service.SearchAsync("cats");

        int FetchCount() => trending
            ? inner.TrendingCallCount
            : inner.GetSearchCallCount("cats");

        var first = await FetchAsync();

        clock.Advance(TimeSpan.FromMinutes(expirationMinutes) - TimeSpan.FromSeconds(1));
        var beforeExpiration = await FetchAsync();

        Assert.Equal(1, FetchCount());
        Assert.Equal(Assert.Single(first), Assert.Single(beforeExpiration));

        clock.Advance(TimeSpan.FromSeconds(1));
        var afterExpiration = await FetchAsync();

        Assert.Equal(2, FetchCount());
        Assert.NotEqual(Assert.Single(first), Assert.Single(afterExpiration));

        var cachedReplacement = await FetchAsync();

        Assert.Equal(2, FetchCount());
        Assert.Equal(Assert.Single(afterExpiration), Assert.Single(cachedReplacement));
    }

    // Returns distinct URLs for each operation, term, and fetch number,
    // making accidental cache-key collisions and stale results observable.
    private sealed class RecordingGifService : IGifService
    {
        private readonly Dictionary<string, int> _searchCalls = new(StringComparer.Ordinal);

        public int TrendingCallCount { get; private set; }

        public int GetSearchCallCount(string term) => _searchCalls.GetValueOrDefault(term);

        public Task<IReadOnlyList<string>> SearchAsync(
            string searchTerm,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = GetSearchCallCount(searchTerm) + 1;
            _searchCalls[searchTerm] = count;

            IReadOnlyList<string> urls = new[]
            {
                $"https://example.test/search/{Uri.EscapeDataString(searchTerm)}/{count}.gif"
            };

            return Task.FromResult(urls);
        }

        public Task<IReadOnlyList<string>> GetTrendingAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TrendingCallCount++;

            IReadOnlyList<string> urls = new[]
            {
                $"https://example.test/trending/{TrendingCallCount}.gif"
            };

            return Task.FromResult(urls);
        }
    }

    // This clock affects only the MemoryCache created by the expiration test.
    private sealed class ManualClock : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan duration)
        {
            UtcNow += duration;
        }
    }

    private sealed class CountingGifService : IGifService
    {
        public int SearchCallCount { get; private set; }

        public Task<IReadOnlyList<string>> SearchAsync(
            string searchTerm,
            CancellationToken cancellationToken = default)
        {
            SearchCallCount++;

            IReadOnlyList<string> urls =
                new[] { "https://example.test/cat.gif" };

            return Task.FromResult(urls);
        }

        public Task<IReadOnlyList<string>> GetTrendingAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                Array.Empty<string>());
        }
    }

    private sealed class ControlledGifService : IGifService
    {
        private int _searchCallCount;

        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> _secondStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<IReadOnlyList<string>> _response =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SearchCallCount => Volatile.Read(ref _searchCallCount);

        public Task Started => _started.Task;

        public Task SecondStarted => _secondStarted.Task;

        public Task<IReadOnlyList<string>> SearchAsync(
            string searchTerm,
            CancellationToken cancellationToken = default)
        {
            var callNumber = Interlocked.Increment(ref _searchCallCount);
            _started.TrySetResult(true);

            if (callNumber == 2)
            {
                _secondStarted.TrySetResult(true);
            }

            return _response.Task.WaitAsync(cancellationToken);
        }

        public void Complete()
        {
            _response.TrySetResult(
                new[] { "https://example.test/cat.gif" });
        }

        public Task<IReadOnlyList<string>> GetTrendingAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                Array.Empty<string>());
        }
    }

    private sealed class FailOnceGifService : IGifService
    {
        private int _searchCallCount;

        public int SearchCallCount => Volatile.Read(ref _searchCallCount);

        public Task<IReadOnlyList<string>> SearchAsync(
            string searchTerm,
            CancellationToken cancellationToken = default)
        {
            var callNumber = Interlocked.Increment(ref _searchCallCount);

            if (callNumber == 1)
            {
                return Task.FromException<IReadOnlyList<string>>(
                    new HttpRequestException("Simulated upstream failure."));
            }

            IReadOnlyList<string> urls =
                new[] { "https://example.test/cat.gif" };

            return Task.FromResult(urls);
        }

        public Task<IReadOnlyList<string>> GetTrendingAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(
                Array.Empty<string>());
        }
    }
}
