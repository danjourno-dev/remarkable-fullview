namespace Fullview.Api.Capture;

/// <summary>Abstracts the S3 write behind the capture upload endpoint — no AWS types here on
/// purpose, same reasoning as <see cref="Fullview.Api.Sync.ISyncStore"/>, so
/// <see cref="CaptureService"/> can be unit tested without a real bucket.</summary>
public interface ICaptureStore
{
    Task PutAsync(string key, byte[] content, CancellationToken ct);
}
