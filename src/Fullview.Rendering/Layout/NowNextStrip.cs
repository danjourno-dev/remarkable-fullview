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
/// across both contexts, plus a sync button on the right edge. Confirmed with Dan this row is
/// a safe tap zone on real hardware — the "OS swallows gestures" constraint (the reMarkable OS
/// reserves the top-left/top-right corners for its own menu/battery+wifi gestures) applies to
/// the row nearer the very top (Header/mode row), not this one. The strip's Now/Next content
/// is sacred — it never hides anything behind the current mode filter, and (mockup v4) no
/// longer shows a mode badge of its own — mode still only appears in Header/Footer.
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
    private static int _cacheWidth;
    private static (Image<L8> Image, Rectangle SyncButtonBounds)? _cache;

    private const int SyncButtonSize = Height - 2 * Margin;

    /// <summary>Renders the strip as its own <see cref="Height"/>-tall bitmap plus the bounds
    /// of the sync button on its right edge, reusing the cached bitmap when
    /// <paramref name="data"/> and <paramref name="width"/> are unchanged since the last
    /// render. Bounds are constant for a given <paramref name="width"/>, so they're cheap to
    /// return even on a cache hit.</summary>
    public static (Image<L8> Image, Rectangle SyncButtonBounds) Render(int width, StripData data)
    {
        if (_cache is { } cached && _cacheWidth == width && _cacheKey == data)
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

        var syncButtonBounds = SyncButtonBounds(width);
        Canvas.DrawFrame(image, syncButtonBounds.X, syncButtonBounds.Y, syncButtonBounds.Width, syncButtonBounds.Height);
        int iconRadius = SyncButtonSize / 3;
        DrawSyncIcon(
            image,
            syncButtonBounds.X + syncButtonBounds.Width / 2,
            syncButtonBounds.Y + syncButtonBounds.Height / 2,
            iconRadius,
            Black);

        var result = (image, syncButtonBounds);
        _cache = result;
        _cacheWidth = width;
        _cacheKey = data;
        return result;
    }

    /// <summary>Bounds of the sync button, vertically centered on the right edge of the
    /// strip with the row's existing <see cref="Margin"/>.</summary>
    private static Rectangle SyncButtonBounds(int width) =>
        new(width - Margin - SyncButtonSize, Margin, SyncButtonSize, SyncButtonSize);

    /// <summary>Draws a monochrome "sync" pictogram — a circular arrow with an arrowhead —
    /// built from <see cref="Canvas.DrawLine"/>, the same line-segment approach
    /// <see cref="Canvas.DrawCheckbox"/> uses for its checkmark, since there's no circle/arc
    /// primitive in <see cref="Canvas"/>.</summary>
    private static void DrawSyncIcon(Image<L8> image, int centerX, int centerY, int radius, byte color)
    {
        const int thickness = 3;
        const double startDeg = -60;
        const double endDeg = 210;
        const double stepDeg = 15;

        (double X, double Y) PointAt(double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return (centerX + radius * Math.Cos(rad), centerY + radius * Math.Sin(rad));
        }

        var previous = PointAt(startDeg);
        for (double deg = startDeg + stepDeg; deg <= endDeg; deg += stepDeg)
        {
            var current = PointAt(deg);
            Canvas.DrawLine(image, (int)previous.X, (int)previous.Y, (int)current.X, (int)current.Y, thickness, color);
            previous = current;
        }

        // Arrowhead at the arc's open end, pointing back along the tangent direction.
        var tip = PointAt(startDeg);
        var tangentPoint = PointAt(startDeg + stepDeg);
        double tangentDx = tip.X - tangentPoint.X;
        double tangentDy = tip.Y - tangentPoint.Y;
        double len = Math.Sqrt(tangentDx * tangentDx + tangentDy * tangentDy);
        if (len > 0)
        {
            tangentDx /= len;
            tangentDy /= len;
        }

        double perpDx = -tangentDy;
        double perpDy = tangentDx;
        double headSize = radius * 0.6;

        int wingAX = (int)(tip.X - tangentDx * headSize + perpDx * headSize * 0.6);
        int wingAY = (int)(tip.Y - tangentDy * headSize + perpDy * headSize * 0.6);
        int wingBX = (int)(tip.X - tangentDx * headSize - perpDx * headSize * 0.6);
        int wingBY = (int)(tip.Y - tangentDy * headSize - perpDy * headSize * 0.6);

        Canvas.DrawLine(image, (int)tip.X, (int)tip.Y, wingAX, wingAY, thickness, color);
        Canvas.DrawLine(image, (int)tip.X, (int)tip.Y, wingBX, wingBY, thickness, color);
    }

    private static string FormatEntry(StripEntry? entry, string emptyLabel) =>
        entry is null ? emptyLabel : $"({(entry.Context == SyncContext.Work ? "W" : "P")}) {entry.Label}";
}
