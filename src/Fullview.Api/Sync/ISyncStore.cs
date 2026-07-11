using Fullview.Domain.Entities;

namespace Fullview.Api.Sync;

/// <summary>Storage abstraction behind <see cref="SyncService"/>. Kept separate from
/// DynamoDB so the LWW/idempotency/tombstone logic gets fast in-memory unit tests, while
/// <see cref="DynamoSyncStore"/> is the real Lambda-time implementation.</summary>
public interface ISyncStore
{
    Task<SyncEntity?> GetAsync(string sortKey, CancellationToken ct);

    Task PutAsync(SyncEntity entity, CancellationToken ct);

    /// <summary>Entities with UpdatedAt strictly after <paramref name="cursor"/> (null/empty
    /// means "since the beginning"), ordered by UpdatedAt ascending, plus the cursor to use
    /// on the next call (the last item's UpdatedAt, or the input cursor unchanged if nothing
    /// new was found).</summary>
    Task<(IReadOnlyList<SyncEntity> Items, string Cursor)> QueryDeltaAsync(string? cursor, CancellationToken ct);
}
