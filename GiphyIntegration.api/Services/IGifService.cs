namespace GiphyIntegration.api.Services;

public interface IGifService
{
    Task<IReadOnlyList<string>> GetTrendingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default);
}
