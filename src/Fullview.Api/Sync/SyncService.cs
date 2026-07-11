using Fullview.Domain.Entities;
using Fullview.Domain.Sync;

namespace Fullview.Api.Sync;

/// <summary>The `/sync` protocol's actual logic (B5): apply an outbox idempotently with
/// last-write-wins conflict resolution, then return the delta since the caller's cursor.
/// No AWS types here on purpose — see <see cref="ISyncStore"/>.</summary>
public sealed class SyncService(ISyncStore store)
{
    public async Task<SyncResponse> ApplyAndPullAsync(SyncRequest request, CancellationToken ct)
    {
        foreach (var mutation in request.Outbox)
        {
            var existing = await store.GetAsync(mutation.SortKey, ct);

            // LWW: an incoming mutation only applies if it's at least as new as what's
            // stored. Equal timestamps (a replayed mutation) re-apply the same data, which
            // is a no-op in effect — that's what makes replay idempotent for free.
            if (existing is null || existing.UpdatedAt <= mutation.UpdatedAt)
            {
                await store.PutAsync(mutation, ct);
            }
        }

        var (items, cursor) = await store.QueryDeltaAsync(request.Cursor, ct);
        return new SyncResponse { Cursor = cursor, Delta = items.ToList() };
    }
}
