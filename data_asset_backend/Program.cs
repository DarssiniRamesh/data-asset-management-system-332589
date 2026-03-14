using System.ComponentModel.DataAnnotations;
using DataAssetBackend.Infrastructure.Database;
using Microsoft.AspNetCore.HttpOverrides;

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
app.MapGet("/", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthRoot")
   .WithTags("Health")
   .WithSummary("Root health check")
   .WithDescription("Simple health check endpoint at the service root. Primarily for manual verification.");

// Healthz returns DB status *if* DB is configured.
// This is intentionally tolerant: if the DB isn't configured, we still return 200 for platform probes.
// (Production deployments should enforce migrations before API start via Flyway job/container.)
app.MapGet("/healthz", async (DatabaseConfigProvider dbConfigProvider, ILoggerFactory loggerFactory) =>
    {
        var logger = loggerFactory.CreateLogger("Healthz");
        var result = await DatabaseHealthCheckFlow.RunAsync(new DatabaseHealthCheckRequest(), dbConfigProvider, logger);

        // Always return 200 for health probe; include DB diagnostics in body.
        return Results.Ok(new
        {
            status = "ok",
            db = new
            {
                configured = result.IsConfigured,
                ok = result.IsHealthy,
                error = result.Error
            }
        });
    })
   .WithName("Healthz")
   .WithTags("Health")
   .WithSummary("Health check")
   .WithDescription("Health probe endpoint. Returns 200 OK when the service is running. Includes Postgres connectivity status when configured.");

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
