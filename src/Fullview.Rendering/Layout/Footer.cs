using Fullview.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Bottom strip: inbox status, a reminder that the reMarkable's physical bottom-right
/// hardware button switches mode, and the current mode name. Mockup v4 removed the tappable
/// PERSONAL/WORK badge — mode switching now happens via the device's hardware button (read
/// off the gpio-keys evdev node, see Fullview.Device.Input), not a screen tap, so this footer
/// has no hit region of its own. The status text is never hidden, per B2's "stale is clearly
/// labelled, never hidden" trust principle.
/// </summary>
public static class Footer
{
    private const int Margin = 20;
    private const int TextScale = 2;
    private const byte Black = Canvas.Black;

    public const int Height = 70;

    /// <summary>Draws the footer onto the bottom <see cref="Height"/> rows of <paramref name="image"/>.</summary>
    public static void Draw(Image<L8> image, string inboxStatus, SyncContext mode)
    {
        int y = image.Height - Height;
        Canvas.DrawFrame(image, 0, y, image.Width, Height);

        int textY = y + (Height - BitmapFont.GlyphHeight * TextScale) / 2;
        string left = $"INBOX: {inboxStatus}  //  HW BUTTON = SWITCH MODE";
        BitmapFont.DrawText(image, left, Margin + 14, textY, TextScale, Black);

        string modeLabel = mode == SyncContext.Work ? "WORK" : "PERSONAL";
        int modeWidth = BitmapFont.MeasureWidth(modeLabel, TextScale);
        BitmapFont.DrawText(image, modeLabel, image.Width - Margin - 14 - modeWidth, textY, TextScale, Black);
    }
}
