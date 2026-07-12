using Fullview.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>One commitment shown in the strip, tagged with which context it belongs to.</summary>
public sealed record StripEntry(string Label, SyncContext Context);

/// <summary>
/// Data for the Now/Next strip. Per B3, this is always cross-context — Now/Next are
/// computed from both agendas merged, regardless of the board's current mode.
/// </summary>
public sealed record StripData(StripEntry? Now, StripEntry? Next, string? TimeUntilNext);

/// <summary>
/// Renders the strip that sits below Header on every screen (B4): current + next commitment
/// across both contexts. Not tappable — the reMarkable OS reserves the top-left and top-right
/// corners for its own system gestures (menu / battery+wifi) and will swallow taps there,
/// so all interactive controls live at the bottom of the board (see Footer). The strip is
/// sacred — it never hides anything behind the current mode filter, and (mockup v4) no longer
/// shows a mode badge of its own — mode now only appears in Header/Footer.
/// </summary>
public static class NowNextStrip
{
    public const int Height = 140;

    private const int Margin = 20;
    private const int TextSize = 24;
    private const byte Black = Canvas.Black;

    // StripData is already the fully-computed (Now, Next, TimeUntilNext) — most taps recompute
    // the same values within the same minute, so cache on the computed data rather than the raw
    // "Now" timestamp (which changes on every tap and would defeat the cache).
    private static StripData? _cacheKey;
    private static Image<L8>? _cache;

    /// <summary>Renders the strip as its own <see cref="Height"/>-tall bitmap, reusing the
    /// cached bitmap when <paramref name="data"/> is unchanged since the last render.</summary>
    public static Image<L8> Render(int width, StripData data)
    {
        if (_cache is { } cached && cached.Width == width && _cacheKey == data)
        {
            return cached;
        }

        var image = new Image<L8>(width, Height, new L8(Canvas.White));
        Canvas.DrawFrame(image, 0, 0, width, Height);

        int innerX = Margin + 14;
        string nowLine = "NOW  " + FormatEntry(data.Now, "—");
        string nextLine = "NEXT " + FormatEntry(data.Next, "—") + (data.TimeUntilNext is null ? "" : "  IN " + data.TimeUntilNext);

        var font = AppFont.Regular(TextSize);
        int lineOneY = Margin + 8;
        AppFont.DrawText(image, nowLine, innerX, lineOneY, font, Black);
        AppFont.DrawText(image, nextLine, innerX, lineOneY + AppFont.LineHeight(font) + 10, font, Black);

        _cache?.Dispose();
        _cache = image;
        _cacheKey = data;
        return image;
    }

    private static string FormatEntry(StripEntry? entry, string emptyLabel) =>
        entry is null ? emptyLabel : $"({(entry.Context == SyncContext.Work ? "W" : "P")}) {entry.Label}";
}
