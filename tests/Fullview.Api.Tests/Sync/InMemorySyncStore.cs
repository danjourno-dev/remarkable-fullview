using Fullview.Api.Sync;
using Fullview.Domain.Entities;

namespace Fullview.Api.Tests.Sync;

/// <summary>Test double for <see cref="ISyncStore"/> — mirrors DynamoSyncStore's
/// contract (point lookup by sort key, full-list read) without any AWS dependency, so
/// <see cref="Fullview.Api.Sync.SyncService"/>'s conflict-resolution logic gets fast,
/// deterministic unit tests.</summary>
public sealed class InMemorySyncStore : ISyncStore
{
    private readonly Dictionary<string, SyncEntity> _itemsBySortKey = [];

    public Task<SyncEntity?> GetAsync(string sortKey, CancellationToken ct) =>
        Task.FromResult(_itemsBySortKey.GetValueOrDefault(sortKey));

    public Task PutAsync(SyncEntity entity, CancellationToken ct)
    {
        _itemsBySortKey[entity.SortKey] = entity;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SyncEntity>> GetAllAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SyncEntity>>(_itemsBySortKey.Values.OrderBy(e => e.UpdatedAt).ToList());
}
