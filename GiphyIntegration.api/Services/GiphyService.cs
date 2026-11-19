using GiphyIntegration.api.Models;
using GiphyIntegration.api.Options;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace GiphyIntegration.api.Services;

public sealed class GiphyService : IGifService
{
    private readonly HttpClient _httpClient;
    private readonly GiphyOptions _options;

    public GiphyService(HttpClient httpClient, IOptions<GiphyOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public Task<IReadOnlyList<string>> GetTrendingAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = Uri.EscapeDataString(_options.ApiKey);
        var requestUri = $"gifs/trending?api_key={apiKey}&limit=25";

        return FetchGifUrlsAsync(requestUri, cancellationToken);
    }

    public Task<IReadOnlyList<string>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException("A search term is required.", nameof(searchTerm));
        }

        if (searchTerm.Length > 50)
        {
            throw new ArgumentException(
                "The search term must not exceed 50 characters.",
                nameof(searchTerm));
        }

        var apiKey = Uri.EscapeDataString(_options.ApiKey);
        var query = Uri.EscapeDataString(searchTerm);

        var requestUri = $"gifs/search?api_key={apiKey}&q={query}&limit=25";

        return FetchGifUrlsAsync(requestUri, cancellationToken);
    }

    private async Task<IReadOnlyList<string>> FetchGifUrlsAsync(string requestUri, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetFromJsonAsync<GiphyResponse>(requestUri, cancellationToken);

        if (response?.Data is null)
        {
            throw new JsonException("The GIPHY response is missing its data collection.");
        }

        return response.Data.Select(gif =>
        {
            var url = gif?.Images?.Original?.Url;

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new JsonException("A GIPHY result is missing its original image URL.");
            }

            return url;
        }).ToArray();
    }
}