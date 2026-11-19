# Giphy Integration

An ASP.NET Core API and Angular GIF browser. The API returns original GIF URLs from GIPHY trending and search endpoints and includes configurable HTTP settings, validated configuration, a bounded in-memory cache, concurrency coordination, structured error responses, and Swagger UI. The Angular client displays the GIFs and supports trending, search, loading, empty results, and error states.

The backend is deployed to Azure App Service and the client has a separate Azure Static Web Apps deployment workflow. Both workflows run tests before deployment.

## Prerequisites

- .NET 10 SDK, or a compatible newer SDK with the .NET 10 runtime available. The project targets `net10.0` and uses C# 14 extension blocks.
- Node.js and npm for the Angular client. The client workflow uses Node.js `24.21.0` and npm `11.19.0`; the project uses Angular 22.3 and an npm lockfile.
- A GIPHY API key for running real requests. See the [GIPHY API documentation](https://developers.giphy.com/docs/api/).
- Network access for the initial NuGet restore and for live requests to GIPHY.

Run commands from the repository root, which contains `GiphyIntegrationTask.slnx`, unless a section explicitly changes directory. PowerShell examples use `npm.cmd` on Windows; use `npm` on Linux and macOS.

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

### Start the Angular client

Keep the API running and open a second terminal. From the repository root:

```powershell
Set-Location GiphyIntegration.client
npm.cmd ci
npm.cmd start
```

Open [the local GIF browser](http://localhost:4200/). The home route lazily loads the gallery and requests trending GIFs. Submit a search or select **Trending** to switch results. Unknown client routes redirect to the home page.

The client trims search input, prevents blank submissions, and cancels the previous request when another request starts so an old response cannot replace newer results. It displays controlled error messages and uses a responsive layout with equal-width buttons and stable scrollbar space.

`proxy.conf.json` forwards `/api/**` to `https://localhost:7284`. Its `secure: false` setting accepts the local development certificate; `changeOrigin: true` changes the forwarded Host header to match the API. Restart the Angular development server after editing the proxy configuration. This proxy is used only by the development server, not by the deployed static site.

### Client environments and production build

| File under `GiphyIntegration.client/src/environments` | Usage | `apiBaseUrl` |
| --- | --- | --- |
| `environment.development.ts` | Local development; selected by the development file replacement in `angular.json`. | `/api/GiphyIntegration` |
| `environment.ts` | Production builds. | The Azure API HTTPS address, including `/api/GiphyIntegration`. |

`GifApiService` imports `environment` and appends `/trending` or `/search`; search text is sent as the `q` query parameter. Keep the controller path in the base URL and omit a trailing slash. The API URL is public configuration; never put GIPHY keys or deployment tokens in Angular environment files. These files are bundled at build time, so changing the production URL requires rebuilding and redeploying the client.

From the client directory, build the production version:

```powershell
npm.cmd run build -- --configuration production
```

The deployable files are in `dist/giphy-integration-client/browser`. The `public` asset configuration copies `public/staticwebapp.config.json` into that directory alongside `index.html`. Its navigation fallback serves `index.html` for client-side routes, while excluding API paths and the listed asset extensions. It does not proxy requests to the backend.

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

Search requires `q`, rejects empty or whitespace-only text, and enforces the configured length limit. The API forwards search terms without trimming or case normalization; the Angular client trims input before sending it. Swagger reflects the configured search-length and results limits.

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

### Backend

Run the complete backend suite after restoring dependencies:

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

### Client

From `GiphyIntegration.client`, after `npm.cmd ci`, run the client tests once:

```powershell
npm.cmd test -- --watch=false
```

For watch mode, use `npm.cmd test`. The .NET solution's test command does not run Angular tests.

Client tests use Vitest and simulated HTTP responses or service dependencies. They cover API paths and query parameters, empty results, failures, cancellation, gallery loading, trimmed search input, stale-response prevention, error recovery, and routing. They do not require a running API or a GIPHY key and do not verify real image downloads, browser layout, CORS, or Azure connectivity.

## Azure hosting and configuration

The backend and frontend are separate Azure resources in resource group `guy-study-rg`:

| Component | Resource | Address |
| --- | --- | --- |
| API | Linux .NET 10 App Service: `giphy-Integration-api` | [API trending endpoint](https://giphy-integration-api-frcpeudzddfygvd8.swedencentral-01.azurewebsites.net/api/GiphyIntegration/trending) |
| Client | Static Web App: `giphy-integration-client` | [GIF browser](https://agreeable-hill-02489d410.3.azurestaticapps.net) |

These addresses identify the configured resources; creating a resource alone does not publish the application. Verify the relevant workflow and live requests after deployment.

Configure the API in Azure Portal:

1. Open the API App Service, then **Settings > Environment variables > App settings**.
2. Set `Giphy__ApiKey` to the real GIPHY key and save/apply. Local User Secrets are not deployed. Other `Giphy` settings can be overridden using the same double-underscore naming convention.
3. Under **API > CORS**, allow the client origin `https://agreeable-hill-02489d410.3.azurestaticapps.net`, without a trailing slash. Keep the allowlist specific to the client origins you intend to support. The current application does not configure CORS in code; the Azure setup uses App Service CORS.

The production client calls the App Service directly using its absolute API URL. CORS allows the browser to read those responses; it does not authenticate callers or prevent direct requests from other clients. Swagger remains Development-only, so verify production using the actual API endpoints.

## GitHub Actions deployment

Workflow files are stored in `.github/workflows` at the repository root, alongside the API and client directories. Deployments use the `master` branch.

### Deployment credentials

The API workflow uses OpenID Connect (OIDC) with the user-assigned managed identity `id-giphy-github`. Its federated credential trusts this GitHub repository's `master` branch. The identity has **Website Contributor** scoped to the API App Service, rather than to the entire subscription.

When recreating this setup, find the repository's **Default subject claim prefix** under **GitHub repository Settings > Actions > General > OIDC Configuration**. The full subject for this setup is that prefix followed by `:ref:refs/heads/master`; configure the Azure federated credential to match. The immutable prefix includes numeric owner and repository IDs. Use the GitHub IDs in those fields, not Azure IDs. The issuer is `https://token.actions.githubusercontent.com` and the audience is `api://AzureADTokenExchange`. The current workflows do not declare a GitHub deployment environment; introducing one changes the OIDC subject and requires matching trust configuration.

Add the following under **GitHub repository Settings > Secrets and variables > Actions > Secrets**:

| Repository secret | Source |
| --- | --- |
| `AZURE_CLIENT_ID` | **Client ID** from the managed identity's Overview page, not its Object/principal ID. |
| `AZURE_TENANT_ID` | **Tenant ID** from Microsoft Entra ID Overview. |
| `AZURE_SUBSCRIPTION_ID` | **Subscription ID** for the App Service's Azure subscription. |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | Existing token from the client Static Web App's **Overview > Manage deployment token**. |

The three Azure IDs identify the account context; OIDC supplies short-lived authentication without a client secret. The Static Web App uses its separate deployment token. Keep that token in GitHub Secrets, not in source code, documentation, or Angular configuration. The GIPHY API key belongs in the API's runtime settings and is not needed for either workflow's tests.

### Workflows and triggers

| Workflow | Automatic trigger on a push to `master` | Steps |
| --- | --- | --- |
| `Deploy API` (`deploy-api.yml`) | API code, backend tests, its workflow, the solution, or shared .NET build/NuGet configuration changes. | Restore, Release build, backend tests, publish deployment files, OIDC login, deploy to App Service. |
| `Deploy Client` (`deploy-client.yml`) | Client directory or its workflow changes. | Install Node/npm, `npm ci`, client tests, production build, verify entry/configuration files, upload to Static Web Apps. |

Client-only changes no longer trigger API deployment. Changes that match both workflows run both independently. A root README-only change triggers neither workflow. Both also support **Actions > select workflow > Run workflow**, with `master` selected. Each has its own concurrency group and does not cancel an active deployment when another run is triggered.

The client workflow uploads `GiphyIntegration.client/dist/giphy-integration-client/browser` with the platform's app build skipped because Angular was already built and tested. It has an empty API location and does not deploy the .NET backend. The API workflow never builds or deploys Angular. Both stop before deployment if an earlier build or test step fails.

Saving a workflow locally does not run it. Commit and push the intended changes to GitHub to trigger the matching workflow. Use the **Actions** tab to inspect individual steps and failures.

### Verify a deployment

1. Confirm the relevant GitHub Actions run completed successfully.
2. Open the API's trending endpoint and `/api/GiphyIntegration/search?q=cats`; expect JSON arrays of GIF URLs. A successful upload alone does not prove the API started with valid runtime configuration.
3. Open the Static Web App and verify initial trending results, a search, and switching back to trending.
4. Refresh the page, check the narrow-screen layout, and inspect browser network requests to confirm they target the Azure API rather than localhost.
5. If direct API requests succeed but the browser cannot read them, check the App Service CORS allowlist and the production client URL. If client routing fails on refresh, verify `staticwebapp.config.json` is included in the deployed output.

## Structure and limitations

- `Controllers`: HTTP endpoints and shared exception-to-response mapping.
- `Models`: incoming search requests and upstream JSON models.
- `Services`: upstream client, caching decorator, dedicated cache, and semaphore registry.
- `Options` and `Extensions`: configuration, startup validation, service registration, and OpenAPI setup.
- `GiphyIntegration.api.Tests`: unit and in-process integration tests.
- `GiphyIntegration.client`: standalone Angular client, lazy-loaded gallery route, HTTP service, environment configuration, and client tests.
- `.github/workflows`: separate API and client deployment workflows with path-based triggers.

Only the first page of GIPHY results is returned; public pagination is not implemented. The cache and locks are local to each application instance and are cleared on restart. When a result cannot be cached, a waiting request may fetch it again, so coordination does not guarantee one upstream call for every group of overlapping requests under capacity pressure.

The application currently has no caller authentication or automatic upstream retries. The API is public. Live requests depend on the configured GIPHY key and its available quota. The automated suites verify behavior using simulated responses; they do not validate a real key, current GIPHY availability, or the deployed client-to-API connection.
