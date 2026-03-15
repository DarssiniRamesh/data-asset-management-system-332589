using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using DataAssetBackend.Features.Assets;
using DataAssetBackend.Features.Legacy;
using DataAssetBackend.Features.Masters;
using DataAssetBackend.Features.Section4;
using DataAssetBackend.Infrastructure.Api;
using DataAssetBackend.Infrastructure.Auth;
using DataAssetBackend.Infrastructure.Database;
using DataAssetBackend.Infrastructure.Idempotency;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NSwag;
using NSwag.Generation.Processors.Security;

// Load .env (if present) *before* building configuration.
// Some hosted/preview environments provide secrets via a .env file rather than true process env vars.
// This ensures IConfiguration can resolve DATABASE_URL / ConnectionStrings__Default consistently.
LoadDotEnvIfPresent();

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddEndpointsApiExplorer();

// ---------------------------------------------------------------------
// Authentication/Authorization (JWT + RBAC)
// ---------------------------------------------------------------------
//
// Contract:
// - JWT_SIGNING_KEY must be provided via environment variable for non-dev usage.
//   For local/dev you may set any sufficiently long string.
// - JWT_ISSUER and JWT_AUDIENCE are optional; safe defaults are used.
var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? "data-asset-backend";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? "data-asset-frontend";
var jwtSigningKey = Environment.GetEnvironmentVariable("JWT_SIGNING_KEY") ?? "dev-insecure-signing-key-change-me";

// Register a token service (used by dev login endpoint and for validation parameters)
builder.Services.AddSingleton(new JwtTokenService(jwtIssuer, jwtAudience, jwtSigningKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Configure validation directly (avoid building a container during service registration).
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtSigningKey)),

            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
        };

        // Allow Authorization: Bearer {token}
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
    });

builder.Services.AddAuthorization(options =>
{
    // Roles (canonical values)
    const string admin = "Admin";
    const string editor = "Editor";
    const string viewer = "Viewer";

    static HashSet<string> GetUserRolesCanonical(ClaimsPrincipal user)
    {
        // Normalize any role claim value (e.g., "admin", "ADMIN", "Admin") to canonical casing.
        // This prevents unexpected 403s when clients mint lowercase roles.
        return user
            .FindAll(ClaimTypes.Role)
            .Select(r => r.Value?.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
    }

    static bool HasAnyRole(ClaimsPrincipal user, params string[] canonicalRoles)
    {
        var roles = GetUserRolesCanonical(user);
        foreach (var role in canonicalRoles)
        {
            if (roles.Contains(role.ToUpperInvariant()))
            {
                return true;
            }
        }

        return false;
    }

    // Default requirement: must be authenticated AND have one of the known roles.
    // This avoids accidentally granting access to tokens missing role claims.
    //
    // IMPORTANT:
    // - In normal runtime, we want a secure-by-default posture (everything requires auth unless explicitly AllowAnonymous).
    // - In the integration test host, many tests are focused on request validation (expect 400) and do not mint JWTs.
    //   For those tests we disable the fallback policy so endpoints are anonymous unless they call RequireAuthorization(...).
    //
    // This keeps protected APIs secured in real runtime while avoiding test brittleness.
    var isTesting = string.Equals(builder.Environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase);
    if (!isTesting)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => HasAnyRole(ctx.User, admin, editor, viewer))
            .Build();
    }

    // Policy mapping used by endpoints:
    // - Viewer: can read/query
    // - Editor: can create/update (and read)
    // - Admin: can delete and do everything
    options.AddPolicy("CanRead", p => p.RequireAssertion(ctx => HasAnyRole(ctx.User, admin, editor, viewer)));
    options.AddPolicy("CanWrite", p => p.RequireAssertion(ctx => HasAnyRole(ctx.User, admin, editor)));
    options.AddPolicy("AdminOnly", p => p.RequireAssertion(ctx => HasAnyRole(ctx.User, admin)));
});

