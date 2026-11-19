using GiphyIntegration.api.Options;

namespace GiphyIntegration.api.Extensions;

public static class GiphyOptionsServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddGiphyOptions(IConfiguration configuration)
        {
            services
                .AddOptions<GiphyOptions>()
                .Bind(configuration.GetSection(GiphyOptions.SectionName))
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.ApiKey),
                    "Giphy:ApiKey is required.")
                .Validate(
                    options =>
                        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
                        && uri.Scheme == Uri.UriSchemeHttps
                        && uri.AbsolutePath.EndsWith('/'),
                    "Giphy:BaseUrl must be an absolute HTTPS URL with a path ending in '/'.")
                .Validate(
                    options => options.TrendingCacheMinutes > 0,
                    "Giphy:TrendingCacheMinutes must be greater than zero.")
                .Validate(
                    options => options.SearchCacheMinutes > 0,
                    "Giphy:SearchCacheMinutes must be greater than zero.")
                .Validate(
                    options => options.HttpTimeoutSeconds > 0
                        && options.HttpTimeoutSeconds <= int.MaxValue / 1000,
                    "Giphy:HttpTimeoutSeconds must be between 1 and 2147483 seconds.")
                .Validate(
                    options => options.MaxSearchTermLength > 0
                        && options.MaxSearchTermLength <= GiphyOptions.MaximumSupportedSearchTermLength,
                    $"Giphy:MaxSearchTermLength must be between 1 and {GiphyOptions.MaximumSupportedSearchTermLength} characters.")
                // Keep requests compatible with GIPHY beta keys, which allow up to 50 results.
                .Validate(
                    options => options.ResultsLimit > 0 && options.ResultsLimit <= 50,
                    "Giphy:ResultsLimit must be between 1 and 50 results.")
                .Validate(
                    options => IsRelativeApiPath(options.TrendingApi),
                    "Giphy:TrendingApi must be a relative path without a leading slash, query string, or fragment.")
                .Validate(
                    options => IsRelativeApiPath(options.SearchApi),
                    "Giphy:SearchApi must be a relative path without a leading slash, query string, or fragment.")
                .Validate(
                    options => options.CacheMaxEntries > 0,
                    "Giphy:CacheMaxEntries must be greater than zero.")
                .ValidateOnStart();

            return services;
        }
    }
    private static bool IsRelativeApiPath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && Uri.IsWellFormedUriString(path, UriKind.Relative)
        && !path.StartsWith('/')
        && !path.Contains('\\')
        && !path.Contains('?')
        && !path.Contains('#');
}
