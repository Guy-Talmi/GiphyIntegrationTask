namespace GiphyIntegration.api.Services;

public sealed class GifCacheLocks : IDisposable
{
    private readonly SemaphoreSlim[] _locks = Enumerable
        .Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1))
        .ToArray();

    public SemaphoreSlim GetLock(string key)
    {
        int hash = StringComparer.Ordinal.GetHashCode(key);
        int remainder = hash % _locks.Length;
        int index = Math.Abs(remainder);

        return _locks[index];
    }

    public void Dispose()
    {
        foreach (var gate in _locks)
        {
            gate.Dispose();
        }
    }
}