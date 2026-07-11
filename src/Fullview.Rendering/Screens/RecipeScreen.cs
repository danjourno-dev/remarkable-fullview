using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Screens;

/// <summary>Recipe detail (B3): title, ingredients, steps. Read-only on device — editing is
/// a web app action. Reached only via a Meals-screen tap; there is no forward nav entry for it,
/// so edge-tap navigation from here lands back on Today (ScreenSet.Step's not-found fallback).</summary>
public static class RecipeScreen
{
    private const int Margin = 24;
    private const int HeaderScale = 4;
    private const int SectionScale = 3;
    private const int RowScale = 3;
    private const int RowHeight = 50;
    private const int SectionGap = 20;

    public static ScreenRenderResult Render(int width, int height, Recipe recipe)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));

        BitmapFont.DrawText(image, recipe.Title.ToUpperInvariant(), Margin, Margin, HeaderScale, Canvas.Black);
        int y = Margin + BitmapFont.GlyphHeight * HeaderScale + Margin;

        y = DrawSection(image, "INGREDIENTS", recipe.Ingredients, y, height);
        y += SectionGap;
        DrawSection(image, "STEPS", recipe.Steps, y, height);

        return new ScreenRenderResult(image, Array.Empty<HitRegion>());
    }

    private static int DrawSection(Image<L8> image, string heading, IReadOnlyList<string> lines, int y, int height)
    {
        BitmapFont.DrawText(image, heading, Margin, y, SectionScale, Canvas.Black);
        y += BitmapFont.GlyphHeight * SectionScale + 10;

        foreach (var line in lines)
        {
            if (y > height - RowHeight)
            {
                break;
            }

            BitmapFont.DrawText(image, "- " + line, Margin, y, RowScale, Canvas.Black);
            y += RowHeight;
        }

        return y;
    }
}
