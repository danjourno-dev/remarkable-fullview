using Fullview.Domain.Entities;

namespace Fullview.Api.Sync;

/// <summary>Storage abstraction behind <see cref="SyncService"/>. Kept separate from
/// DynamoDB so the LWW/idempotency/tombstone logic gets fast in-memory unit tests, while
/// <see cref="DynamoSyncStore"/> is the real Lambda-time implementation.</summary>
public interface ISyncStore
{
    Task<SyncEntity?> GetAsync(string sortKey, CancellationToken ct);

    Task PutAsync(SyncEntity entity, CancellationToken ct);

    /// <summary>Every entity for the user, including tombstones (Deleted=true) so a client
    /// doing a full resync learns about deletions too.</summary>
    Task<IReadOnlyList<SyncEntity>> GetAllAsync(CancellationToken ct);
}
