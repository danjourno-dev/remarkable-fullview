using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering;

/// <summary>
/// Minimal 5x7 block font, hand-authored for this project (no external
/// font asset, no licensing to track). Covers only what Stage 3's
/// hello-world screen needs; Stage 4 brings real typography via a proper
/// font once screen layouts are designed.
/// </summary>
public static class BitmapFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    private const int GlyphSpacing = 1;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['H'] = new[] { "#...#", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
        ['L'] = new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" },
        ['O'] = new[] { ".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['D'] = new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." },
        ['A'] = new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" },
        ['N'] = new[] { "#...#", "##..#", "#.#.#", "#..##", "#...#", "#...#", "#...#" },
        [' '] = new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." },
    };

    public static int MeasureWidth(string text, int scale) => text.Length * (GlyphWidth + GlyphSpacing) * scale;

    public static void DrawText(Image<L8> image, string text, int originX, int originY, int scale, byte color)
    {
        int cursorX = originX;
        foreach (char c in text)
        {
            char upper = char.ToUpperInvariant(c);
            if (!Glyphs.TryGetValue(upper, out var glyph))
            {
                glyph = Glyphs[' '];
            }

            for (int row = 0; row < GlyphHeight; row++)
            {
                string bits = glyph[row];
                for (int col = 0; col < GlyphWidth; col++)
                {
                    if (bits[col] != '#')
                    {
                        continue;
                    }

                    FillBlock(image, cursorX + col * scale, originY + row * scale, scale, scale, color);
                }
            }

            cursorX += (GlyphWidth + GlyphSpacing) * scale;
        }
    }

    private static void FillBlock(Image<L8> image, int x, int y, int width, int height, byte color)
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
}
