using System.Text.Json.Serialization;

namespace GiphyIntegration.api.Models;

public sealed class GiphyResponse
{
    [JsonPropertyName("data")]
    public List<GiphyGif>? Data { get; set; }
}

public sealed class GiphyGif
{
    [JsonPropertyName("images")]
    public GiphyImages? Images { get; set; }
}

public sealed class GiphyImages
{
    [JsonPropertyName("original")]
    public GiphyImage? Original { get; set; }
}

public sealed class GiphyImage
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}