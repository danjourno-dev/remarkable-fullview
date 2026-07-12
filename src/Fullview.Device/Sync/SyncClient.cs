using System.Net.Http.Json;
using Fullview.Device.Storage;
using Fullview.Domain.Entities;
using Fullview.Domain.Sync;

namespace Fullview.Device.Sync;

/// <summary>Thin wrapper over the single `/sync` endpoint (B5 — same shape for device and
/// web). No auth header: `/sync` is still unauthenticated as of Stage 2 (see PROGRESS.md),
/// which is out of scope for Stage 5.</summary>
public sealed class SyncClient
{
    private readonly HttpClient _http;

    public SyncClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<SyncResponse> SyncAsync(
        string deviceId, string? cursor, IReadOnlyList<SyncEntity> outbox, CancellationToken ct)
    {
        var request = new SyncRequest { DeviceId = deviceId, Cursor = cursor, Outbox = outbox.ToList() };
        using var response = await _http.PostAsJsonAsync("/sync", request, DeviceJson.Options, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SyncResponse>(DeviceJson.Options, ct);
        return body ?? throw new InvalidOperationException("Empty /sync response body.");
    }
}
