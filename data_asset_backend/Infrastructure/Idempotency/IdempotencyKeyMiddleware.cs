using System.Text;
using Microsoft.AspNetCore.Http;

namespace DataAssetBackend.Infrastructure.Idempotency;

/// <summary>
/// Middleware to provide idempotency for unsafe HTTP methods using the X-Idempotency-Key request header.
/// </summary>
/// <remarks>
/// Behavior:
/// - If X-Idempotency-Key is missing: request proceeds normally.
/// - If present:
///   - The first request for a given composite key executes and its full HTTP response (status + headers + body)
///     is captured and stored.
///   - Duplicate requests with the same key return the previously stored response and do not execute downstream.
///   - Concurrent duplicates wait for the first request to complete and then replay the stored response.
/// </remarks>
public sealed class IdempotencyKeyMiddleware
{
    private const string IdempotencyHeaderName = "X-Idempotency-Key";
    private const string ReplayHeaderName = "X-Idempotency-Replayed";

    private readonly RequestDelegate _next;
    private readonly ILogger<IdempotencyKeyMiddleware> _logger;

    public IdempotencyKeyMiddleware(RequestDelegate next, ILogger<IdempotencyKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    private static bool IsUnsafeMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static bool IsAssetOperationPath(PathString path) =>
        path.StartsWithSegments("/api/assets", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/asset-copy-lineage", StringComparison.OrdinalIgnoreCase);

    private static string BuildCompositeKey(HttpRequest request, string idempotencyKey)
    {
        // Scope to endpoint identity; do not include host to keep stable behind proxies.
        // Include query string for safety (rare for POST, but possible).
        return $"{request.Method}:{request.Path}{request.QueryString}:{idempotencyKey}";
    }

    private static IReadOnlyDictionary<string, string[]> CaptureHeaders(IHeaderDictionary headers)
    {
        // Exclude hop-by-hop headers and Content-Length (will be recalculated).
        var dict = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in headers)
        {
            if (string.Equals(kvp.Key, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(kvp.Key, "Content-Length", StringComparison.OrdinalIgnoreCase)) continue;

            dict[kvp.Key] = kvp.Value.ToArray();
        }
        return dict;
    }

    private static void ApplyHeaders(HttpResponse response, IReadOnlyDictionary<string, string[]> headers)
    {
        response.Headers.Clear();
        foreach (var kvp in headers)
        {
            response.Headers[kvp.Key] = kvp.Value;
        }
    }

    private static async Task WriteStoredResponseAsync(HttpContext context, StoredIdempotencyResponse stored, bool isReplay)
    {
        context.Response.StatusCode = stored.StatusCode;
        ApplyHeaders(context.Response, stored.Headers);

        // Helpful for tests/diagnostics; not required by BRD but harmless and additive.
        context.Response.Headers[ReplayHeaderName] = isReplay ? "true" : "false";

        if (stored.Body.Length > 0)
        {
            await context.Response.Body.WriteAsync(stored.Body, 0, stored.Body.Length, context.RequestAborted);
        }
    }

    /// <summary>
    /// Handles the request and applies idempotency behavior when the X-Idempotency-Key header is present.
    /// </summary>
    public async Task InvokeAsync(HttpContext context, IIdempotencyStore store)
    {
        // Only apply to unsafe methods + asset operations.
        if (!IsUnsafeMethod(context.Request.Method) || !IsAssetOperationPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var idempotencyKey = context.Request.Headers[IdempotencyHeaderName].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await _next(context);
            return;
        }

        var compositeKey = BuildCompositeKey(context.Request, idempotencyKey);

        // Fast path: completed response already available.
        if (await store.TryGetCompletedAsync(compositeKey, out var completed))
        {
            _logger.LogInformation("Idempotency replay (completed). key={CompositeKey}", compositeKey);
            await WriteStoredResponseAsync(context, completed!, isReplay: true);
            return;
        }

        // Slow path: get or begin operation; duplicates will wait on the same task.
        var handle = await store.GetOrBeginAsync(compositeKey, out var isNew);
        if (!isNew)
        {
            _logger.LogInformation("Idempotency replay (await in-flight). key={CompositeKey}", compositeKey);
            var stored = await handle.Task;
            await WriteStoredResponseAsync(context, stored, isReplay: true);
            return;
        }

        // This request is responsible for executing downstream and capturing response.
        var originalBody = context.Response.Body;
        await using var captureStream = new MemoryStream();
        context.Response.Body = captureStream;

        try
        {
            await _next(context);

            // Capture response body
            await context.Response.Body.FlushAsync(context.RequestAborted);
            var bodyBytes = captureStream.ToArray();

            // Restore original body and write out captured response for this original request
            context.Response.Body = originalBody;
            var stored = new StoredIdempotencyResponse(
                StatusCode: context.Response.StatusCode,
                Headers: CaptureHeaders(context.Response.Headers),
                Body: bodyBytes);

            // Persist completion.
            if (store is InMemoryIdempotencyStore mem)
            {
                mem.SetCompleted(compositeKey, stored);
            }

            handle.SetResult(stored);

            // Important: write the stored response bytes to the actual response stream
            // (headers/status are already set on context.Response).
            context.Response.Headers[ReplayHeaderName] = "false";
            if (bodyBytes.Length > 0)
            {
                await originalBody.WriteAsync(bodyBytes, 0, bodyBytes.Length, context.RequestAborted);
            }
        }
        catch (Exception ex)
        {
            // If the request fails before producing a valid response, remove the key so callers can retry.
            _logger.LogWarning(ex, "Idempotency operation failed; removing key. key={CompositeKey}", compositeKey);

            store.Remove(compositeKey);
            handle.SetException(ex);

            // Ensure response body stream is restored before rethrowing.
            context.Response.Body = originalBody;
            throw;
        }
        finally
        {
            // Ensure we restore body even if downstream wrote nothing.
            context.Response.Body = originalBody;
        }
    }
}
