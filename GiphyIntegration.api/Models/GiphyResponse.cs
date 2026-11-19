namespace GiphyIntegration.api.Models;

public sealed class GiphyResponse
{
    public List<GiphyGif>? Data { get; set; }
}

public sealed class GiphyGif
{
    public GiphyImages? Images { get; set; }
}

public sealed class GiphyImages
{
    public GiphyImage? Original { get; set; }
}

public sealed class GiphyImage
{
    public string? Url { get; set; }
}