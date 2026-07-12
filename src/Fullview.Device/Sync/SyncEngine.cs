using Fullview.Device.Storage;

namespace Fullview.Device.Sync;

public enum SyncOutcome
{
    Succeeded,
    Failed
}

/// <summary>
/// Drives one outbox-drain-and-delta-apply cycle against `/sync` (Stage 5). Deliberately has
/// no retry/backoff loop of its own: the caller decides when to invoke it (app open, manual
/// tap, or a headless timer gated on "is there anything to push"), and a failed attempt just
/// leaves the outbox and cursor untouched for the next trigger to retry — see
/// tools/device/systemd/fullview-sync.timer and Program.cs for the three call sites.
/// </summary>
public sealed class SyncEngine
{
    private readonly DeviceStore _store;
    private readonly DeviceSettings _settings;
    private readonly SyncClient _client;
    private readonly string _deviceId;

    public SyncEngine(DeviceStore store, DeviceSettings settings, SyncClient client, string deviceId)
    {
        _store = store;
        _settings = settings;
        _client = client;
        _deviceId = deviceId;
    }

    public async Task<SyncOutcome> SyncOnceAsync(CancellationToken ct)
    {
        var outbox = _store.ReadOutbox();
        string? cursor = _settings.GetSyncCursor();

        Fullview.Domain.Sync.SyncResponse response;
        try
        {
            response = await _client.SyncAsync(_deviceId, cursor, outbox.Select(e => e.Entity).ToList(), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"[sync] Failed: {ex}");
            return SyncOutcome.Failed;
        }

        _store.ApplyRemoteDelta(response.Delta);
        if (outbox.Count > 0)
        {
            _store.DeleteOutboxThrough(outbox[^1].Seq);
        }

        _settings.SetSyncCursor(response.Cursor);
        _settings.SetLastSyncedAt(DateTimeOffset.Now);
        return SyncOutcome.Succeeded;
    }
}
