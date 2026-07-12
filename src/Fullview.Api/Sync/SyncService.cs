using Fullview.Domain.Entities;

namespace Fullview.Api.Sync;

/// <summary>The `/entities` protocol's actual logic: create (POST), last-write-wins
/// upsert (PUT), and full-list read (GET). No AWS types here on purpose — see
/// <see cref="ISyncStore"/>.</summary>
public sealed class SyncService(ISyncStore store)
{
    public Task<IReadOnlyList<SyncEntity>> GetAllAsync(CancellationToken ct) => store.GetAllAsync(ct);

    /// <summary>POST semantics: fails if an entity with this Id/EntityType already exists.
    /// Returns false (no write performed) when that's the case.</summary>
    public async Task<bool> CreateAsync(SyncEntity entity, CancellationToken ct)
    {
        var existing = await store.GetAsync(entity.SortKey, ct);
        if (existing is not null)
        {
            return false;
        }

        await store.PutAsync(entity, ct);
        return true;
    }

    /// <summary>PUT semantics: idempotent last-write-wins upsert. An incoming mutation only
    /// applies if it's at least as new as what's stored. Equal timestamps (a replayed
    /// mutation) re-apply the same data, which is a no-op in effect — that's what makes
    /// replay idempotent for free.</summary>
    public async Task ApplyMutationAsync(SyncEntity mutation, CancellationToken ct)
    {
        var existing = await store.GetAsync(mutation.SortKey, ct);
        if (existing is null || existing.UpdatedAt <= mutation.UpdatedAt)
        {
            await store.PutAsync(mutation, ct);
        }
    }
}
