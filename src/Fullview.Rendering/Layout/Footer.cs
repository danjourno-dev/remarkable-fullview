using Fullview.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Bottom strip: inbox status, live sync status, and the current mode name. Mockup v4 removed
/// the tappable PERSONAL/WORK badge — mode switching now happens via the device's hardware
/// button (read off the gpio-keys evdev node, see Fullview.Device.Input), not a screen tap.
/// The sync-status segment IS tappable (Stage 5) — tapping it triggers a manual sync; its
/// bounds are returned alongside the image so BoardRenderer can register that hit region. The
/// status text is never hidden, per B2's "stale is clearly labelled, never hidden" trust
/// principle.
/// </summary>
public static class Footer
{
    private const int Margin = 20;
    private const int TextSize = 18;
    private const byte Black = Canvas.Black;

    public const int Height = 70;

    // Same reasoning as Header's cache: inboxStatus/syncStatus/mode/version are almost always
    // unchanged across a tap, so avoid re-rasterizing this text every render.
    private static (string InboxStatus, string SyncStatus, SyncContext Mode, string Version)? _cacheKey;
    private static Image<L8>? _cache;
    private static Rectangle _cacheSyncStatusBounds;

    /// <summary>Renders the footer as its own <see cref="Height"/>-tall bitmap, reusing the
    /// cached bitmap when (inboxStatus, syncStatus, mode, version) are unchanged since the
    /// last render. Returns the sync-status text's bounds (relative to this bitmap) so the
    /// caller can register it as a tap target.</summary>
    public static (Image<L8> Image, Rectangle SyncStatusBounds) Render(
        int width, string inboxStatus, string syncStatus, SyncContext mode, string version)
    {
        var key = (inboxStatus, syncStatus, mode, version);
        if (_cache is { } cached && cached.Width == width && _cacheKey == key)
        {
            return (cached, _cacheSyncStatusBounds);
        }

        var image = new Image<L8>(width, Height, new L8(Canvas.White));
        Canvas.DrawFrame(image, 0, 0, width, Height);

        var font = AppFont.Regular(TextSize);
        int textY = (Height - AppFont.LineHeight(font)) / 2;
        string left = $"INBOX: {inboxStatus}  //  ";
        AppFont.DrawText(image, left, Margin + 14, textY, font, Black);

        int syncX = Margin + 14 + AppFont.MeasureWidth(left, font);
        AppFont.DrawText(image, syncStatus, syncX, textY, font, Black);
        int syncWidth = AppFont.MeasureWidth(syncStatus, font);
        var syncBounds = new Rectangle(syncX, 0, syncWidth, Height);

        string modeLabel = mode == SyncContext.Work ? "WORK" : "PERSONAL";
        int modeWidth = AppFont.MeasureWidth(modeLabel, font);
        AppFont.DrawText(image, modeLabel, width - Margin - 14 - modeWidth, textY, font, Black);

        // Deploy-verification marker (see tools/device/publish-arm.sh): centred so it's easy
        // to eyeball that a freshly published build actually replaced the running one.
        string versionLabel = $"v{version}";
        int versionWidth = AppFont.MeasureWidth(versionLabel, font);
        AppFont.DrawText(image, versionLabel, (width - versionWidth) / 2, textY, font, Black);

        _cache?.Dispose();
        _cache = image;
        _cacheKey = key;
        _cacheSyncStatusBounds = syncBounds;
        return (image, syncBounds);
    }
}
