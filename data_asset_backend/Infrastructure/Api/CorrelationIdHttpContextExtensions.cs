using Microsoft.AspNetCore.Http;

namespace DataAssetBackend.Infrastructure.Api;

/// <summary>
/// Helpers for accessing the correlation id established by <see cref="CorrelationIdMiddleware"/>.
/// </summary>
public static class CorrelationIdHttpContextExtensions
{
    /// <summary>
    /// Gets the current request correlation id as set by <see cref="CorrelationIdMiddleware"/>.
    /// If middleware was not registered, falls back to <see cref="HttpContext.TraceIdentifier"/>.
/// </summary>
    public static string GetCorrelationId(this HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out var value) &&
            value is string s &&
            !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return httpContext.TraceIdentifier;
    }
}
