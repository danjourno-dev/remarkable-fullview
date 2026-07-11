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
    private const int TextScale = 3;
    private const byte Black = Canvas.Black;

    /// <summary>Draws the strip onto rows [originY, originY + Height) of image.</summary>
    public static void Draw(Image<L8> image, StripData data, int originY)
    {
        Canvas.DrawFrame(image, 0, originY, image.Width, Height);

        int innerX = Margin + 14;
        string nowLine = "NOW  " + FormatEntry(data.Now, "—");
        string nextLine = "NEXT " + FormatEntry(data.Next, "—") + (data.TimeUntilNext is null ? "" : "  IN " + data.TimeUntilNext);

        int lineOneY = originY + Margin + 8;
        BitmapFont.DrawText(image, nowLine, innerX, lineOneY, TextScale, Black);
        BitmapFont.DrawText(image, nextLine, innerX, lineOneY + BitmapFont.GlyphHeight * TextScale + 10, TextScale, Black);
    }

    private static string FormatEntry(StripEntry? entry, string emptyLabel) =>
        entry is null ? emptyLabel : $"({(entry.Context == SyncContext.Work ? "W" : "P")}) {entry.Label}";
}
