using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.HttpOverrides;

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

// Health check endpoint
app.MapGet("/", () => new { message = "Healthy" })
   .WithName("Health")
   .WithTags("Health");

// Validity check endpoint (lightweight utility for validating client-provided values)
//
// Route: POST /api/validity-check
// Body:  { "value": "..." }
// 200:   { "isValid": true, "reason": null }
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
