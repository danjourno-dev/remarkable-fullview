using System.Net.Http.Json;
using Fullview.Device.Storage;
using Fullview.Domain.Entities;

namespace Fullview.Device.Sync;

/// <summary>Thin wrapper over the `/entities` protocol (same shape for device and web).
/// Doesn't set the `x-api-key` header itself — the caller attaches it to the `HttpClient`
/// passed in (see `AddApiKeyHeader` in Program.cs), since it's the same header on every
/// request regardless of which endpoint is called.</summary>
public sealed class SyncClient
{
    private readonly HttpClient _http;

    public SyncClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>GET /entities — every entity for the user, including tombstones.</summary>
    public async Task<IReadOnlyList<SyncEntity>> GetAllAsync(CancellationToken ct)
    {
        var entities = await _http.GetFromJsonAsync<List<SyncEntity>>("/entities", DeviceJson.Options, ct);
        return entities ?? [];
    }

    /// <summary>PUT /entities/{id} — idempotent last-write-wins upsert, used for every
    /// queued outbox mutation whether it originated as a local create or a local
    /// update.</summary>
    public async Task PushAsync(SyncEntity entity, CancellationToken ct)
    {
        using var response = await _http.PutAsJsonAsync<SyncEntity>($"/entities/{entity.Id}", entity, DeviceJson.Options, ct);
        response.EnsureSuccessStatusCode();
    }
}
