using System.Diagnostics.CodeAnalysis;

namespace DataAssetBackend.Infrastructure.Idempotency;

/// <summary>
/// Contract for storing and replaying responses for idempotent requests (X-Idempotency-Key).
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Attempts to get a completed stored response for the given composite key.
    /// </summary>
    /// <param name="compositeKey">A composite key (typically method + path + idempotency key).</param>
    /// <param name="response">The stored response, if present and completed.</param>
    /// <returns>True if a completed response exists; otherwise false.</returns>
    ValueTask<bool> TryGetCompletedAsync(string compositeKey, [NotNullWhen(true)] out StoredIdempotencyResponse? response);

    /// <summary>
    /// Gets an existing in-flight/completed operation for a key, or begins a new one.
    /// </summary>
    /// <param name="compositeKey">A composite key (typically method + path + idempotency key).</param>
    /// <param name="isNew">True if a new operation was created by this call; otherwise false.</param>
    /// <returns>A handle representing the single operation for this key.</returns>
    ValueTask<IdempotencyOperationHandle> GetOrBeginAsync(string compositeKey, out bool isNew);

    /// <summary>
    /// Clears a key from the store (used when a request fails before producing a response).
    /// </summary>
    /// <param name="compositeKey">Composite key to remove.</param>
    void Remove(string compositeKey);
}

/// <summary>
/// A completed response snapshot stored for idempotency replay.
/// </summary>
/// <param name="StatusCode">HTTP status code.</param>
/// <param name="Headers">Response headers captured at completion time.</param>
/// <param name="Body">Raw response body bytes.</param>
public sealed record StoredIdempotencyResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string[]> Headers,
    byte[] Body);

/// <summary>
/// Handle around a single in-flight or completed idempotent operation.
/// </summary>
public sealed class IdempotencyOperationHandle
{
    internal IdempotencyOperationHandle(Task<StoredIdempotencyResponse> task, TaskCompletionSource<StoredIdempotencyResponse>? tcs)
    {
        Task = task;
        _tcs = tcs;
    }

    private readonly TaskCompletionSource<StoredIdempotencyResponse>? _tcs;

    /// <summary>
    /// Completed response task for this idempotency key. Await this to replay.
    /// </summary>
    public Task<StoredIdempotencyResponse> Task { get; }

    /// <summary>
    /// True if this handle belongs to the request that is responsible for completing the operation.
    /// </summary>
    public bool CanComplete => _tcs is not null;

    /// <summary>
    /// Completes the operation with a stored response.
    /// </summary>
    public void SetResult(StoredIdempotencyResponse response)
    {
        if (_tcs is null) throw new InvalidOperationException("This handle cannot complete the operation.");
        _tcs.TrySetResult(response);
    }

    /// <summary>
    /// Completes the operation with an exception.
    /// </summary>
    public void SetException(Exception exception)
    {
        if (_tcs is null) throw new InvalidOperationException("This handle cannot complete the operation.");
        _tcs.TrySetException(exception);
    }
}
