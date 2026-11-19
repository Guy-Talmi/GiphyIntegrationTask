using GiphyIntegration.api.Options;
using GiphyIntegration.api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info.Title = "Giphy Integration API";
        document.Info.Version = "v1";
        document.Info.Description = "Retrieve trending GIF URLs or search for GIFs.";
        return Task.CompletedTask;
    }));

builder.Services
    .AddOptions<GiphyOptions>()
    .Bind(builder.Configuration.GetSection(GiphyOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ApiKey),
        "Giphy:ApiKey is required.")
    .Validate(
        options =>
            Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps,
        "Giphy:BaseUrl must be an absolute HTTPS URL.")
    .Validate(
        options => options.TrendingCacheMinutes > 0,
        "Giphy:TrendingCacheMinutes must be greater than zero.")
    .Validate(
        options => options.SearchCacheMinutes > 0,
        "Giphy:SearchCacheMinutes must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<GifCacheLocks>();

builder.Services.AddHttpClient<GiphyService>(
    (serviceProvider, client) =>
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<GiphyOptions>>()
            .Value;

        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddTransient<IGifService>(serviceProvider =>
    new CachedGifService(
        serviceProvider.GetRequiredService<GiphyService>(),
        serviceProvider.GetRequiredService<IMemoryCache>(),
        serviceProvider.GetRequiredService<GifCacheLocks>(),
        serviceProvider.GetRequiredService<IOptions<GiphyOptions>>()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Giphy Integration API v1");
        options.DocumentTitle = "Giphy Integration API";
    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