// ---------------------------------------------------------------------
// OpenAPI/Swagger (NSwag) with Bearer auth
// ---------------------------------------------------------------------
builder.Services.AddOpenApiDocument(config =>
{
    config.Title = "Data Asset Backend API";
    config.Version = "1.0.0";
    config.Description = "Backend API for data asset management (POC). Includes validation utilities for asset configuration workflows.";

    // IMPORTANT:
    // Swagger UI only sends Authorization headers when the OpenAPI document defines a security scheme
    // and operations declare a security requirement. This enables the "Authorize" button and makes
    // "Try it out" send: Authorization: Bearer {token}
    config.AddSecurity("Bearer", new OpenApiSecurityScheme
    {
        Type = OpenApiSecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\""
    });

    config.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("Bearer"));
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
            // Dev/preview defaults:
            // - Allow localhost dev servers
            // - Allow Kavia preview hosts where the frontend runs on :3000 and backend on :3001
            //
            // Note: We use SetIsOriginAllowed (instead of AllowAnyOrigin) so this remains compatible
            // with credentialed requests if the frontend ever needs cookies/Authorization flows.
            static bool IsAllowedDevOrPreviewOrigin(string origin)
            {
                if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                // Local dev
                if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                {
                    return uri.Port == 3000 || uri.Port == 3001 || uri.Port == 7038;
                }

                // Hosted preview environment (example host: vscode-internal-34811-beta.beta01.cloud.kavia.ai)
                // In many cases frontend is :3000 and backend is :3001, but some preview/proxy setups
                // present the same hosts on default ports (443/80) while routing internally.
                //
                // NOTE:
                // We must match subdomains like: vscode-internal-18008-beta.beta01.cloud.kavia.ai
                // Those hosts end with ".cloud.kavia.ai" but do NOT necessarily contain "kavia.ai"
                // as a raw substring (because of the dot boundary).
                var host = uri.Host;
                var isKaviaPreviewHost =
                    host.EndsWith(".kavia.ai", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(host, "kavia.ai", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".cloud.kavia.ai", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(host, "cloud.kavia.ai", StringComparison.OrdinalIgnoreCase);

                if (isKaviaPreviewHost &&
                    (uri.Port == 3000 || uri.Port == 3001 || uri.Port == 443 || uri.Port == 80) &&
                    (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                return false;
            }

            policy.SetIsOriginAllowed(IsAllowedDevOrPreviewOrigin)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
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

// Idempotency (BRD §10.3): in-memory store + middleware for X-Idempotency-Key.
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

// Postgres data access services (Flyway V2 schema)
builder.Services.AddSingleton<NpgsqlConnectionFactory>();
builder.Services.AddSingleton<AssetRepository>();
builder.Services.AddSingleton<AssetCopyRepository>();
builder.Services.AddSingleton<AssetCopyLineageRepository>();
builder.Services.AddSingleton<MasterRepository>();
builder.Services.AddSingleton<Section4Repository>();

// Section 4 schema bootstrap (safety net for environments where Flyway/migrations were not executed).
builder.Services.AddSingleton<Section4SchemaBootstrapper>();

var app = builder.Build();

// Best-effort boot-time schema ensure for Section 4 tables.
// This prevents runtime 500s due to missing V3 tables in preview/dev environments.
//
// IMPORTANT:
// In the automated test host (Environment=Testing), we intentionally skip this step so
// tests can run in DB-less CI environments. Section4SchemaBootstrapper opens a DB
// connection, which would otherwise hard-fail startup if Postgres isn't available.
if (!string.Equals(app.Environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<Section4SchemaBootstrapper>();
        await bootstrapper.EnsureCreatedAsync(CancellationToken.None);
    }
    catch (InvalidOperationException)
    {
        // DB not configured: ignore (health checks will report).
    }
}

// Must be early in pipeline, before anything that relies on scheme/host (OpenAPI generation).
app.UseForwardedHeaders();

// ---------------------------------------------------------------------
// Robust CORS preflight handling
// ---------------------------------------------------------------------
//
// Why this exists:
// - The app uses a strict Authorization FallbackPolicy (authenticated + role required).
// - In some proxy/preview environments, OPTIONS preflight requests can fail to match endpoints
//   or can reach the auth/authorization middleware before the CORS middleware has a chance to
//   set headers. That results in 403 responses and broken browser requests.
// - Swagger UI also fails ("Failed to fetch") when preflight is blocked.
//
// This middleware explicitly handles preflight early and guarantees Access-Control-* headers
// are present for allowed origins.
static bool IsAllowedCorsOriginForPreflight(string origin)
{
    var allowedOriginsEnv = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
    var allowedOrigins = (allowedOriginsEnv ?? string.Empty)
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    if (allowedOrigins.Length > 0)
    {
        return allowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase));
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    // Local dev
    if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return uri.Port == 3000 || uri.Port == 3001 || uri.Port == 7038;
    }

    // Hosted preview (frontend typically :3000, backend typically :3001).
    // Match both the apex domains and any subdomains.
    var host = uri.Host;
    var isKaviaPreviewHost =
        host.EndsWith(".kavia.ai", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "kavia.ai", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".cloud.kavia.ai", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "cloud.kavia.ai", StringComparison.OrdinalIgnoreCase);

    if (isKaviaPreviewHost &&
        (uri.Port == 3000 || uri.Port == 3001 || uri.Port == 443 || uri.Port == 80) &&
        (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
    {
        return true;
    }

    return false;
}

app.Use(async (context, next) =>
{
    // Treat any OPTIONS request with an Origin header as CORS preflight.
    if (HttpMethods.IsOptions(context.Request.Method) &&
        context.Request.Headers.ContainsKey("Origin"))
    {
        var origin = context.Request.Headers.Origin.ToString();

        if (IsAllowedCorsOriginForPreflight(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Vary"] = "Origin";

            // Echo requested headers/method (strict browser compatibility).
            var reqHeaders = context.Request.Headers["Access-Control-Request-Headers"].ToString();
            context.Response.Headers["Access-Control-Allow-Headers"] =
                string.IsNullOrWhiteSpace(reqHeaders) ? "Content-Type, Authorization" : reqHeaders;

            var reqMethod = context.Request.Headers["Access-Control-Request-Method"].ToString();
            context.Response.Headers["Access-Control-Allow-Methods"] =
                string.IsNullOrWhiteSpace(reqMethod) ? "GET, POST, PUT, DELETE, OPTIONS" : reqMethod;

            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";

            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    await next();
});

// Ensure endpoint routing runs before CORS so CORS can evaluate endpoint metadata and apply headers.
app.UseRouting();

// Apply CORS before auth, so even auth failures include CORS headers (browser-visible).
app.UseCors("DefaultCors");

// Unified exception->HTTP mapping for all endpoints.
app.UseUnifiedExceptionHandling();

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

/*
 * ---------------------------------------------------------------------
 * OpenAPI/Swagger (NSwag)
 * ---------------------------------------------------------------------
 *
 * Swagger UI + OpenAPI documents must remain publicly accessible (no auth)
 * so that preview environments can load /docs without needing a JWT.
 *
 * IMPORTANT:
 * Do NOT register placeholder endpoints like `/docs/{*path}` because they intercept
 * Swagger UI static bundle requests (JS/CSS) and cause a blank screen:
 * `SwaggerUIBundle is not defined`.
 *
 * Correct behavior: let NSwag middleware serve `/docs` and its embedded assets.
 */

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

// Compatibility: many Swagger UI static bundles (and some reverse proxies) assume
// the Swashbuckle default JSON path: /swagger/v1/swagger.json.
// We serve the same NSwag-generated OpenAPI document at that path so /docs works
// even if it was configured for Swashbuckle.
app.UseOpenApi(settings =>
{
    settings.Path = "/swagger/v1/swagger.json";
    ConfigureOpenApiDocument(settings);
});

app.UseSwaggerUi(config =>
{
    // Serve Swagger UI at /docs.
    config.Path = "/docs";

    // Explicitly set the OpenAPI endpoint used by the UI.
    //
    // IMPORTANT:
    // Use root-anchored absolute paths so the UI does NOT try to resolve the spec relative
    // to a computed base path (which in some proxy setups can become `/v1`, causing a
    // failing GET `/v1`).
    config.SwaggerRoutes.Clear();
    config.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("/openapi.json", "v1"));
});

/*
 * ---------------------------------------------------------------------
 * Swagger UI anonymous access hardening
 * ---------------------------------------------------------------------
 *
 * Why:
 * - The service uses a strict Authorization FallbackPolicy.
 * - In some environments, NSwag's embedded assets under `/docs/*` still get challenged (401),
 *   preventing Swagger UI from loading (e.g., `SwaggerUIBundle is not defined`).
 *
 * Fix:
 * - When the request targets Swagger/OpenAPI paths, attach IAllowAnonymous metadata to the
 *   selected endpoint (if any). Authorization middleware respects IAllowAnonymous even when
 *   a fallback policy exists.
 */
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    var isSwaggerRelated =
        path.StartsWithSegments("/docs", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("/openapi.json", StringComparison.OrdinalIgnoreCase);

    if (isSwaggerRelated)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is not null && !endpoint.Metadata.OfType<IAllowAnonymous>().Any())
        {
            context.SetEndpoint(new Endpoint(
                endpoint.RequestDelegate,
                new EndpointMetadataCollection(endpoint.Metadata.Concat(new object[] { new AllowAnonymousAttribute() })),
                endpoint.DisplayName));
        }
    }

    await next();
});

/*
 * ---------------------------------------------------------------------
 * Authentication/Authorization
 * ---------------------------------------------------------------------
 */
app.UseAuthentication();
app.UseAuthorization();

/*
 * NOTE:
 * We intentionally do NOT use app.MapWhen(...).AllowAnonymous() here because MapWhen returns
 * IApplicationBuilder (middleware pipeline), not an endpoint convention builder.
 *
 * Swagger/OpenAPI are configured above before UseAuthentication/UseAuthorization, and the
 * middleware above additionally forces anonymous access for /docs assets.
 */

// Idempotency (BRD §10.3): handles X-Idempotency-Key for asset operations (e.g., Copy Asset).
app.UseMiddleware<IdempotencyKeyMiddleware>();

//
// Auth endpoints
//
app.MapPost("/api/auth/login", (DevLoginRequest request, JwtTokenService tokenService) =>
    {
        // Minimal dev login:
        // - No password validation
        // - Accepts a role and mints a JWT
        var role = (request.Role ?? "Viewer").Trim();
        if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Editor", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(role, "Viewer", StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Role)] = new[] { "role must be one of: Admin, Editor, Viewer." }
            });
        }

        var username = (request.Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Username)] = new[] { "username is required." }
            });
        }

        var expires = TimeSpan.FromHours(8);
        var token = tokenService.CreateToken(username, role, expires);

        return Results.Ok(new DevLoginResponse
        {
            AccessToken = token,
            ExpiresInSeconds = (int)expires.TotalSeconds,
            Username = username,
            Role = role
        });
    })
    .AllowAnonymous()
    .WithName("DevLogin")
    .WithTags("Auth")
    .WithSummary("Dev login (mints JWT)")
    .WithDescription("Development-only login endpoint that returns a signed JWT for the provided username and role (Admin/Editor/Viewer).")
    .Accepts<DevLoginRequest>("application/json")
    .Produces<DevLoginResponse>(StatusCodes.Status200OK)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest);

