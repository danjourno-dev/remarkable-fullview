using System.Text.RegularExpressions;

namespace Fullview.Api.Capture;

/// <summary>
/// Backs `PUT /captures/{pageId}` (see docs/plans/implementation.md Stage 7): the device
/// uploads a changed Inbox page's raw `.rm` bytes here rather than writing to S3 directly, so
/// the device never holds S3 credentials — only the shared API key it already has for
/// `/entities`. The Lambda then owns getting the bytes into the inbox bucket; the device's
/// only job after a successful upload is to PUT an <c>InboxPage</c> entity through the normal
/// `/entities` sync protocol referencing the key this returns.
/// </summary>
public sealed partial class CaptureService(ICaptureStore store)
{
    public async Task<string> UploadPageAsync(string pageId, byte[] content, CancellationToken ct)
    {
        if (!IsValidPageId(pageId))
        {
            throw new ArgumentException($"'{pageId}' is not a valid page id.", nameof(pageId));
        }

        string key = $"inbox/{pageId}.rm";
        await store.PutAsync(key, content, ct);
        return key;
    }

    // pageId becomes part of an S3 key built by simple string concatenation (above) — restrict
    // it to what a device-generated page uuid actually looks like (hex + dashes, no dots) so
    // nothing resembling a path traversal ("..", "/") can ever reach PutAsync.
    public static bool IsValidPageId(string pageId) =>
        !string.IsNullOrEmpty(pageId) && PageIdPattern().IsMatch(pageId);

    [GeneratedRegex("^[a-zA-Z0-9-]{1,128}$")]
    private static partial Regex PageIdPattern();
}
