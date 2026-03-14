using System.Collections.Concurrent;

namespace DataAssetBackend.Infrastructure.Idempotency;

/// <summary>
/// In-memory implementation of <see cref="IIdempotencyStore"/>.
/// </summary>
/// <remarks>
/// This is suitable for single-instance deployments and tests. For multi-instance deployments,
/// a distributed store (e.g., Redis) would be needed to share idempotency state.
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private sealed class Entry
    {
        public Entry(TaskCompletionSource<StoredIdempotencyResponse> tcs)
        {
            Tcs = tcs;
        }

        public TaskCompletionSource<StoredIdempotencyResponse> Tcs { get; }
        public StoredIdempotencyResponse? Completed { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public ValueTask<bool> TryGetCompletedAsync(string compositeKey, out StoredIdempotencyResponse? response)
    {
        if (_entries.TryGetValue(compositeKey, out var entry) && entry.Completed is not null)
        {
            response = entry.Completed;
            return ValueTask.FromResult(true);
        }

        response = null;
        return ValueTask.FromResult(false);
    }

    public ValueTask<IdempotencyOperationHandle> GetOrBeginAsync(string compositeKey, out bool isNew)
    {
        // Run continuations asynchronously so middleware can safely complete without executing continuations inline.
        var createdTcs = new TaskCompletionSource<StoredIdempotencyResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        var entry = _entries.GetOrAdd(compositeKey, _ => new Entry(createdTcs));
        isNew = ReferenceEquals(entry.Tcs, createdTcs);

        // If we didn't create the entry, we cannot complete it.
        var handle = isNew
            ? new IdempotencyOperationHandle(entry.Tcs.Task, entry.Tcs)
            : new IdempotencyOperationHandle(entry.Tcs.Task, tcs: null);

        return ValueTask.FromResult(handle);
    }

    public void Remove(string compositeKey)
    {
        _entries.TryRemove(compositeKey, out _);
    }

    /// <summary>
    /// Marks an entry as completed. This is invoked by the middleware after the response is produced.
    /// </summary>
    internal void SetCompleted(string compositeKey, StoredIdempotencyResponse response)
    {
        if (_entries.TryGetValue(compositeKey, out var entry))
        {
            entry.Completed = response;
        }
    }
}
