using Fullview.Api.Sync;
using Fullview.Domain.Entities;

namespace Fullview.Api.Tests.Sync;

/// <summary>Test double for <see cref="ISyncStore"/> — mirrors DynamoSyncStore's
/// contract (point lookup by sort key, delta query ordered by UpdatedAt) without any AWS
/// dependency, so <see cref="Fullview.Api.Sync.SyncService"/>'s conflict-resolution logic
/// gets fast, deterministic unit tests.</summary>
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

    public Task<(IReadOnlyList<SyncEntity> Items, string Cursor)> QueryDeltaAsync(string? cursor, CancellationToken ct)
    {
        var since = string.IsNullOrEmpty(cursor) ? DateTimeOffset.MinValue : DateTimeOffset.Parse(cursor);

        var items = _itemsBySortKey.Values
            .Where(e => e.UpdatedAt > since)
            .OrderBy(e => e.UpdatedAt)
            .ToList();

        var newCursor = items.Count > 0 ? items[^1].UpdatedAt.ToString("O") : (cursor ?? since.ToString("O"));

        return Task.FromResult<(IReadOnlyList<SyncEntity>, string)>((items, newCursor));
    }
}
