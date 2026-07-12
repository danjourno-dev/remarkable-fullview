using System.Net.Http.Json;
using Fullview.Device.Storage;
using Fullview.Domain.Entities;
using Fullview.Domain.Sync;

namespace Fullview.Device.Sync;

/// <summary>Thin wrapper over the single `/sync` endpoint (B5 — same shape for device and
/// web). Doesn't set the `x-api-key` header itself — the caller attaches it to the
/// `HttpClient` passed in (see `AddApiKeyHeader` in Program.cs), since it's the same header
/// on every request regardless of which endpoint is called.</summary>
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
