using GiphyIntegration.api.Controllers;
using GiphyIntegration.api.Options;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace GiphyIntegration.api.Extensions;

public static class OpenApiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddGiphyOpenApi()
        {
            return services.AddOpenApi(options =>
            {
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Info.Title = "Giphy Integration API";
                    document.Info.Version = "v1";
                    document.Info.Description = "Retrieve trending GIF URLs or search for GIFs.";
                    return Task.CompletedTask;
                });

                // Keep the documented limits consistent with the configured HTTP behavior.
                options.AddOperationTransformer((operation, context, cancellationToken) =>
                {
                    if (context.Description.ActionDescriptor is not ControllerActionDescriptor controller
                        || controller.ControllerTypeInfo.AsType() != typeof(GiphyIntegrationController))
                    {
                        return Task.CompletedTask;
                    }

                    var giphyOptions = context.ApplicationServices.GetRequiredService<IOptions<GiphyOptions>>().Value;
                    operation.Description += $" The current maximum is {giphyOptions.ResultsLimit} GIF URLs per request.";

                    var searchParameter = context.Description.ParameterDescriptions.FirstOrDefault(
                        parameter => parameter.ModelMetadata?.ContainerType == typeof(SearchGifsRequest)
                            && parameter.ModelMetadata.PropertyName == nameof(SearchGifsRequest.SearchTerm));

                    if (searchParameter is not null
                        && operation.Parameters?.OfType<OpenApiParameter>().FirstOrDefault(
                            parameter => parameter.Name == searchParameter.Name) is { } queryParameter
                        && queryParameter.Schema is OpenApiSchema schema)
                    {
                        var limit = giphyOptions.MaxSearchTermLength;
                        schema.MinLength = 1;
                        schema.MaxLength = limit;
                        queryParameter.Description = $"Search text: 1 to {limit} characters, not whitespace-only.";
                    }

                    return Task.CompletedTask;
                });
            });
        }
    }
}
