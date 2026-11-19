namespace GiphyIntegration.api.Options;

public sealed class GiphyOptions
{
    public const string SectionName = "Giphy";
    // GIPHY's search API rejects queries longer than this provider limit.
    public const int MaximumSupportedSearchTermLength = 50;

    public string BaseUrl { get; set; } = string.Empty;
    public string TrendingApi { get; set; } = "gifs/trending";
    public string SearchApi { get; set; } = "gifs/search";
    public string ApiKey { get; set; } = string.Empty;
    public int HttpTimeoutSeconds { get; set; } = 10;
    public int MaxSearchTermLength { get; set; } = MaximumSupportedSearchTermLength;
    public int ResultsLimit { get; set; } = 25;
    // Maximum result lists shared by trending and search, per application instance; not a byte limit.
    public int CacheMaxEntries { get; set; } = 1000;
    public int TrendingCacheMinutes { get; set; } = 5;
    public int SearchCacheMinutes { get; set; } = 30;
}
