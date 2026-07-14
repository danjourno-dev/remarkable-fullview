using System.Security.Cryptography;
using Fullview.Device.Logging;

namespace Fullview.Device.Capture;

/// <summary>
/// Stage 7: scans the configured Inbox notebook's directory
/// (`~/.local/share/remarkable/xochitl/&lt;notebookUuid&gt;/`, see docs/device-setup.md) for
/// page files (`&lt;pageUuid&gt;.rm`, xochitl's on-disk layout) and queues any whose content
/// hash differs from the last successfully-uploaded hash for that page id. Polled on the same
/// triggers as <see cref="Sync.SyncEngine"/> (background timer, network reconnect, app open —
/// see Program.cs) rather than a filesystem watch: xochitl itself owns writing these files
/// while the pen is in use, and a poll-on-trigger is simpler and more robust than reasoning
/// about a `FileSystemWatcher` racing a third-party process's own save behavior.
/// </summary>
public static class InboxWatcher
{
    /// <summary>Scans <paramref name="notebookPath"/> and enqueues every page whose content
    /// hash is new or has changed since its last recorded upload. Returns the number of pages
    /// newly queued. A missing directory (Inbox notebook never created, or wrong config) is
    /// logged and treated as "nothing to queue" rather than thrown — same "stale is fine"
    /// posture as the rest of the sync path.</summary>
    public static int ScanAndEnqueue(string notebookPath, CaptureStore captureStore)
    {
        if (!Directory.Exists(notebookPath))
        {
            DeviceLog.Debug($"[capture] Inbox notebook path does not exist: {notebookPath}.");
            return 0;
        }

        int queued = 0;
        foreach (var filePath in Directory.EnumerateFiles(notebookPath, "*.rm"))
        {
            string pageId = Path.GetFileNameWithoutExtension(filePath);
            byte[] content = File.ReadAllBytes(filePath);
            string hash = Convert.ToHexString(SHA256.HashData(content));

            if (captureStore.GetKnownHash(pageId) == hash)
            {
                continue;
            }

            captureStore.Enqueue(pageId, filePath, hash);
            queued++;
            DeviceLog.Debug($"[capture] Queued page {pageId} ({content.Length} bytes, hash={hash[..8]}...).");
        }

        return queued;
    }
}
