using System.Diagnostics;
using Fullview.Rendering.Layout;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Fullview.Rendering;

/// <summary>
/// Roboto (Apache License 2.0 — see Assets/Fonts/Roboto-LICENSE.txt) text rendering, embedded
/// so device deploys don't depend on a system font being installed. Replaced the original
/// hand-authored 5x7 bitmap font once legibility became a real concern.
/// </summary>
public static class AppFont
{
    private static readonly FontFamily RegularFamily;
    private static readonly FontFamily BoldFamily;

    static AppFont()
    {
        var collection = new FontCollection();
        var assembly = typeof(AppFont).Assembly;

        using var regularStream = assembly.GetManifestResourceStream("Fullview.Rendering.Assets.Fonts.Roboto-Regular.ttf")
            ?? throw new InvalidOperationException("Embedded resource Roboto-Regular.ttf not found.");
        RegularFamily = collection.Add(regularStream);

        using var boldStream = assembly.GetManifestResourceStream("Fullview.Rendering.Assets.Fonts.Roboto-Bold.ttf")
            ?? throw new InvalidOperationException("Embedded resource Roboto-Bold.ttf not found.");
        BoldFamily = collection.Add(boldStream);
    }

    public static Font Regular(float size) => RegularFamily.CreateFont(size, FontStyle.Regular);

    public static Font Bold(float size) => BoldFamily.CreateFont(size, FontStyle.Bold);

    /// <summary>Cap height in pixels for a given font — the rough visual "row height" a single
    /// line of this font occupies, comparable to the old bitmap font's fixed cell height.</summary>
    public static int LineHeight(Font font) => (int)Math.Ceiling(font.Size);

    public static int MeasureWidth(string text, Font font) =>
        (int)Math.Ceiling(TextMeasurer.MeasureAdvance(text, new TextOptions(font)).Width);

    // SixLabors' outline-to-raster fill + antialiasing is the dominant per-render cost on the
    // rM1's CPU — tens of ms per DrawText call, confirmed against RenderDiagnostics. There's no
    // per-glyph reuse across calls, even though the same (font, character) pair recurs constantly
    // (headers, digits, common letters). Rasterizing each (font, char) once into an ink-coverage
    // mask and blitting it thereafter turns that repeated vector rasterization into a cheap
    // per-pixel blend on every render after the first.
    private static readonly Dictionary<(string FontKey, char Ch), Glyph> GlyphCache = new();

    private readonly record struct Glyph(byte[,]? Coverage, int Width, int Height, int OffsetX, int OffsetY, int Advance);

    public static void DrawText(Image<L8> image, string text, int x, int y, Font font, byte color)
    {
        var sw = Stopwatch.StartNew();

        string fontKey = $"{font.Family.Name}_{font.Size}_{font.IsBold}_{font.IsItalic}";
        int penX = x;
        foreach (char ch in text)
        {
            var glyph = GetGlyph(fontKey, font, ch);
            if (glyph.Coverage is not null)
            {
                BlendGlyph(image, glyph, penX, y, color);
            }

            penX += glyph.Advance;
        }

        RenderDiagnostics.TextDrawCalls++;
        RenderDiagnostics.TextDrawTicks += sw.Elapsed.Ticks;
    }

    private static Glyph GetGlyph(string fontKey, Font font, char ch)
    {
        var key = (fontKey, ch);
        if (!GlyphCache.TryGetValue(key, out var glyph))
        {
            glyph = RasterizeGlyph(font, ch);
            GlyphCache[key] = glyph;
        }

        return glyph;
    }

    private static Glyph RasterizeGlyph(Font font, char ch)
    {
        int advance = (int)Math.Ceiling(TextMeasurer.MeasureAdvance(ch.ToString(), new TextOptions(font)).Width);

        if (char.IsWhiteSpace(ch))
        {
            return new Glyph(null, 0, 0, 0, 0, advance);
        }

        // Scratch canvas big enough for any glyph's ascenders/descenders at this size, with a
        // fixed pad so the crop below can recover the glyph's true offset from the pen position.
        int pad = (int)Math.Ceiling(font.Size);
        int cell = pad + (int)Math.Ceiling(font.Size * 2);
        using var scratch = new Image<L8>(cell, cell, new L8(Canvas.White));
        scratch.Mutate(ctx => ctx.DrawText(ch.ToString(), font, Color.Black, new PointF(pad, pad)));

        int minX = cell, minY = cell, maxX = -1, maxY = -1;
        scratch.ProcessPixelRows(accessor =>
        {
            for (int row = 0; row < accessor.Height; row++)
            {
                var span = accessor.GetRowSpan(row);
                for (int col = 0; col < span.Length; col++)
                {
                    if (span[col].PackedValue < 255)
                    {
                        if (col < minX) minX = col;
                        if (col > maxX) maxX = col;
                        if (row < minY) minY = row;
                        if (row > maxY) maxY = row;
                    }
                }
            }
        });

        if (maxX < minX || maxY < minY)
        {
            return new Glyph(null, 0, 0, 0, 0, advance);
        }

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        var coverage = new byte[height, width];
        scratch.ProcessPixelRows(accessor =>
        {
            for (int row = 0; row < height; row++)
            {
                var span = accessor.GetRowSpan(minY + row);
                for (int col = 0; col < width; col++)
                {
                    coverage[row, col] = (byte)(255 - span[minX + col].PackedValue);
                }
            }
        });

        return new Glyph(coverage, width, height, minX - pad, minY - pad, advance);
    }

    private static void BlendGlyph(Image<L8> image, Glyph glyph, int x, int y, byte color)
    {
        int destX = x + glyph.OffsetX;
        int destY = y + glyph.OffsetY;

        image.ProcessPixelRows(accessor =>
        {
            for (int row = 0; row < glyph.Height; row++)
            {
                int py = destY + row;
                if (py < 0 || py >= accessor.Height)
                {
                    continue;
                }

                var span = accessor.GetRowSpan(py);
                for (int col = 0; col < glyph.Width; col++)
                {
                    int px = destX + col;
                    if (px < 0 || px >= span.Length)
                    {
                        continue;
                    }

                    int coverage = glyph.Coverage![row, col];
                    if (coverage == 0)
                    {
                        continue;
                    }

                    byte bg = span[px].PackedValue;
                    int blended = bg + (color - bg) * coverage / 255;
                    span[px] = new L8((byte)blended);
                }
            }
        });
    }
}
