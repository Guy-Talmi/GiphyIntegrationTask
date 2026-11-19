namespace GiphyIntegration.api.Options;

public sealed class GiphyOptions
{
    public const string SectionName = "Giphy";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public int TrendingCacheMinutes { get; set; } = 5;
    public int SearchCacheMinutes { get; set; } = 30;
}
