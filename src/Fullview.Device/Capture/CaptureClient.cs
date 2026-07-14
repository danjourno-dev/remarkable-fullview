using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fullview.Device.Storage;

namespace Fullview.Device.Capture;

/// <summary>Thin wrapper over `PUT /captures/{pageId}`. Doesn't set the `x-api-key` header
/// itself, same convention as <see cref="Sync.SyncClient"/> — the caller attaches it to the
/// shared `HttpClient` in Program.cs.</summary>
public sealed class CaptureClient
{
    private readonly HttpClient _http;

    public CaptureClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Uploads a page's raw `.rm` bytes. Returns the S3 key the API wrote to.</summary>
    public async Task<string> UploadAsync(string pageId, byte[] content, CancellationToken ct)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        using var response = await _http.PutAsync($"/captures/{pageId}", body, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CaptureUploadResponse>(DeviceJson.Options, ct);
        return result?.S3Key ?? throw new InvalidOperationException("Capture upload response did not include an s3Key.");
    }

    private sealed record CaptureUploadResponse(string S3Key);
}
