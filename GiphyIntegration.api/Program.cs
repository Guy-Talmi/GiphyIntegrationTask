using GiphyIntegration.api.Options;
using GiphyIntegration.api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

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
        serviceProvider.GetRequiredService<GifCacheLocks>()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
