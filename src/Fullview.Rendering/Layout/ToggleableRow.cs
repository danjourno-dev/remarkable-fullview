using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Draws a checkbox + label (with strikethrough when completed). Shared by every checkbox-style
/// list (Reminders, Shopping, ...). Row-level tap regions are computed by the caller (see
/// TodayScreen) rather than here, since panel bitmaps are cached and hit regions need to be
/// recomputed on every render regardless of whether the bitmap itself was redrawn.
/// </summary>
public static class ToggleableRow
{
    /// <summary>Y offset (above <paramref name="y"/>) a row's tap region should start at, so
    /// taps register slightly above the text baseline — kept here so callers building hit
    /// regions stay in sync with the drawn checkbox/text position.</summary>
    public const int RegionYOffset = 8;

    public const int CheckboxGap = 14;

    public static void DrawContent(Image<L8> image, int x, int y, Font font, string label, bool completed, byte color)
    {
        int checkboxSize = AppFont.LineHeight(font);
        int textX = x + checkboxSize + CheckboxGap;

        Canvas.DrawCheckbox(image, x, y, checkboxSize, completed, color);
        AppFont.DrawText(image, label, textX, y, font, color);

        if (completed)
        {
            int textWidth = AppFont.MeasureWidth(label, font);
            int strikeY = y + checkboxSize / 2;
            Canvas.StrikeThrough(image, textX, strikeY, textWidth, color);
        }
    }
}
