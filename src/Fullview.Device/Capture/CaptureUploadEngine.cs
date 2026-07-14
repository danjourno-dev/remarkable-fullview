using Fullview.Device.Logging;
using Fullview.Device.Storage;
using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Device.Capture;

/// <summary>
/// Drives one capture-outbox-drain cycle: uploads each queued page's bytes via
/// `PUT /captures/{pageId}`, and on success writes an `InboxPage` entity (State=Queued,
/// S3Key=the key the API returned) through <see cref="DeviceStore.Save"/> — which queues it
/// into the normal entity outbox for <see cref="Sync.SyncEngine"/> to push on its own next
/// drain. Mirrors SyncEngine's one-item-at-a-time contract: a mid-drain failure leaves the
/// rest of the capture outbox untouched for the next call to retry.
/// </summary>
public sealed class CaptureUploadEngine
{
    private readonly CaptureStore _captureStore;
    private readonly DeviceStore _store;
    private readonly CaptureClient _client;
    private readonly string _deviceId;

    public CaptureUploadEngine(CaptureStore captureStore, DeviceStore store, CaptureClient client, string deviceId)
    {
        _captureStore = captureStore;
        _store = store;
        _client = client;
        _deviceId = deviceId;
    }

    /// <summary>Returns the number of pages successfully uploaded this call.</summary>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var outbox = _captureStore.ReadOutbox();
        DeviceLog.Debug($"[capture] Draining capture outbox: {outbox.Count} item(s).");

        int uploaded = 0;
        foreach (var item in outbox)
        {
            byte[] content;
            try
            {
                content = File.ReadAllBytes(item.FilePath);
            }
            catch (IOException ex)
            {
                // The page file vanished or is mid-write by xochitl since it was queued — skip
                // it this pass; the next InboxWatcher scan will re-queue it once it settles.
                DeviceLog.Debug($"[capture] Could not read {item.FilePath} for page {item.PageId}: {ex.Message}.");
                continue;
            }

            string s3Key;
            try
            {
                s3Key = await _client.UploadAsync(item.PageId, content, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                Console.WriteLine($"[capture] Upload failed for page {item.PageId}: {ex}");
                break;
            }

            _captureStore.MarkUploaded(item.Seq, item.PageId, item.ContentHash);

            _store.Save(new InboxPage
            {
                Id = item.PageId,
                Context = SyncContext.Personal,
                UpdatedAt = DateTimeOffset.UtcNow,
                UpdatedBy = _deviceId,
                State = InboxPageState.Queued,
                S3Key = s3Key
            });

            uploaded++;
        }

        return uploaded;
    }
}
