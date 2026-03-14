using System.ComponentModel.DataAnnotations;
using DataAssetBackend.Features.Assets;
using DataAssetBackend.Features.Masters;
using DataAssetBackend.Infrastructure.Database;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

// Load .env (if present) *before* building configuration.
// Some hosted/preview environments provide secrets via a .env file rather than true process env vars.
// This ensures IConfiguration can resolve DATABASE_URL / ConnectionStrings__Default consistently.
LoadDotEnvIfPresent();

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "Data Asset Backend API";
    config.Version = "1.0.0";
    config.Description = "Backend API for data asset management (POC). Includes validation utilities for asset configuration workflows.";
});

// Forwarded headers: required so Request.Scheme/Host reflect the *external* URL
// (e.g., https://... in hosted environments), avoiding Swagger "Failed to fetch"
// due to mixed scheme (http vs https) or incorrect host/port.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;

    // Managed environments often use dynamic proxy IPs; don't restrict.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// Add CORS
// NOTE: Browsers disallow credentialed requests with wildcard origins.
// Swagger UI typically doesn't require credentials, but the frontend might.
// We therefore support explicit origins via env var, and provide a safe local-dev default.
builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCors", policy =>
    {
        var allowedOriginsEnv = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
        var allowedOrigins = (allowedOriginsEnv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // Dev-only defaults (no credentials when not explicitly configured).
            policy.WithOrigins(
                    "http://localhost:3000",
                    "http://localhost:3001",
                    "https://localhost:7038"
                )
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

// Database configuration
//
// Contract:
// - Input: environment/config keys:
//   - DATABASE_URL (preferred for platforms like Neon; URL form: postgresql://user:pass@host:5432/db?sslmode=require)
//   - ConnectionStrings__Default (standard .NET config binding; typical "Host=...;Username=...;Password=...;Database=...;Ssl Mode=Require;")
// - Output: a normalized Postgres connection string suitable for Npgsql
// - Errors: parsing errors are logged and treated as "not configured" for health checks (API still starts)
// - Side effects: none at startup (no eager DB connect); health endpoint may attempt connection when configured
builder.Services.AddSingleton<DatabaseConfigProvider>();

 // Postgres data access services (Flyway V2 schema)
builder.Services.AddSingleton<NpgsqlConnectionFactory>();
builder.Services.AddSingleton<AssetRepository>();
builder.Services.AddSingleton<MasterRepository>();

var app = builder.Build();

// Must be early in pipeline, before anything that relies on scheme/host (OpenAPI generation).
app.UseForwardedHeaders();

// Use CORS
app.UseCors("DefaultCors");

static string BuildPublishedServerUrl(HttpRequest req)
{
    // Optional override for environments that want a fixed absolute server URL.
    // Example: OPENAPI_SERVER_URL=https://api.example.com
    var explicitServerUrl = Environment.GetEnvironmentVariable("OPENAPI_SERVER_URL");
    if (!string.IsNullOrWhiteSpace(explicitServerUrl))
    {
        return explicitServerUrl.Trim().TrimEnd('/');
    }

    // Best-effort: if the OpenAPI document is being requested by Swagger UI in a browser,
    // the request typically includes an Origin header containing the correct external
    // scheme+host+port (this avoids "missing port" issues behind some proxies).
    var origin = req.Headers.Origin.ToString();
    if (!string.IsNullOrWhiteSpace(origin) &&
        Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
        (string.Equals(originUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(originUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
    {
        return originUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }

    // Fallback: reconstruct from forwarded headers if present.
    var forwardedProto = req.Headers["X-Forwarded-Proto"].ToString();
    var forwardedHost = req.Headers["X-Forwarded-Host"].ToString();
    var forwardedPort = req.Headers["X-Forwarded-Port"].ToString();

    var scheme = !string.IsNullOrWhiteSpace(forwardedProto) ? forwardedProto.Split(',')[0].Trim() : req.Scheme;
    var host = !string.IsNullOrWhiteSpace(forwardedHost) ? forwardedHost.Split(',')[0].Trim() : req.Host.Value;

    // If host doesn't already contain a port, but we have X-Forwarded-Port, add it.
    if (!string.IsNullOrWhiteSpace(host) && !host.Contains(':', StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(forwardedPort))
    {
        host = $"{host}:{forwardedPort.Split(',')[0].Trim()}";
    }

    return $"{scheme}://{host}".TrimEnd('/');
}

static void ConfigureOpenApiDocument(NSwag.AspNetCore.OpenApiDocumentMiddlewareSettings settings)
{
    settings.PostProcess = (document, req) =>
    {
        // Publish a server URL that preserves the externally visible scheme/host/port.
        // This is critical for Swagger UI's "Try it out" to call the correct origin.
        var baseUrl = BuildPublishedServerUrl(req);

        // Respect PathBase if hosting behind a sub-path reverse proxy.
        var pathBase = req.PathBase.HasValue ? req.PathBase.Value : string.Empty;
        var serverUrl = string.IsNullOrWhiteSpace(pathBase) ? baseUrl : $"{baseUrl}{pathBase}";

        document.Servers.Clear();
        document.Servers.Add(new NSwag.OpenApiServer { Url = serverUrl });
    };
}

// Configure OpenAPI/Swagger (serve at default NSwag path)
app.UseOpenApi(settings =>
{
    ConfigureOpenApiDocument(settings);
});

// Also serve OpenAPI at /openapi.json (some tooling expects this path)
app.UseOpenApi(settings =>
{
    settings.Path = "/openapi.json";
    ConfigureOpenApiDocument(settings);
});

app.UseSwaggerUi(config =>
{
    config.Path = "/docs";
});

 // Health check endpoints
// Note: the platform/preview health probe expects `/healthz`.
// We keep `/` as a friendly default while ensuring `/healthz` returns HTTP 200.
app.MapGet("/", () =>
    {
        // Use a typed response so OpenAPI accurately documents the response shape.
        return Results.Ok(new HealthRootResponse(Status: "ok"));
    })
   .WithName("HealthRoot")
   .WithTags("Health")
   .WithSummary("Root health check")
   .WithDescription("Simple health check endpoint at the service root. Primarily for manual verification.")
   .Produces<HealthRootResponse>(StatusCodes.Status200OK);

// Healthz returns DB status *if* DB is configured.
// This is intentionally tolerant: if the DB isn't configured, we still return 200 for platform probes.
// (Production deployments should enforce migrations before API start via Flyway job/container.)
app.MapGet("/healthz", async (DatabaseConfigProvider dbConfigProvider, ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("Healthz");
        var result = await DatabaseHealthCheckFlow.RunAsync(new DatabaseHealthCheckRequest(), dbConfigProvider, logger);

        // Always return 200 for health probe; include DB diagnostics in body.
        return Results.Ok(new HealthzResponse(
            Status: "ok",
            Db: new HealthzDbStatus(
                Configured: result.IsConfigured,
                Ok: result.IsHealthy,
                Error: result.Error)));
    })
   .WithName("Healthz")
   .WithTags("Health")
   .WithSummary("Health check")
   .WithDescription("Health probe endpoint. Returns 200 OK when the service is running. Includes Postgres connectivity status when configured.")
   .Produces<HealthzResponse>(StatusCodes.Status200OK);

// Validity check endpoint (lightweight utility for validating client-provided values)
//
// Route: POST /api/validity-check
// Body:  { \"value\": \"...\" }
// 200:   { \"isValid\": true, \"reason\": null }
app.MapPost("/api/validity-check", (ValidityCheckRequest request) =>
    {
        // Baseline checks (safe and generic)
        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return Results.Ok(new ValidityCheckResponse { IsValid = false, Reason = "Value must not be empty." });
        }

        // Data-quality: avoid unintended whitespace
        if (!string.Equals(request.Value, request.Value.Trim(), StringComparison.Ordinal))
        {
            return Results.Ok(new ValidityCheckResponse { IsValid = false, Reason = "Value must not have leading or trailing whitespace." });
        }

        // Block control characters
        if (request.Value.Any(char.IsControl))
        {
            return Results.Ok(new ValidityCheckResponse { IsValid = false, Reason = "Value contains invalid control characters." });
        }

        return Results.Ok(new ValidityCheckResponse { IsValid = true, Reason = null });
    })
    .WithName("ValidityCheck")
    .WithTags("Validation")
    .WithSummary("Validity check utility")
    .WithDescription("Performs a basic validity check on an input value. This endpoint can be extended as asset/tab validation rules are implemented.")
    .Produces<ValidityCheckResponse>(StatusCodes.Status200OK)
    .Accepts<ValidityCheckRequest>("application/json");

// Asset endpoints (Flyway V2 schema)
app.MapPost("/api/assets", async (
        CreateAssetRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateAsset");

        try
        {
            var result = await AssetFlows.CreateAssetAsync(
                new AssetFlows.CreateAssetFlowRequest(request),
                repository,
                logger,
                cancellationToken);

            return Results.Created($"/api/assets/{result.Asset.AssetId}", result.Asset);
        }
        catch (InvalidOperationException ex)
        {
            // DB not configured
            logger.LogWarning(ex, "CreateAsset failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            // Surface constraint errors with safe info (no secrets).
            logger.LogWarning(ex, "CreateAsset failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .WithName("CreateAsset")
    .WithTags("Assets")
    .WithSummary("Create asset")
    .WithDescription("Creates an Asset (BRD §6.1 header fields) and persists it to Postgres using the Flyway V2 schema.")
    .Accepts<CreateAssetRequest>("application/json")
    .Produces<AssetDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapGet("/api/assets/{assetId:long}", async (
        long assetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetAsset");

        try
        {
            var result = await AssetFlows.GetAssetAsync(assetId, repository, logger, cancellationToken);
            return result.Asset is null ? Results.NotFound() : Results.Ok(result.Asset);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetAsset failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("GetAssetById")
    .WithTags("Assets")
    .WithSummary("Get asset by ID")
    .WithDescription("Fetches an Asset by its database ID (excluding soft-deleted rows).")
    .Produces<AssetDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/assets/{assetId:long}", async (
        long assetId,
        UpdateAssetRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateAsset");

        try
        {
            var result = await AssetFlows.UpdateAssetAsync(
                new AssetFlows.UpdateAssetFlowRequest(assetId, request),
                repository,
                logger,
                cancellationToken);

            return result.Asset is null ? Results.NotFound() : Results.Ok(result.Asset);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateAsset failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("UpdateAsset")
    .WithTags("Assets")
    .WithSummary("Update asset")
    .WithDescription("Updates an existing Asset (does not allow changing Global Unique Asset ID).")
    .Accepts<UpdateAssetRequest>("application/json")
    .Produces<AssetDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets", async (
        [AsParameters] QueryAssetsRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("QueryAssets");

        try
        {
            var result = await AssetFlows.QueryAssetsAsync(request, repository, logger, cancellationToken);
            return Results.Ok(result.Assets);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "QueryAssets failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("QueryAssets")
    .WithTags("Assets")
    .WithSummary("Query assets")
    .WithDescription("Queries Assets with optional filters (siteId, assetGroup, processGroup, assetNameContains, permitEuId, globalUniqueAssetId).")
    .Produces<IReadOnlyList<AssetDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

//
// Master/reference endpoints (BRD §6.14)
//

// UOM Master
app.MapPost("/api/masters/uoms", async (
        CreateUomMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateUomMaster");

        try
        {
            var created = await MasterFlows.CreateUomAsync(request, repository, logger, cancellationToken);
            return Results.Created($"/api/masters/uoms/{created.UomId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateUomMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateUomMaster failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .WithName("CreateUomMaster")
    .WithTags("Masters")
    .WithSummary("Create UOM master")
    .WithDescription("Creates a UOM master row (BRD §6.14).")
    .Accepts<CreateUomMasterRequest>("application/json")
    .Produces<UomMasterDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapGet("/api/masters/uoms", async (
        [AsParameters] QueryMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("QueryUomMasters");

        try
        {
            var list = await MasterFlows.QueryUomsAsync(request, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "QueryUomMasters failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("QueryUomMasters")
    .WithTags("Masters")
    .WithSummary("Query UOM masters")
    .WithDescription("Queries UOM master rows (BRD §6.14). Optional: activeOnly.")
    .Produces<IReadOnlyList<UomMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/masters/uoms/{uomId:long}", async (
        long uomId,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetUomMaster");

        try
        {
            var uom = await repository.GetUomByIdAsync(uomId, cancellationToken);
            return uom is null ? Results.NotFound() : Results.Ok(uom);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetUomMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("GetUomMasterById")
    .WithTags("Masters")
    .WithSummary("Get UOM master by ID")
    .WithDescription("Fetches a single UOM master row by ID (excluding soft-deleted).")
    .Produces<UomMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/uoms/{uomId:long}", async (
        long uomId,
        UpdateUomMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateUomMaster");

        try
        {
            var updated = await MasterFlows.UpdateUomAsync(uomId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateUomMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("UpdateUomMaster")
    .WithTags("Masters")
    .WithSummary("Update UOM master")
    .WithDescription("Updates a UOM master row by ID (excluding soft-deleted).")
    .Accepts<UpdateUomMasterRequest>("application/json")
    .Produces<UomMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Reporting Program Master
app.MapPost("/api/masters/reporting-programs", async (
        CreateReportingProgramMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateReportingProgramMaster");

        try
        {
            var created = await MasterFlows.CreateReportingProgramAsync(request, repository, logger, cancellationToken);
            return Results.Created($"/api/masters/reporting-programs/{created.ReportingProgramId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateReportingProgramMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateReportingProgramMaster failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .WithName("CreateReportingProgramMaster")
    .WithTags("Masters")
    .WithSummary("Create reporting program master")
    .WithDescription("Creates a reporting program master row (BRD §6.14).")
    .Accepts<CreateReportingProgramMasterRequest>("application/json")
    .Produces<ReportingProgramMasterDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapGet("/api/masters/reporting-programs", async (
        [AsParameters] QueryMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("QueryReportingProgramMasters");

        try
        {
            var list = await MasterFlows.QueryReportingProgramsAsync(request, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "QueryReportingProgramMasters failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("QueryReportingProgramMasters")
    .WithTags("Masters")
    .WithSummary("Query reporting program masters")
    .WithDescription("Queries reporting program master rows (BRD §6.14). Optional: activeOnly.")
    .Produces<IReadOnlyList<ReportingProgramMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/masters/reporting-programs/{reportingProgramId:long}", async (
        long reportingProgramId,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetReportingProgramMaster");

        try
        {
            var item = await repository.GetReportingProgramByIdAsync(reportingProgramId, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetReportingProgramMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("GetReportingProgramMasterById")
    .WithTags("Masters")
    .WithSummary("Get reporting program master by ID")
    .WithDescription("Fetches a single reporting program master row by ID (excluding soft-deleted).")
    .Produces<ReportingProgramMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/reporting-programs/{reportingProgramId:long}", async (
        long reportingProgramId,
        UpdateReportingProgramMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateReportingProgramMaster");

        try
        {
            var updated = await MasterFlows.UpdateReportingProgramAsync(reportingProgramId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateReportingProgramMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("UpdateReportingProgramMaster")
    .WithTags("Masters")
    .WithSummary("Update reporting program master")
    .WithDescription("Updates a reporting program master row by ID (excluding soft-deleted).")
    .Accepts<UpdateReportingProgramMasterRequest>("application/json")
    .Produces<ReportingProgramMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Control Device Master
app.MapPost("/api/masters/control-devices", async (
        CreateControlDeviceMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateControlDeviceMaster");

        try
        {
            var created = await MasterFlows.CreateControlDeviceAsync(request, repository, logger, cancellationToken);
            return Results.Created($"/api/masters/control-devices/{created.ControlDeviceId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateControlDeviceMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateControlDeviceMaster failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .WithName("CreateControlDeviceMaster")
    .WithTags("Masters")
    .WithSummary("Create control device master")
    .WithDescription("Creates a control device master row (BRD §6.14).")
    .Accepts<CreateControlDeviceMasterRequest>("application/json")
    .Produces<ControlDeviceMasterDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapGet("/api/masters/control-devices", async (
        string? siteId,
        [AsParameters] QueryMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("QueryControlDeviceMasters");

        try
        {
            var list = await MasterFlows.QueryControlDevicesAsync(siteId, request, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "QueryControlDeviceMasters failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("QueryControlDeviceMasters")
    .WithTags("Masters")
    .WithSummary("Query control device masters")
    .WithDescription("Queries control device master rows (BRD §6.14). Optional: siteId, activeOnly.")
    .Produces<IReadOnlyList<ControlDeviceMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/masters/control-devices/{controlDeviceId:long}", async (
        long controlDeviceId,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetControlDeviceMaster");

        try
        {
            var item = await repository.GetControlDeviceByIdAsync(controlDeviceId, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetControlDeviceMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("GetControlDeviceMasterById")
    .WithTags("Masters")
    .WithSummary("Get control device master by ID")
    .WithDescription("Fetches a single control device master row by ID (excluding soft-deleted).")
    .Produces<ControlDeviceMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/control-devices/{controlDeviceId:long}", async (
        long controlDeviceId,
        UpdateControlDeviceMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateControlDeviceMaster");

        try
        {
            var updated = await MasterFlows.UpdateControlDeviceAsync(controlDeviceId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateControlDeviceMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("UpdateControlDeviceMaster")
    .WithTags("Masters")
    .WithSummary("Update control device master")
    .WithDescription("Updates a control device master row by ID (excluding soft-deleted).")
    .Accepts<UpdateControlDeviceMasterRequest>("application/json")
    .Produces<ControlDeviceMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Equation Master
app.MapPost("/api/masters/equations", async (
        CreateEquationMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateEquationMaster");

        try
        {
            var created = await MasterFlows.CreateEquationAsync(request, repository, logger, cancellationToken);
            return Results.Created($"/api/masters/equations/{created.EquationMasterId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateEquationMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateEquationMaster failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .WithName("CreateEquationMaster")
    .WithTags("Masters")
    .WithSummary("Create equation master")
    .WithDescription("Creates an equation master row (BRD §6.14).")
    .Accepts<CreateEquationMasterRequest>("application/json")
    .Produces<EquationMasterDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapGet("/api/masters/equations", async (
        [AsParameters] QueryMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("QueryEquationMasters");

        try
        {
            var list = await MasterFlows.QueryEquationsAsync(request, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "QueryEquationMasters failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("QueryEquationMasters")
    .WithTags("Masters")
    .WithSummary("Query equation masters")
    .WithDescription("Queries equation master rows (BRD §6.14).")
    .Produces<IReadOnlyList<EquationMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/masters/equations/{equationMasterId:long}", async (
        long equationMasterId,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetEquationMaster");

        try
        {
            var item = await repository.GetEquationByIdAsync(equationMasterId, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetEquationMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("GetEquationMasterById")
    .WithTags("Masters")
    .WithSummary("Get equation master by ID")
    .WithDescription("Fetches a single equation master row by ID (excluding soft-deleted).")
    .Produces<EquationMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/equations/{equationMasterId:long}", async (
        long equationMasterId,
        UpdateEquationMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateEquationMaster");

        try
        {
            var updated = await MasterFlows.UpdateEquationAsync(equationMasterId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateEquationMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("UpdateEquationMaster")
    .WithTags("Masters")
    .WithSummary("Update equation master")
    .WithDescription("Updates an equation master row by ID (excluding soft-deleted).")
    .Accepts<UpdateEquationMasterRequest>("application/json")
    .Produces<EquationMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Status Code Master
app.MapPost("/api/masters/status-codes", async (
        CreateStatusCodeMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateStatusCodeMaster");

        try
        {
            var created = await MasterFlows.CreateStatusCodeAsync(request, repository, logger, cancellationToken);
            return Results.Created($"/api/masters/status-codes/{created.StatusCodeId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateStatusCodeMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateStatusCodeMaster failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .WithName("CreateStatusCodeMaster")
    .WithTags("Masters")
    .WithSummary("Create status code master")
    .WithDescription("Creates a status code master row (BRD §6.14).")
    .Accepts<CreateStatusCodeMasterRequest>("application/json")
    .Produces<StatusCodeMasterDto>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapGet("/api/masters/status-codes", async (
        [AsParameters] QueryMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("QueryStatusCodeMasters");

        try
        {
            var list = await MasterFlows.QueryStatusCodesAsync(request, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "QueryStatusCodeMasters failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("QueryStatusCodeMasters")
    .WithTags("Masters")
    .WithSummary("Query status code masters")
    .WithDescription("Queries status code master rows (BRD §6.14). Optional: activeOnly.")
    .Produces<IReadOnlyList<StatusCodeMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/masters/status-codes/{statusCodeId:long}", async (
        long statusCodeId,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetStatusCodeMaster");

        try
        {
            var item = await repository.GetStatusCodeByIdAsync(statusCodeId, cancellationToken);
            return item is null ? Results.NotFound() : Results.Ok(item);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetStatusCodeMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("GetStatusCodeMasterById")
    .WithTags("Masters")
    .WithSummary("Get status code master by ID")
    .WithDescription("Fetches a single status code master row by ID (excluding soft-deleted).")
    .Produces<StatusCodeMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/status-codes/{statusCodeId:long}", async (
        long statusCodeId,
        UpdateStatusCodeMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateStatusCodeMaster");

        try
        {
            var updated = await MasterFlows.UpdateStatusCodeAsync(statusCodeId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateStatusCodeMaster failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("UpdateStatusCodeMaster")
    .WithTags("Masters")
    .WithSummary("Update status code master")
    .WithDescription("Updates a status code master row by ID (excluding soft-deleted).")
    .Accepts<UpdateStatusCodeMasterRequest>("application/json")
    .Produces<StatusCodeMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.Run();

static void LoadDotEnvIfPresent()
{
    try
    {
        var envPath = Path.Combine(AppContext.BaseDirectory, ".env");
        if (!File.Exists(envPath))
        {
            // Also try working directory (useful for local runs where base dir is /bin/Debug/...).
            envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
            if (!File.Exists(envPath))
            {
                return;
            }
        }

        foreach (var rawLine in File.ReadAllLines(envPath))
        {
            var line = rawLine.Trim();

            // Skip blanks/comments.
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            // Support "export KEY=VALUE" syntax.
            if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
            {
                line = line["export ".Length..].TrimStart();
            }

            var idx = line.IndexOf('=');
            if (idx <= 0)
            {
                continue;
            }

            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim();

            // Strip optional surrounding quotes.
            value = value.Trim().Trim('"').Trim('\'');

            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // Don't override already-provided real environment variables.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }
    catch
    {
        // Never fail app start due to .env parsing issues; health endpoint will report not configured if needed.
    }
}

/// <summary>
/// Request payload for the validity check endpoint.
/// </summary>
public sealed class ValidityCheckRequest
{
    /// <summary>
    /// The input value to validate.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "value is required")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Response payload for the validity check endpoint.
/// </summary>
public sealed class ValidityCheckResponse
{
    /// <summary>
    /// True when the value is considered valid.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Optional reason when invalid.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Response payload for the root health endpoint.
/// </summary>
/// <param name="Status">Overall service status.</param>
public sealed record HealthRootResponse(string Status);

/// <summary>
/// DB status portion of the health probe response.
/// </summary>
/// <param name="Configured">True when a DB connection string is configured.</param>
/// <param name="Ok">True when DB connectivity check succeeded.</param>
/// <param name="Error">Optional DB error message when unhealthy/unconfigured.</param>
public sealed record HealthzDbStatus(bool Configured, bool Ok, string? Error);

/// <summary>
/// Response payload for the /healthz health probe.
/// </summary>
/// <param name="Status">Overall service status.</param>
/// <param name="Db">Database diagnostics.</param>
public sealed record HealthzResponse(string Status, HealthzDbStatus Db);
