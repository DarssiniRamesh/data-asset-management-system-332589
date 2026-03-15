using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DataAssetBackend.Infrastructure.Api;

/// <summary>
/// Middleware that ensures a correlation ID is present for every request, and that it is:
/// - read from an incoming request header when provided,
/// - generated when missing/invalid,
/// - stored on <see cref="HttpContext.Items"/> for downstream handlers,
/// - added to the response header,
/// - and pushed into the logging scope for consistent structured logs.
/// </summary>
/// <remarks>
/// <para><b>Flow name:</b> CorrelationIdPropagationFlow</para>
/// <para><b>Single entrypoint:</b> <see cref="InvokeAsync"/></para>
/// <para><b>Contract</b></para>
/// <list type="bullet">
///   <item>
///     <description><b>Inputs:</b> HTTP request + optional header <c>X-Correlation-Id</c>.</description>
///   </item>
///   <item>
///     <description>
///       <b>Outputs:</b> A non-empty correlation id string is available to downstream code via
///       <see cref="CorrelationIdHttpContextExtensions.GetCorrelationId"/> and is returned in the response header
///       <c>X-Correlation-Id</c>.
///     </description>
///   </item>
///   <item>
///     <description><b>Errors:</b> None thrown by this middleware; invalid/missing IDs are replaced.</description>
///   </item>
///   <item>
///     <description><b>Side effects:</b> Adds/overwrites response header <c>X-Correlation-Id</c>; adds a log scope property <c>correlationId</c>.</description>
///   </item>
/// </list>
/// <para><b>Invariants</b></para>
/// <list type="bullet">
///   <item><description>The correlation id is always a trimmed non-empty string.</description></item>
///   <item><description>Response header is always set (even on failures), because it is attached via <c>Response.OnStarting</c>.</description></item>
/// </list>
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    /// <summary>
    /// Canonical HTTP header used for correlation ID propagation.
    /// </summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// HttpContext.Items key used to store the correlation id for downstream handlers.
    /// </summary>
    public const string HttpContextItemKey = "CorrelationId";

    private static readonly Regex AllowedHeaderValue = new("^[a-zA-Z0-9._:-]{1,128}$", RegexOptions.Compiled);

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Resolves/creates a correlation id for the request and ensures propagation to downstream code and response headers.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        // Store for downstream access.
        context.Items[HttpContextItemKey] = correlationId;

        // If Activity is enabled (OpenTelemetry/diagnostics), align it for easier cross-system correlation.
        // We set the TraceState? no; just ensure trace id is present in logs via scope and allow Activity.Current.Id usage.
        if (Activity.Current is not null && string.IsNullOrWhiteSpace(Activity.Current.DisplayName))
        {
            Activity.Current.DisplayName = $"{context.Request.Method} {context.Request.Path}";
        }

        // Always include the correlation id in the response, even when downstream throws,
        // by using OnStarting (runs for both normal and exceptional pipeline completions).
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        // Enrich structured logs for everything downstream.
        using (_logger.BeginScope(new Dictionary<string, object>
               {
                   ["correlationId"] = correlationId,
                   ["httpMethod"] = context.Request.Method,
                   ["path"] = context.Request.Path.ToString(),
               }))
        {
            await _next(context);
        }
    }

    private string ResolveCorrelationId(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString()?.Trim();

        if (!string.IsNullOrWhiteSpace(incoming) && AllowedHeaderValue.IsMatch(incoming))
        {
            return incoming;
        }

        // Generate a new one (trace-friendly, URL-safe, no braces).
        // Use 32 hex chars.
        var generated = Guid.NewGuid().ToString("N");

        if (!string.IsNullOrWhiteSpace(incoming))
        {
            // Provided but invalid; log at debug to avoid noisy logs, but keep it observable when needed.
            _logger.LogDebug("Invalid correlation id header received; replacing. headerName={HeaderName} received={Received} generated={Generated}",
                HeaderName,
                incoming,
                generated);
        }

        return generated;
    }
}
