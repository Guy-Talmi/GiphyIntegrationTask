using GiphyIntegration.api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddGiphyOpenApi();

builder.Services.AddGiphyOptions(builder.Configuration);

builder.Services.AddGiphyHttpClient();

builder.Services.AddCachedGifService();

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
