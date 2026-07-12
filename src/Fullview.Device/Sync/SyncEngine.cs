using Fullview.Device.Logging;
using Fullview.Device.Storage;

namespace Fullview.Device.Sync;

public enum SyncOutcome
{
    Succeeded,
    Failed
}

/// <summary>
/// Result of one <see cref="SyncEngine.SyncOnceAsync"/> call. <see cref="Changed"/> tells a
/// caller whether there's anything new to show — background-triggered syncs use it to skip a
/// full e-ink redraw when nothing moved, while user-facing syncs (manual tap, startup) ignore
/// it and always refresh.
/// </summary>
public sealed record SyncResult(SyncOutcome Outcome, bool Changed);

/// <summary>
/// Drives one outbox-drain-and-full-resync cycle against `/entities`. Deliberately has no
/// retry/backoff loop of its own: the caller decides when to invoke it (app open, manual
/// tap, or a headless timer gated on "is there anything to push"), and a failed attempt just
/// leaves the outbox untouched for the next trigger to retry — see
/// tools/device/systemd/fullview-sync.timer and Program.cs for the three call sites.
///
/// Pushes the outbox one entity at a time (PUT per item, deleting each outbox row only after
/// its PUT succeeds) rather than one batched call — a mid-drain network drop loses nothing
/// already acknowledged by the server. Then does a full `GET /entities` and applies it
/// wholesale: there's no cursor, so this always converges regardless of clock skew between
/// this device and whatever else writes to the store (see PROGRESS.md's Decisions for why
/// the old cursor-based delta was dropped).
/// </summary>
public sealed class SyncEngine
{
    private readonly DeviceStore _store;
    private readonly DeviceSettings _settings;
    private readonly SyncClient _client;

    public SyncEngine(DeviceStore store, DeviceSettings settings, SyncClient client)
    {
        _store = store;
        _settings = settings;
        _client = client;
    }

    public async Task<SyncResult> SyncOnceAsync(CancellationToken ct)
    {
        var outbox = _store.ReadOutbox();
        DeviceLog.Debug($"[sync] Starting sync: outbox={outbox.Count} item(s).");

        try
        {
            foreach (var (seq, entity) in outbox)
            {
                await _client.PushAsync(entity, ct);
                _store.DeleteOutboxThrough(seq);
            }

            var entities = await _client.GetAllAsync(ct);
            var applied = _store.ApplyRemoteSnapshot(entities);

            _settings.SetLastSyncedAt(DateTimeOffset.Now);
            DeviceLog.Debug($"[sync] Sync response: entities={entities.Count} item(s), applied={applied}.");
            return new SyncResult(SyncOutcome.Succeeded, Changed: applied || outbox.Count > 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"[sync] Failed: {ex}");
            return new SyncResult(SyncOutcome.Failed, Changed: false);
        }
    }
}
