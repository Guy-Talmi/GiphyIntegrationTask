# Giphy Integration API

An ASP.NET Core API that returns original GIF URLs from GIPHY trending and search endpoints. It includes configurable HTTP settings, validated configuration, a bounded in-memory cache, concurrency coordination, structured error responses, and Swagger UI.

## Prerequisites

- .NET 10 SDK, or a compatible newer SDK with the .NET 10 runtime available. The project targets `net10.0` and uses C# 14 extension blocks.
- A GIPHY API key for running real requests. See the [GIPHY API documentation](https://developers.giphy.com/docs/api/).
- Network access for the initial NuGet restore and for live requests to GIPHY.

Run the commands below from the repository root, which contains `GiphyIntegrationTask.slnx`.

## Local setup and execution

Restore dependencies and build:

```powershell
dotnet restore GiphyIntegrationTask.slnx
dotnet build GiphyIntegrationTask.slnx --no-restore
```

Store your development API key in User Secrets, replacing the placeholder:

```powershell
dotnet user-secrets set "Giphy:ApiKey" "YOUR_GIPHY_API_KEY" --project GiphyIntegration.api/GiphyIntegration.api.csproj
```

The API project already has a User Secrets identifier; no initialization command is needed. User Secrets are loaded in Development and kept outside the repository. They are not encrypted and are intended for development, not production secret storage. See [Microsoft's User Secrets documentation](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-10.0).

Trust the local HTTPS development certificate if it is not already trusted:

```powershell
dotnet dev-certs https --trust
```

This command may display an operating-system confirmation prompt. See the [development certificate documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-dev-certs).

Start the API using the HTTPS launch profile:

```powershell
dotnet run --project GiphyIntegration.api/GiphyIntegration.api.csproj --launch-profile https
```

This profile sets the environment to Development and listens on HTTPS port 7284 and HTTP port 5133. Use HTTPS for the examples below. The browser does not open automatically.

- [Swagger UI](https://localhost:7284/swagger)
- [OpenAPI JSON](https://localhost:7284/openapi/v1.json)

Swagger UI and the OpenAPI document are enabled only in Development. Press `Ctrl+C` in the hosting terminal to stop the API.

## Configuration

The `Giphy` section in `GiphyIntegration.api/appsettings.json` contains the non-secret defaults:

```json
{
  "Giphy": {
    "BaseUrl": "https://api.giphy.com/v1/",
    "TrendingApi": "gifs/trending",
    "SearchApi": "gifs/search",
    "HttpTimeoutSeconds": 10,
    "MaxSearchTermLength": 50,
    "ResultsLimit": 25,
    "CacheMaxEntries": 1000,
    "TrendingCacheMinutes": 5,
    "SearchCacheMinutes": 30
  }
}
```

| Setting | Configured default | Meaning and validation |
| --- | --- | --- |
| `ApiKey` | Not stored in appsettings | Required and not whitespace. Supply through User Secrets in Development or the deployment's configuration provider. |
| `BaseUrl` | `https://api.giphy.com/v1/` | Absolute HTTPS address with a path ending in `/`, so relative paths preserve the API version. |
| `TrendingApi` | `gifs/trending` | Relative upstream trending path. |
| `SearchApi` | `gifs/search` | Relative upstream search path. |
| `HttpTimeoutSeconds` | `10` | HTTP-client timeout, from 1 through 2,147,483 seconds. |
| `MaxSearchTermLength` | `50` | Accepted search length, from 1 through 50 characters. The limit is inclusive. |
| `ResultsLimit` | `25` | Requested maximum results per upstream call, from 1 through 50. GIPHY can return fewer results. |
| `CacheMaxEntries` | `1000` | Positive maximum number of cached result lists, shared by search and trending in one application instance. This is an entry count, not a byte limit. |
| `TrendingCacheMinutes` | `5` | Positive absolute lifetime of a cached trending result. |
| `SearchCacheMinutes` | `30` | Positive absolute lifetime of a cached search result. |

Both upstream paths must be nonblank relative paths without a leading slash, backslash, query string, fragment, or parent-directory segment (`..`, including encoded dots). These settings do not change the public API routes.

Configuration is validated at startup. Restart the application after changing these settings; the services use `IOptions<GiphyOptions>`, not live configuration updates. Environment variables can override nested settings using double underscores, for example `Giphy__ApiKey` or `Giphy__CacheMaxEntries`. Keep real API keys out of source-controlled files.

## Endpoints and examples

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/GiphyIntegration/trending` | Return original GIF URLs from the first page of trending results. |
| GET | `/api/GiphyIntegration/search?q=cats` | Return original GIF URLs for the supplied search text. |

Search requires `q`, rejects empty or whitespace-only text, and enforces the configured length limit. Search terms are forwarded without trimming or case normalization. Swagger reflects the configured search-length and results limits.

With the API running, send these requests from PowerShell:

```powershell
Invoke-RestMethod -Uri 'https://localhost:7284/api/GiphyIntegration/trending'
Invoke-RestMethod -Uri 'https://localhost:7284/api/GiphyIntegration/search?q=cats'
Invoke-RestMethod -Uri 'https://localhost:7284/api/GiphyIntegration/search?q=tom%20%26%20jerry'
```

Successful responses are JSON arrays of URL strings, without a `data` wrapper. An illustrative response is:

```json
["https://example.test/illustrative.gif"]
```

A valid search with no matches returns `[]` and HTTP 200. You can also send the requests in `GiphyIntegration.api/GiphyIntegration.api.http` from your IDE; the API must be running for those requests.

## Errors and diagnostics

| HTTP status | Meaning |
| --- | --- |
| `400` | Invalid search input, rejected by model validation or the service's argument guard. |
| `502` | Upstream HTTP failure, invalid JSON, or a response missing required GIF data or image URLs. |
| `504` | An upstream HTTP-client timeout. |
| `500` | An unexpected failure. |

Handled failures return `ProblemDetails`; automatic model-validation failures return `ValidationProblemDetails` with an `errors` collection. Responses use generic error details rather than exposing exception messages. Client cancellation propagates and is not converted into a timeout response.

The controller logs the operation, a controlled failure category, and the request trace identifier. Upstream failures use Warning; unexpected failures use Error. These controller log entries exclude exception objects, credentials, search text, and raw request URLs. Framework logging is configured separately under `Logging`.

## Cache and concurrency behavior

- A dedicated singleton cache stores URL lists, not GIF image bytes. Each list consumes one entry from the shared capacity.
- Search keys use the exact search text. Trending has its own key and cannot overwrite a search entry.
- Cache lifetimes are absolute; reading an entry does not extend its lifetime.
- A shared array of 64 semaphores coordinates cache misses. A waiting request checks the cache again after acquiring its semaphore. Different keys can share a semaphore and therefore sometimes wait for each other.
- Failed fetches are not cached. Cancellation while waiting does not release another request's semaphore; the active owner releases its semaphore in a `finally` block.
- If a new entry would exceed capacity, the cache can decline to store it and initiate background compaction. The successfully fetched result is still returned to the caller. Rejected entries are not inserted automatically after compaction.

## Tests

Run the complete suite after restoring dependencies:

```powershell
dotnet test GiphyIntegrationTask.slnx --no-restore
```

Run only cache tests:

```powershell
dotnet test GiphyIntegration.api.Tests/GiphyIntegration.api.Tests.csproj --no-restore --filter FullyQualifiedName~CachedGifServiceTests
```

In Visual Studio, open the solution and use **Test Explorer > Run All Tests**.

Tests do not require a running API or a real GIPHY key. Unit tests use controlled dependencies; integration tests start an internal test server and replace outbound HTTP transport. They make no real GIPHY requests.

Coverage includes cache reuse, capacity, expiration with controlled time, concurrency, cancellation, JSON parsing, query encoding, controller error mapping and logging, request validation, configuration validation, registered HTTP-client settings, routing, and OpenAPI metadata.

## Structure and limitations

- `Controllers`: HTTP endpoints and shared exception-to-response mapping.
- `Models`: incoming search requests and upstream JSON models.
- `Services`: upstream client, caching decorator, dedicated cache, and semaphore registry.
- `Options` and `Extensions`: configuration, startup validation, service registration, and OpenAPI setup.
- `GiphyIntegration.api.Tests`: unit and in-process integration tests.

Only the first page of GIPHY results is returned; public pagination is not implemented. The cache and locks are local to each application instance and are cleared on restart. When a result cannot be cached, a waiting request may fetch it again, so coordination does not guarantee one upstream call for every group of overlapping requests under capacity pressure.

The application currently has no caller authentication, request rate limiting, automatic upstream retries, or frontend. Live requests depend on the configured GIPHY key and its available quota. The automated suite verifies behavior using simulated upstream responses; it does not validate a real key or current GIPHY availability.
