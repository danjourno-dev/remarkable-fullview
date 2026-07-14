using System.Diagnostics;
using Fullview.Rendering;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>Shared drawing primitives so each screen isn't re-implementing rect-fill clipping.</summary>
public static class Canvas
{
    public const byte Black = 0;
    public const byte White = 255;

    /// <summary>Raw pixel-row copy of <paramref name="source"/> onto <paramref name="target"/> at
    /// the given offset — used to composite cached section/panel bitmaps back into a parent
    /// image without re-drawing their contents.</summary>
    public static void Composite(Image<L8> target, Image<L8> source, int originX, int originY)
    {
        source.ProcessPixelRows(target, (sourceAccessor, targetAccessor) =>
        {
            for (int y = 0; y < sourceAccessor.Height; y++)
            {
                var sourceRow = sourceAccessor.GetRowSpan(y);
                var targetRow = targetAccessor.GetRowSpan(originY + y).Slice(originX, sourceAccessor.Width);
                sourceRow.CopyTo(targetRow);
            }
        });
    }

    public static void FillRect(Image<L8> image, int x, int y, int width, int height, byte color)
    {
        var sw = Stopwatch.StartNew();
        try
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
                    accessor.GetRowSpan(py).Slice(x0, x1 - x0).Fill(new L8(color));
                }
            });
        }
        finally
        {
            RenderDiagnostics.FillRectCalls++;
            RenderDiagnostics.FillRectTicks += sw.Elapsed.Ticks;
        }
    }

    /// <summary>Inverts every pixel in the given rect (255 - value) in place. Used to flash
    /// instant "pressed" feedback on a tap target before its (possibly slow) action runs.</summary>
    public static void InvertRect(Image<L8> image, int x, int y, int width, int height)
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
                    row[px] = new L8((byte)(255 - row[px].PackedValue));
                }
            }
        });
    }

    /// <summary>Draws a horizontal strike-through line, used for completed todos / ticked items.</summary>
    public static void StrikeThrough(Image<L8> image, int x, int y, int width, byte color) =>
        FillRect(image, x, y, width, 2, color);

    /// <summary>Draws a thin horizontal divider between list rows.</summary>
    public static void DrawDivider(Image<L8> image, int x, int y, int width, byte color = Black) =>
        FillRect(image, x, y, width, 1, color);

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

    private const int CheckboxLineThickness = 2;

    /// <summary>Draws a square checkbox outline, with a checkmark inside when ticked.
    /// Used in place of "[ ]"/"[X]" text for todo/shopping rows.</summary>
    public static void DrawCheckbox(Image<L8> image, int x, int y, int size, bool isChecked, byte color = Black)
    {
        DrawRectOutline(image, x, y, size, size, CheckboxLineThickness, color);

        if (isChecked)
        {
            int x0 = x + (int)(size * 0.2);
            int y0 = y + (int)(size * 0.55);
            int x1 = x + (int)(size * 0.42);
            int y1 = y + (int)(size * 0.78);
            int x2 = x + (int)(size * 0.82);
            int y2 = y + (int)(size * 0.22);
            DrawLine(image, x0, y0, x1, y1, CheckboxLineThickness, color);
            DrawLine(image, x1, y1, x2, y2, CheckboxLineThickness, color);
        }
    }

    /// <summary>Draws a straight line of the given thickness by stamping small squares along it.</summary>
    public static void DrawLine(Image<L8> image, int x0, int y0, int x1, int y1, int thickness, byte color)
    {
        int dx = x1 - x0;
        int dy = y1 - y0;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps == 0)
        {
            FillRect(image, x0, y0, thickness, thickness, color);
            return;
        }

        for (int i = 0; i <= steps; i++)
        {
            int px = x0 + dx * i / steps;
            int py = y0 + dy * i / steps;
            FillRect(image, px - thickness / 2, py - thickness / 2, thickness, thickness, color);
        }
    }
}
