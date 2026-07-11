using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>Shared drawing primitives so each screen isn't re-implementing rect-fill clipping.</summary>
public static class Canvas
{
    public const byte Black = 0;
    public const byte White = 255;

    public static void FillRect(Image<L8> image, int x, int y, int width, int height, byte color)
    {
        int x0 = Math.Max(0, x);
        int y0 = Math.Max(0, y);
        int x1 = Math.Min(image.Width, x + width);
        int y1 = Math.Min(image.Height, y + height);
        if (x1 <= x0 || y1 <= y0)
        {
            return;
        }

        image.ProcessPixelRows(accessor =>
        {
            for (int py = y0; py < y1; py++)
            {
                var row = accessor.GetRowSpan(py);
                for (int px = x0; px < x1; px++)
                {
                    row[px] = new L8(color);
                }
            }
        });
    }

    /// <summary>Draws a horizontal strike-through line, used for completed todos / ticked items.</summary>
    public static void StrikeThrough(Image<L8> image, int x, int y, int width, byte color) =>
        FillRect(image, x, y, width, 2, color);

    private const int FrameLineThickness = 3;
    private const int FrameGap = 5;

    /// <summary>Draws the mockup's double-ruled box: an outer border, then a second border
    /// inset by <see cref="FrameGap"/>. Used for every panel/strip/header/footer box.</summary>
    public static void DrawFrame(Image<L8> image, int x, int y, int width, int height, byte color = Black)
    {
        DrawRectOutline(image, x, y, width, height, FrameLineThickness, color);

        int gx = x + FrameLineThickness + FrameGap;
        int gy = y + FrameLineThickness + FrameGap;
        int gw = width - 2 * (FrameLineThickness + FrameGap);
        int gh = height - 2 * (FrameLineThickness + FrameGap);
        if (gw > 0 && gh > 0)
        {
            DrawRectOutline(image, gx, gy, gw, gh, FrameLineThickness, color);
        }
    }

    private static void DrawRectOutline(Image<L8> image, int x, int y, int width, int height, int thickness, byte color)
    {
        FillRect(image, x, y, width, thickness, color);
        FillRect(image, x, y + height - thickness, width, thickness, color);
        FillRect(image, x, y, thickness, height, color);
        FillRect(image, x + width - thickness, y, thickness, height, color);
    }
}