// Compatibility redirect:
// Some Swagger UI configurations (or older cached `/docs` assets) may try to fetch `/v1` as the
// OpenAPI document. In this service, `/v1` is not an API prefix and is protected by the fallback
// auth policy, so that request fails with 401 and breaks the UI.
// We redirect it to the actual OpenAPI document endpoint.
app.MapGet("/v1", () => Results.Redirect("/openapi.json", permanent: false))
   .AllowAnonymous()
   .WithName("OpenApiLegacyV1Redirect")
   .WithTags("OpenAPI")
   .WithSummary("Legacy OpenAPI redirect")
   .WithDescription("Compatibility redirect so Swagger UI never breaks if it attempts to load the spec from /v1. Redirects to /openapi.json.")
   .Produces(StatusCodes.Status302Found);

// Health check endpoints
// Note: the platform/preview health probe expects `/healthz`.
// We keep `/` as a friendly default while ensuring `/healthz` returns HTTP 200.
app.MapGet("/", () =>
    {
        // Use a typed response so OpenAPI accurately documents the response shape.
        return Results.Ok(new HealthRootResponse(Status: "ok"));
    })
   .AllowAnonymous()
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
   .AllowAnonymous()
   .WithName("Healthz")
   .WithTags("Health")
   .WithSummary("Health check")
   .WithDescription("Health probe endpoint. Returns 200 OK when the service is running. Includes Postgres connectivity status when configured.")
   .Produces<HealthzResponse>(StatusCodes.Status200OK);

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
    .AllowAnonymous()
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

        // BRD FR-05 tab-level validations (cross-field/conditional)
        RequestValidation.ValidateAndThrow(request, nameof(CreateAssetRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("CreateAsset")
    .WithTags("Assets")
    .WithSummary("Create asset")
    .WithDescription("Creates an Asset (BRD §6.1 header fields) and persists it to Postgres using the Flyway V2 schema.")
    .Accepts<CreateAssetRequest>("application/json")
    .Produces<AssetDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
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
    .RequireAuthorization("CanRead")
    .WithName("GetAssetById")
    .WithTags("Assets")
    .WithSummary("Get asset by ID")
    .WithDescription("Fetches an Asset by its database ID (excluding soft-deleted rows).")
    .Produces<AssetDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapPut("/api/assets/{assetId:long}", async (
        long assetId,
        UpdateAssetRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateAsset");

        // BRD FR-05 tab-level validations (cross-field/conditional)
        RequestValidation.ValidateAndThrow(request, nameof(UpdateAssetRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("UpdateAsset")
    .WithTags("Assets")
    .WithSummary("Update asset")
    .WithDescription("Updates an existing Asset (does not allow changing Global Unique Asset ID).")
    .Accepts<UpdateAssetRequest>("application/json")
    .Produces<AssetDto>(StatusCodes.Status200OK)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
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
    .RequireAuthorization("CanRead")
    .WithName("QueryAssets")
    .WithTags("Assets")
    .WithSummary("Query assets")
    .WithDescription("Queries Assets with optional filters (siteId, assetGroup, processGroup, assetNameContains, permitEuId, globalUniqueAssetId).")
    .Produces<IReadOnlyList<AssetDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// FR-04: Delete Asset (soft-delete)
app.MapDelete("/api/assets/{assetId:long}", async (
        long assetId,
        [FromBody] DeleteAssetRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("DeleteAsset");

        try
        {
            var result = await AssetFlows.DeleteAssetAsync(
                new AssetFlows.DeleteAssetFlowRequest(assetId, request),
                repository,
                logger,
                cancellationToken);

            return result.Deleted ? Results.NoContent() : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "DeleteAsset failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            // Conservative: map integrity/constraint errors the same way as other endpoints.
            logger.LogWarning(ex, "DeleteAsset failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("AdminOnly")
    .WithName("DeleteAsset")
    .WithTags("Assets")
    .WithSummary("Delete asset")
    .WithDescription("Soft-deletes an Asset by ID using the existing is_deleted flag (and soft-deletes BRD-evidenced dependent records).")
    .Accepts<DeleteAssetRequest>("application/json")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

// ---------------------------------------------------------------------
// Copy Asset (BRD FR-03; Copy and Lineage Capture Requirements - BRD §6.13)
// ---------------------------------------------------------------------
app.MapPost("/api/assets/{assetId:long}/copy", async (
        long assetId,
        CopyAssetRequest request,
        AssetCopyRepository copyRepository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CopyAsset");

        try
        {
            var result = await AssetCopyFlows.CopyAssetAsync(
                new AssetCopyFlows.CopyAssetFlowRequest(assetId, request),
                copyRepository,
                logger,
                cancellationToken);

            return Results.Created($"/api/assets/{result.Response.TargetAsset.AssetId}", result.Response);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            // DB not configured
            logger.LogWarning(ex, "CopyAsset failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CopyAsset failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CopyAsset")
    .WithTags("Assets")
    .WithSummary("Copy asset")
    .WithDescription("Copies an asset and BRD-evidenced related records into a new target asset, recording lineage (BRD §6.13; table: asset_copy_lineage).")
    .Accepts<CopyAssetRequest>("application/json")
    .Produces<CopyAssetResponse>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

// ---------------------------------------------------------------------
// Asset child endpoints (BRD-evidenced asset-scoped child resources)
// ---------------------------------------------------------------------
//
// Fix for frontend 404s:
// - GET /api/assets/{assetId}/input-parameters
// - GET /api/assets/{assetId}/control-device-mappings
// - GET /api/assets/{assetId}/properties
//
app.MapGet("/api/assets/{assetId:long}/input-parameters", async (
        long assetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListInputParameters");

        try
        {
            var list = await AssetChildFlows.ListInputParametersAsync(assetId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListInputParameters failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListInputParameters")
    .WithTags("Assets")
    .WithSummary("List input parameters")
    .WithDescription("Lists input parameter rows for the given asset.")
    .Produces<IReadOnlyList<InputParameterDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/control-device-mappings", async (
        long assetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListControlDeviceMappings");

        try
        {
            var list = await AssetChildFlows.ListControlDeviceMappingsAsync(assetId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListControlDeviceMappings failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListControlDeviceMappings")
    .WithTags("Assets")
    .WithSummary("List control device mappings")
    .WithDescription("Lists control device mapping rows for the given asset.")
    .Produces<IReadOnlyList<ControlDeviceMappingDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/properties", async (
        long assetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListAssetProperties");

        try
        {
            var list = await AssetChildFlows.ListAssetPropertiesAsync(assetId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListAssetProperties failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListAssetProperties")
    .WithTags("Assets")
    .WithSummary("List asset properties")
    .WithDescription("Lists asset property rows for the given asset.")
    .Produces<IReadOnlyList<AssetPropertyDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// ---------------------------------------------------------------------
// Missing asset child endpoints (previously implemented in flows/repo but
// not registered here -> runtime 404 + missing from Swagger/OpenAPI)
// ---------------------------------------------------------------------

// Status logs
app.MapPost("/api/assets/{assetId:long}/status-logs", async (
        long assetId,
        CreateAssetStatusLogRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateAssetStatusLog");

        try
        {
            var created = await AssetChildFlows.CreateAssetStatusLogAsync(assetId, request, repository, logger, cancellationToken);
            return Results.Created($"/api/assets/{assetId}/status-logs/{created.AssetStatusLogId}", created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateAssetStatusLog failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CreateAssetStatusLog")
    .WithTags("Assets")
    .WithSummary("Create asset status log")
    .WithDescription("Creates a status log entry for the asset (BRD §6.2).")
    .Accepts<CreateAssetStatusLogRequest>("application/json")
    .Produces<AssetStatusLogDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/status-logs", async (
        long assetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListAssetStatusLogs");

        try
        {
            var list = await AssetChildFlows.ListAssetStatusLogsAsync(assetId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListAssetStatusLogs failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListAssetStatusLogs")
    .WithTags("Assets")
    .WithSummary("List asset status logs")
    .WithDescription("Lists status log entries for the asset (BRD §6.2).")
    .Produces<IReadOnlyList<AssetStatusLogDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/status-logs/{assetStatusLogId:long}", async (
        long assetId,
        long assetStatusLogId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetAssetStatusLogById");

        try
        {
            var row = await AssetChildFlows.GetAssetStatusLogByIdAsync(assetId, assetStatusLogId, repository, logger, cancellationToken);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetAssetStatusLogById failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("GetAssetStatusLogById")
    .WithTags("Assets")
    .WithSummary("Get asset status log by ID")
    .WithDescription("Gets a specific status log entry by ID under an asset scope.")
    .Produces<AssetStatusLogDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/assets/{assetId:long}/status-logs/{assetStatusLogId:long}", async (
        long assetId,
        long assetStatusLogId,
        UpdateAssetStatusLogRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateAssetStatusLog");

        try
        {
            var updated = await AssetChildFlows.UpdateAssetStatusLogAsync(assetId, assetStatusLogId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateAssetStatusLog failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("UpdateAssetStatusLog")
    .WithTags("Assets")
    .WithSummary("Update asset status log")
    .WithDescription("Updates a status log entry by ID under an asset scope.")
    .Accepts<UpdateAssetStatusLogRequest>("application/json")
    .Produces<AssetStatusLogDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Additional IDs
app.MapPost("/api/assets/{assetId:long}/additional-ids", async (
        long assetId,
        CreateAdditionalAssetIdRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateAdditionalAssetId");

        try
        {
            var created = await AssetChildFlows.CreateAdditionalAssetIdAsync(assetId, request, repository, logger, cancellationToken);
            return Results.Created($"/api/assets/{assetId}/additional-ids/{created.AdditionalAssetId}", created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateAdditionalAssetId failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CreateAdditionalAssetId")
    .WithTags("Assets")
    .WithSummary("Create additional asset ID")
    .WithDescription("Creates an additional ID record under an asset (BRD §6.3).")
    .Accepts<CreateAdditionalAssetIdRequest>("application/json")
    .Produces<AdditionalAssetIdDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/additional-ids", async (
        long assetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListAdditionalAssetIds");

        try
        {
            var list = await AssetChildFlows.ListAdditionalAssetIdsAsync(assetId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListAdditionalAssetIds failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListAdditionalAssetIds")
    .WithTags("Assets")
    .WithSummary("List additional asset IDs")
    .WithDescription("Lists additional ID records under an asset (BRD §6.3).")
    .Produces<IReadOnlyList<AdditionalAssetIdDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/additional-ids/{additionalAssetId:long}", async (
        long assetId,
        long additionalAssetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetAdditionalAssetIdById");

        try
        {
            var row = await AssetChildFlows.GetAdditionalAssetIdByIdAsync(assetId, additionalAssetId, repository, logger, cancellationToken);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetAdditionalAssetIdById failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("GetAdditionalAssetIdById")
    .WithTags("Assets")
    .WithSummary("Get additional asset ID by ID")
    .WithDescription("Gets an additional ID record by ID under an asset scope.")
    .Produces<AdditionalAssetIdDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/assets/{assetId:long}/additional-ids/{additionalAssetId:long}", async (
        long assetId,
        long additionalAssetId,
        UpdateAdditionalAssetIdRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateAdditionalAssetId");

        try
        {
            var updated = await AssetChildFlows.UpdateAdditionalAssetIdAsync(assetId, additionalAssetId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateAdditionalAssetId failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("UpdateAdditionalAssetId")
    .WithTags("Assets")
    .WithSummary("Update additional asset ID")
    .WithDescription("Updates an additional ID record by ID under an asset scope.")
    .Accepts<UpdateAdditionalAssetIdRequest>("application/json")
    .Produces<AdditionalAssetIdDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Reporting attribute mappings
app.MapPost("/api/assets/{assetId:long}/reporting-attribute-mappings", async (
        long assetId,
        CreateReportingAttributeMappingRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateReportingAttributeMapping");

        try
        {
            var created = await AssetChildFlows.CreateReportingAttributeMappingAsync(assetId, request, repository, logger, cancellationToken);
            return Results.Created($"/api/assets/{assetId}/reporting-attribute-mappings/{created.ReportingAttributeMappingId}", created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateReportingAttributeMapping failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CreateReportingAttributeMapping")
    .WithTags("Assets")
    .WithSummary("Create reporting attribute mapping")
    .WithDescription("Creates a reporting attribute mapping row under an asset (BRD §6.7).")
    .Accepts<CreateReportingAttributeMappingRequest>("application/json")
    .Produces<ReportingAttributeMappingDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/reporting-attribute-mappings", async (
        long assetId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListReportingAttributeMappings");

        try
        {
            var list = await AssetChildFlows.ListReportingAttributeMappingsAsync(assetId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListReportingAttributeMappings failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListReportingAttributeMappings")
    .WithTags("Assets")
    .WithSummary("List reporting attribute mappings")
    .WithDescription("Lists reporting attribute mapping rows under an asset (BRD §6.7).")
    .Produces<IReadOnlyList<ReportingAttributeMappingDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/reporting-attribute-mappings/{reportingAttributeMappingId:long}", async (
        long assetId,
        long reportingAttributeMappingId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("GetReportingAttributeMappingById");

        try
        {
            var row = await AssetChildFlows.GetReportingAttributeMappingByIdAsync(assetId, reportingAttributeMappingId, repository, logger, cancellationToken);
            return row is null ? Results.NotFound() : Results.Ok(row);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "GetReportingAttributeMappingById failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("GetReportingAttributeMappingById")
    .WithTags("Assets")
    .WithSummary("Get reporting attribute mapping by ID")
    .WithDescription("Gets a reporting attribute mapping row by ID under an asset scope.")
    .Produces<ReportingAttributeMappingDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/assets/{assetId:long}/reporting-attribute-mappings/{reportingAttributeMappingId:long}", async (
        long assetId,
        long reportingAttributeMappingId,
        UpdateReportingAttributeMappingRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateReportingAttributeMapping");

        try
        {
            var updated = await AssetChildFlows.UpdateReportingAttributeMappingAsync(assetId, reportingAttributeMappingId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateReportingAttributeMapping failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("UpdateReportingAttributeMapping")
    .WithTags("Assets")
    .WithSummary("Update reporting attribute mapping")
    .WithDescription("Updates a reporting attribute mapping row by ID under an asset scope.")
    .Accepts<UpdateReportingAttributeMappingRequest>("application/json")
    .Produces<ReportingAttributeMappingDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Parent input mappings
app.MapPost("/api/assets/{assetId:long}/input-parameters/{childInputParameterId:long}/parent-input-mappings", async (
        long assetId,
        long childInputParameterId,
        CreateParentInputMappingRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateParentInputMapping");

        try
        {
            var created = await AssetChildFlows.CreateParentInputMappingAsync(assetId, childInputParameterId, request, repository, logger, cancellationToken);
            return Results.Created($"/api/assets/{assetId}/input-parameters/{childInputParameterId}/parent-input-mappings/{created.ParentInputMappingId}", created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateParentInputMapping failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CreateParentInputMapping")
    .WithTags("Assets")
    .WithSummary("Create parent input mapping")
    .WithDescription("Creates a parent input mapping for a child input parameter (BRD §6.6).")
    .Accepts<CreateParentInputMappingRequest>("application/json")
    .Produces<ParentInputMappingDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/input-parameters/{childInputParameterId:long}/parent-input-mappings", async (
        long assetId,
        long childInputParameterId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListParentInputMappings");

        try
        {
            var list = await AssetChildFlows.ListParentInputMappingsAsync(assetId, childInputParameterId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListParentInputMappings failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListParentInputMappings")
    .WithTags("Assets")
    .WithSummary("List parent input mappings")
    .WithDescription("Lists parent input mappings for a child input parameter (BRD §6.6).")
    .Produces<IReadOnlyList<ParentInputMappingDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/assets/{assetId:long}/input-parameters/{childInputParameterId:long}/parent-input-mappings/{parentInputMappingId:long}", async (
        long assetId,
        long childInputParameterId,
        long parentInputMappingId,
        UpdateParentInputMappingRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateParentInputMapping");

        try
        {
            var updated = await AssetChildFlows.UpdateParentInputMappingAsync(
                assetId,
                childInputParameterId,
                parentInputMappingId,
                request,
                repository,
                logger,
                cancellationToken);

            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateParentInputMapping failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("UpdateParentInputMapping")
    .WithTags("Assets")
    .WithSummary("Update parent input mapping")
    .WithDescription("Updates a parent input mapping row by ID under an asset scope (BRD §6.6).")
    .Accepts<UpdateParentInputMappingRequest>("application/json")
    .Produces<ParentInputMappingDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Throughput equations
app.MapPost("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/throughput-equations", async (
        long assetId,
        long inputParameterId,
        CreateThroughputEquationRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateThroughputEquation");

        try
        {
            var created = await AssetChildFlows.CreateThroughputEquationAsync(assetId, inputParameterId, request, repository, logger, cancellationToken);
            return Results.Created($"/api/assets/{assetId}/input-parameters/{inputParameterId}/throughput-equations/{created.ThroughputEquationId}", created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateThroughputEquation failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CreateThroughputEquation")
    .WithTags("Assets")
    .WithSummary("Create throughput equation")
    .WithDescription("Creates a throughput equation row under an input parameter (BRD §6.8).")
    .Accepts<CreateThroughputEquationRequest>("application/json")
    .Produces<ThroughputEquationDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/throughput-equations", async (
        long assetId,
        long inputParameterId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListThroughputEquations");

        try
        {
            var list = await AssetChildFlows.ListThroughputEquationsAsync(assetId, inputParameterId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListThroughputEquations failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListThroughputEquations")
    .WithTags("Assets")
    .WithSummary("List throughput equations")
    .WithDescription("Lists throughput equation rows under an input parameter (BRD §6.8).")
    .Produces<IReadOnlyList<ThroughputEquationDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/throughput-equations/{throughputEquationId:long}", async (
        long assetId,
        long inputParameterId,
        long throughputEquationId,
        UpdateThroughputEquationRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateThroughputEquation");

        try
        {
            var updated = await AssetChildFlows.UpdateThroughputEquationAsync(assetId, inputParameterId, throughputEquationId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateThroughputEquation failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("UpdateThroughputEquation")
    .WithTags("Assets")
    .WithSummary("Update throughput equation")
    .WithDescription("Updates a throughput equation row by ID under an input parameter.")
    .Accepts<UpdateThroughputEquationRequest>("application/json")
    .Produces<ThroughputEquationDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

/*
 * ---------------------------------------------------------------------
 * Input Parameter nested child resources (asset scoped)
 * ---------------------------------------------------------------------
 */

/*
 * ---------------------------------------------------------------------
 * EF source mappings (asset scoped)
 * ---------------------------------------------------------------------
 *
 * Root bug:
 * - Only GET was mapped for /ef-source-mappings, but the frontend calls POST to create.
 * - ASP.NET Core returns 405 when the path matches but the verb is not mapped.
 *
 * Fix:
 * - Add POST mapping for CreateEfSourceMapping so Swagger includes it and runtime accepts it.
 */

// List EF source mappings
app.MapGet("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/ef-source-mappings", async (
        long assetId,
        long inputParameterId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListEfSourceMappings");

        try
        {
            var list = await AssetChildFlows.ListEfSourceMappingsAsync(assetId, inputParameterId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListEfSourceMappings failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListEfSourceMappings")
    .WithTags("Assets")
    .WithSummary("List EF source mappings")
    .WithDescription("Lists EF source mapping rows under an input parameter (asset scoped).")
    .Produces<IReadOnlyList<EfSourceMappingDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// Create EF source mapping (FIX for 405)
app.MapPost("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/ef-source-mappings", async (
        long assetId,
        long inputParameterId,
        CreateEfSourceMappingRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateEfSourceMapping");

        try
        {
            var created = await AssetChildFlows.CreateEfSourceMappingAsync(assetId, inputParameterId, request, repository, logger, cancellationToken);
            return Results.Created(
                $"/api/assets/{assetId}/input-parameters/{inputParameterId}/ef-source-mappings/{created.EfSourceMappingId}",
                created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateEfSourceMapping failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateEfSourceMapping failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CreateEfSourceMapping")
    .WithTags("Assets")
    .WithSummary("Create EF source mapping")
    .WithDescription("Creates an EF source mapping row under an input parameter (asset scoped).")
    .Accepts<CreateEfSourceMappingRequest>("application/json")
    .Produces<EfSourceMappingDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

// Data input values
app.MapPost("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/data-input-values", async (
        long assetId,
        long inputParameterId,
        CreateDataInputValueRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateDataInputValue");

        try
        {
            var created = await AssetChildFlows.CreateDataInputValueAsync(assetId, inputParameterId, request, repository, logger, cancellationToken);
            return Results.Created($"/api/assets/{assetId}/input-parameters/{inputParameterId}/data-input-values/{created.DataInputValueId}", created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateDataInputValue failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("CreateDataInputValue")
    .WithTags("Assets")
    .WithSummary("Create data input value")
    .WithDescription("Creates a data input value row under an input parameter (BRD §6.9).")
    .Accepts<CreateDataInputValueRequest>("application/json")
    .Produces<DataInputValueDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/data-input-values", async (
        long assetId,
        long inputParameterId,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("ListDataInputValues");

        try
        {
            var list = await AssetChildFlows.ListDataInputValuesAsync(assetId, inputParameterId, repository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListDataInputValues failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("ListDataInputValues")
    .WithTags("Assets")
    .WithSummary("List data input values")
    .WithDescription("Lists data input value rows under an input parameter (BRD §6.9).")
    .Produces<IReadOnlyList<DataInputValueDto>>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/assets/{assetId:long}/input-parameters/{inputParameterId:long}/data-input-values/{dataInputValueId:long}", async (
        long assetId,
        long inputParameterId,
        long dataInputValueId,
        UpdateDataInputValueRequest request,
        AssetRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateDataInputValue");

        try
        {
            var updated = await AssetChildFlows.UpdateDataInputValueAsync(assetId, inputParameterId, dataInputValueId, request, repository, logger, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateDataInputValue failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("UpdateDataInputValue")
    .WithTags("Assets")
    .WithSummary("Update data input value")
    .WithDescription("Updates a data input value row by ID under an input parameter.")
    .Accepts<UpdateDataInputValueRequest>("application/json")
    .Produces<DataInputValueDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

//
// Copy lineage endpoints (BRD §6.13; table: asset_copy_lineage)
//

// Record a copy lineage row
app.MapPost("/api/asset-copy-lineage", async (
        CreateAssetCopyLineageRequest request,
        AssetCopyLineageRepository lineageRepository,
        AssetRepository assetRepository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateAssetCopyLineage");

        try
        {
            var created = await AssetCopyLineageFlows.CreateAsync(
                request,
                lineageRepository,
                assetRepository,
                logger,
                cancellationToken);

            return Results.Created($"/api/asset-copy-lineage/{created.AssetCopyLineageId}", created);
        }
        catch (AssetRepository.EntityNotFoundException)
        {
            return Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateAssetCopyLineage failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            // Constraint violations can occur (e.g., FK violations) if DB is configured but references invalid.
            logger.LogWarning(ex, "CreateAssetCopyLineage failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .WithName("CreateAssetCopyLineage")
    .WithTags("Assets")
    .WithSummary("Create asset copy lineage record")
    .WithDescription("Records copy/lineage capture data for an asset copy operation (BRD §6.13; table: asset_copy_lineage).")
    .Accepts<CreateAssetCopyLineageRequest>("application/json")
    .Produces<AssetCopyLineageDto>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

// Query lineage by supported filters
app.MapGet("/api/asset-copy-lineage", async (
        [AsParameters] QueryAssetCopyLineageRequest request,
        AssetCopyLineageRepository lineageRepository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("QueryAssetCopyLineage");

        try
        {
            var list = await AssetCopyLineageFlows.QueryAsync(request, lineageRepository, logger, cancellationToken);
            return Results.Ok(list);
        }
        catch (AssetCopyLineageFlows.MissingFiltersException ex)
        {
            return Results.Problem(
                title: "Missing filters",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "QueryAssetCopyLineage failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .WithName("QueryAssetCopyLineage")
    .WithTags("Assets")
    .WithSummary("Query asset copy lineage")
    .WithDescription("Queries copy lineage records by copyOperationId, sourceAssetId, and/or targetAssetId. Multiple filters are ANDed. At least one filter is required.")
    .Produces<IReadOnlyList<AssetCopyLineageDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// ---------------------------------------------------------------------
// Masters endpoints (reference data)
// ---------------------------------------------------------------------
//
// IMPORTANT:
// These endpoints must be explicitly mapped; NSwag only documents endpoints that are
// registered in the ASP.NET Core endpoint pipeline.
//
// Tags: "Masters" so they appear grouped in Swagger UI.
//
// NOTE:
// The current MasterFlows layer supports create/update/list operations. There are no
// "get by id" flow methods at this time, so we intentionally only map the supported
// endpoints to keep the build and OpenAPI consistent.

// -------------------------
// UOM Masters
// -------------------------
app.MapPost("/api/masters/uoms", async (
        CreateUomMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateUomMaster");

        RequestValidation.ValidateAndThrow(request, nameof(CreateUomMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("CreateUomMaster")
    .WithTags("Masters")
    .WithSummary("Create UOM master")
    .WithDescription("Creates a Unit of Measure (UOM) master record.")
    .Accepts<CreateUomMasterRequest>("application/json")
    .Produces<UomMasterDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
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
    .RequireAuthorization("CanRead")
    .WithName("QueryUomMasters")
    .WithTags("Masters")
    .WithSummary("Query UOM masters")
    .WithDescription("Lists UOM masters with optional filters (activeOnly, limit).")
    .Produces<IReadOnlyList<UomMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/uoms/{uomId:long}", async (
        long uomId,
        UpdateUomMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateUomMaster");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateUomMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("UpdateUomMaster")
    .WithTags("Masters")
    .WithSummary("Update UOM master")
    .WithDescription("Updates an existing UOM master record by ID.")
    .Accepts<UpdateUomMasterRequest>("application/json")
    .Produces<UomMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// -------------------------
// Reporting Program Masters
// -------------------------
app.MapPost("/api/masters/reporting-programs", async (
        CreateReportingProgramMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateReportingProgramMaster");

        RequestValidation.ValidateAndThrow(request, nameof(CreateReportingProgramMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("CreateReportingProgramMaster")
    .WithTags("Masters")
    .WithSummary("Create reporting program master")
    .WithDescription("Creates a Reporting Program master record.")
    .Accepts<CreateReportingProgramMasterRequest>("application/json")
    .Produces<ReportingProgramMasterDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
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
    .RequireAuthorization("CanRead")
    .WithName("QueryReportingProgramMasters")
    .WithTags("Masters")
    .WithSummary("Query reporting program masters")
    .WithDescription("Lists reporting program masters with optional filters (activeOnly, limit).")
    .Produces<IReadOnlyList<ReportingProgramMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/reporting-programs/{reportingProgramId:long}", async (
        long reportingProgramId,
        UpdateReportingProgramMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateReportingProgramMaster");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateReportingProgramMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("UpdateReportingProgramMaster")
    .WithTags("Masters")
    .WithSummary("Update reporting program master")
    .WithDescription("Updates an existing reporting program master record by ID.")
    .Accepts<UpdateReportingProgramMasterRequest>("application/json")
    .Produces<ReportingProgramMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// -------------------------
// Control Device Masters
// -------------------------
app.MapPost("/api/masters/control-devices", async (
        CreateControlDeviceMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateControlDeviceMaster");

        RequestValidation.ValidateAndThrow(request, nameof(CreateControlDeviceMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("CreateControlDeviceMaster")
    .WithTags("Masters")
    .WithSummary("Create control device master")
    .WithDescription("Creates a Control Device master record.")
    .Accepts<CreateControlDeviceMasterRequest>("application/json")
    .Produces<ControlDeviceMasterDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
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
    .RequireAuthorization("CanRead")
    .WithName("QueryControlDeviceMasters")
    .WithTags("Masters")
    .WithSummary("Query control device masters")
    .WithDescription("Lists control device masters with optional filters (siteId, activeOnly, limit).")
    .Produces<IReadOnlyList<ControlDeviceMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/control-devices/{controlDeviceId:long}", async (
        long controlDeviceId,
        UpdateControlDeviceMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateControlDeviceMaster");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateControlDeviceMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("UpdateControlDeviceMaster")
    .WithTags("Masters")
    .WithSummary("Update control device master")
    .WithDescription("Updates an existing control device master record by ID.")
    .Accepts<UpdateControlDeviceMasterRequest>("application/json")
    .Produces<ControlDeviceMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// -------------------------
// Equation Masters
// -------------------------
app.MapPost("/api/masters/equations", async (
        CreateEquationMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateEquationMaster");

        RequestValidation.ValidateAndThrow(request, nameof(CreateEquationMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("CreateEquationMaster")
    .WithTags("Masters")
    .WithSummary("Create equation master")
    .WithDescription("Creates an Equation master record.")
    .Accepts<CreateEquationMasterRequest>("application/json")
    .Produces<EquationMasterDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
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
    .RequireAuthorization("CanRead")
    .WithName("QueryEquationMasters")
    .WithTags("Masters")
    .WithSummary("Query equation masters")
    .WithDescription("Lists equation masters with optional filters (activeOnly, limit).")
    .Produces<IReadOnlyList<EquationMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/equations/{equationMasterId:long}", async (
        long equationMasterId,
        UpdateEquationMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateEquationMaster");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateEquationMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("UpdateEquationMaster")
    .WithTags("Masters")
    .WithSummary("Update equation master")
    .WithDescription("Updates an existing equation master record by ID.")
    .Accepts<UpdateEquationMasterRequest>("application/json")
    .Produces<EquationMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// -------------------------
// Status Code Masters
// -------------------------
app.MapPost("/api/masters/status-codes", async (
        CreateStatusCodeMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("CreateStatusCodeMaster");

        RequestValidation.ValidateAndThrow(request, nameof(CreateStatusCodeMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("CreateStatusCodeMaster")
    .WithTags("Masters")
    .WithSummary("Create status code master")
    .WithDescription("Creates a Status Code master record.")
    .Accepts<CreateStatusCodeMasterRequest>("application/json")
    .Produces<StatusCodeMasterDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
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
    .RequireAuthorization("CanRead")
    .WithName("QueryStatusCodeMasters")
    .WithTags("Masters")
    .WithSummary("Query status code masters")
    .WithDescription("Lists status code masters with optional filters (activeOnly, limit).")
    .Produces<IReadOnlyList<StatusCodeMasterDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.MapPut("/api/masters/status-codes/{statusCodeId:long}", async (
        long statusCodeId,
        UpdateStatusCodeMasterRequest request,
        MasterRepository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("UpdateStatusCodeMaster");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateStatusCodeMasterRequest));

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
    .RequireAuthorization("CanWrite")
    .WithName("UpdateStatusCodeMaster")
    .WithTags("Masters")
    .WithSummary("Update status code master")
    .WithDescription("Updates an existing status code master record by ID.")
    .Accepts<UpdateStatusCodeMasterRequest>("application/json")
    .Produces<StatusCodeMasterDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

// ---------------------------------------------------------------------
// BRD §4 Section 4 module endpoints
// ---------------------------------------------------------------------
//
// These must be explicitly mapped; having repository/flows/models is not enough.
// Frontend contract (authoritative): GET /api/section4/site-profiles?limit=200 (optionally siteId)
//
// Note: Keep query parameter names aligned with frontend usage (`siteId`, `limit`) to avoid
// subtle casing/binding mismatches across environments.
var section4 = app.MapGroup("/api/section4")
    .WithTags("Section4");

/*
 * NOTE:
 * Section 4 endpoints must be explicitly registered here; otherwise the frontend receives 404
 * and the endpoints do not appear in Swagger/OpenAPI.
 */

// -------------------------
// Site Profiles
// -------------------------
section4.MapGet("/site-profiles", async (
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.ListSiteProfiles");

        try
        {
            var rows = await Section4Flows.ListSiteProfilesAsync(siteId, limit, repository, logger, cancellationToken);
            return Results.Ok(rows);
        }
        catch (InvalidOperationException ex)
        {
            // DB not configured
            logger.LogWarning(ex, "ListSiteProfiles failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("Section4_ListSiteProfiles")
    .WithSummary("List site profiles")
    .WithDescription("Lists Section 4 Site Profiles with optional siteId filter and limit.")
    .Produces<IReadOnlyList<SiteProfileDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

section4.MapPost("/site-profiles", async (
        CreateSiteProfileRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.CreateSiteProfile");

        // Ensure 400s on missing required fields.
        RequestValidation.ValidateAndThrow(request, nameof(CreateSiteProfileRequest));

        try
        {
            var created = await repository.CreateSiteProfileAsync(request, cancellationToken);
            return Results.Created($"/api/section4/site-profiles/{created.SiteProfileId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateSiteProfile failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateSiteProfile failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("Section4_CreateSiteProfile")
    .WithSummary("Create site profile")
    .WithDescription("Creates a Section 4 Site Profile.")
    .Accepts<CreateSiteProfileRequest>("application/json")
    .Produces<SiteProfileDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

section4.MapPut("/site-profiles/{siteProfileId:long}", async (
        long siteProfileId,
        UpdateSiteProfileRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.UpdateSiteProfile");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateSiteProfileRequest));

        try
        {
            var updated = await repository.UpdateSiteProfileAsync(siteProfileId, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateSiteProfile failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("Section4_UpdateSiteProfile")
    .WithSummary("Update site profile")
    .WithDescription("Updates a Section 4 Site Profile by ID.")
    .Accepts<UpdateSiteProfileRequest>("application/json")
    .Produces<SiteProfileDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

section4.MapDelete("/site-profiles/{siteProfileId:long}", async (
        long siteProfileId,
        [FromBody] DeleteAssetRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.DeleteSiteProfile");

        // Reuse existing delete request contract (modifiedBy, correlationId).
        RequestValidation.ValidateAndThrow(request, nameof(DeleteAssetRequest));

        try
        {
            var deleted = await repository.DeleteSiteProfileAsync(
                siteProfileId,
                modifiedBy: request.ModifiedBy,
                correlationId: request.CorrelationId,
                cancellationToken: cancellationToken);

            return deleted ? Results.NoContent() : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "DeleteSiteProfile failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "DeleteSiteProfile failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("AdminOnly")
    .WithName("Section4_DeleteSiteProfile")
    .WithSummary("Delete site profile")
    .WithDescription("Soft-deletes a Section 4 Site Profile by ID.")
    .Accepts<DeleteAssetRequest>("application/json")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

// -------------------------
// Chemical Raw Materials (FIX for frontend 404: /api/section4/chemical-raw-materials)
// -------------------------
section4.MapGet("/chemical-raw-materials", async (
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.ListChemicalRawMaterials");

        try
        {
            var rows = await Section4Flows.ListChemicalRawMaterialsAsync(siteId, limit, repository, logger, cancellationToken);
            return Results.Ok(rows);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListChemicalRawMaterials failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("Section4_ListChemicalRawMaterials")
    .WithSummary("List chemical raw materials")
    .WithDescription("Lists Section 4 Chemical Raw Materials with optional siteId filter and limit.")
    .Produces<IReadOnlyList<ChemicalRawMaterialDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

section4.MapPost("/chemical-raw-materials", async (
        CreateChemicalRawMaterialRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.CreateChemicalRawMaterial");

        RequestValidation.ValidateAndThrow(request, nameof(CreateChemicalRawMaterialRequest));

        try
        {
            var created = await repository.CreateChemicalRawMaterialAsync(request, cancellationToken);
            return Results.Created($"/api/section4/chemical-raw-materials/{created.ChemicalRawMaterialId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateChemicalRawMaterial failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateChemicalRawMaterial failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("Section4_CreateChemicalRawMaterial")
    .WithSummary("Create chemical raw material")
    .WithDescription("Creates a Section 4 Chemical Raw Material.")
    .Accepts<CreateChemicalRawMaterialRequest>("application/json")
    .Produces<ChemicalRawMaterialDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

section4.MapPut("/chemical-raw-materials/{chemicalRawMaterialId:long}", async (
        long chemicalRawMaterialId,
        UpdateChemicalRawMaterialRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.UpdateChemicalRawMaterial");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateChemicalRawMaterialRequest));

        try
        {
            var updated = await repository.UpdateChemicalRawMaterialAsync(chemicalRawMaterialId, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateChemicalRawMaterial failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("Section4_UpdateChemicalRawMaterial")
    .WithSummary("Update chemical raw material")
    .WithDescription("Updates a Section 4 Chemical Raw Material by ID.")
    .Accepts<UpdateChemicalRawMaterialRequest>("application/json")
    .Produces<ChemicalRawMaterialDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

section4.MapDelete("/chemical-raw-materials/{chemicalRawMaterialId:long}", async (
        long chemicalRawMaterialId,
        [FromBody] DeleteAssetRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.DeleteChemicalRawMaterial");

        RequestValidation.ValidateAndThrow(request, nameof(DeleteAssetRequest));

        try
        {
            var deleted = await repository.DeleteChemicalRawMaterialAsync(
                chemicalRawMaterialId,
                modifiedBy: request.ModifiedBy,
                correlationId: request.CorrelationId,
                cancellationToken: cancellationToken);

            return deleted ? Results.NoContent() : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "DeleteChemicalRawMaterial failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "DeleteChemicalRawMaterial failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("AdminOnly")
    .WithName("Section4_DeleteChemicalRawMaterial")
    .WithSummary("Delete chemical raw material")
    .WithDescription("Soft-deletes a Section 4 Chemical Raw Material by ID.")
    .Accepts<DeleteAssetRequest>("application/json")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

// -------------------------
// Chemical SDS (FIX for frontend 404: /api/section4/chemical-sds)
// -------------------------
section4.MapGet("/chemical-sds", async (
        string? siteId,
        int? limit,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.ListChemicalSds");

        try
        {
            var rows = await Section4Flows.ListChemicalSdsAsync(siteId, limit, repository, logger, cancellationToken);
            return Results.Ok(rows);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "ListChemicalSds failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanRead")
    .WithName("Section4_ListChemicalSds")
    .WithSummary("List chemical SDS")
    .WithDescription("Lists Section 4 Chemical SDS rows with optional siteId filter and limit.")
    .Produces<IReadOnlyList<ChemicalSdsDto>>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

section4.MapPost("/chemical-sds", async (
        CreateChemicalSdsRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.CreateChemicalSds");

        RequestValidation.ValidateAndThrow(request, nameof(CreateChemicalSdsRequest));

        try
        {
            var created = await repository.CreateChemicalSdsAsync(request, cancellationToken);
            return Results.Created($"/api/section4/chemical-sds/{created.ChemicalSdsId}", created);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "CreateChemicalSds failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "CreateChemicalSds failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("Section4_CreateChemicalSds")
    .WithSummary("Create chemical SDS")
    .WithDescription("Creates a Section 4 Chemical SDS row.")
    .Accepts<CreateChemicalSdsRequest>("application/json")
    .Produces<ChemicalSdsDto>(StatusCodes.Status201Created)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

section4.MapPut("/chemical-sds/{chemicalSdsId:long}", async (
        long chemicalSdsId,
        UpdateChemicalSdsRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.UpdateChemicalSds");

        RequestValidation.ValidateAndThrow(request, nameof(UpdateChemicalSdsRequest));

        try
        {
            var updated = await repository.UpdateChemicalSdsAsync(chemicalSdsId, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "UpdateChemicalSds failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    })
    .RequireAuthorization("CanWrite")
    .WithName("Section4_UpdateChemicalSds")
    .WithSummary("Update chemical SDS")
    .WithDescription("Updates a Section 4 Chemical SDS row by ID.")
    .Accepts<UpdateChemicalSdsRequest>("application/json")
    .Produces<ChemicalSdsDto>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

section4.MapDelete("/chemical-sds/{chemicalSdsId:long}", async (
        long chemicalSdsId,
        [FromBody] DeleteAssetRequest request,
        Section4Repository repository,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken) =>
    {
        var logger = loggerFactory.CreateLogger("Section4.DeleteChemicalSds");

        RequestValidation.ValidateAndThrow(request, nameof(DeleteAssetRequest));

        try
        {
            var deleted = await repository.DeleteChemicalSdsAsync(
                chemicalSdsId,
                modifiedBy: request.ModifiedBy,
                correlationId: request.CorrelationId,
                cancellationToken: cancellationToken);

            return deleted ? Results.NoContent() : Results.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "DeleteChemicalSds failed: DB not configured.");
            return Results.Problem(
                title: "Database not configured",
                detail: ex.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (PostgresException ex)
        {
            logger.LogWarning(ex, "DeleteChemicalSds failed due to database constraint error.");
            return Results.Problem(
                title: "Database constraint error",
                detail: ex.MessageText,
                statusCode: StatusCodes.Status409Conflict);
        }
    })
    .RequireAuthorization("AdminOnly")
    .WithName("Section4_DeleteChemicalSds")
    .WithSummary("Delete chemical SDS")
    .WithDescription("Soft-deletes a Section 4 Chemical SDS row by ID.")
    .Accepts<DeleteAssetRequest>("application/json")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound)
    .ProducesValidationProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status409Conflict);

// ---------------------------------------------------------------------
// BRD §9 Legacy/Observed API inventory endpoints (compatibility shims)
// ---------------------------------------------------------------------
app.MapLegacyObservedApiEndpoints();

/*
 * ---------------------------------------------------------------------
 * CORS preflight support (OPTIONS fallback)
 * ---------------------------------------------------------------------
 *
 * IMPORTANT:
 * Do NOT register a catch-all endpoint route like `MapMethods("{*path}", new[] { "OPTIONS" }, ...)`.
 * Even when restricted to OPTIONS, that endpoint can still match other verbs and cause ASP.NET Core
 * to return 405 for GET/POST routes (including NSwag's embedded Swagger UI assets under /docs/*).
 *
 * Instead, use middleware that only handles true OPTIONS requests that weren't already handled.
 */
app.Use(async (context, next) =>
{
    if (HttpMethods.IsOptions(context.Request.Method))
    {
        // If an endpoint was matched, let it handle the request (more specific wins).
        if (context.GetEndpoint() is null)
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }
    }

    await next();
});

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

