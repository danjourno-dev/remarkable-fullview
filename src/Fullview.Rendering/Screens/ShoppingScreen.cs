using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Screens;

/// <summary>Shopping list grouped by category (B3), tap to tick.</summary>
public static class ShoppingScreen
{
    private const int Margin = 24;
    private const int HeaderScale = 4;
    private const int CategoryScale = 3;
    private const int RowScale = 3;
    private const int RowHeight = 70;
    private const int CategoryGap = 20;

    public static ScreenRenderResult Render(int width, int height, IReadOnlyList<ShoppingItem> items)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));
        var regions = new List<HitRegion>();

        BitmapFont.DrawText(image, "SHOPPING", Margin, Margin, HeaderScale, Canvas.Black);

        int y = Margin + BitmapFont.GlyphHeight * HeaderScale + Margin;

        var byCategory = items
            .GroupBy(i => string.IsNullOrWhiteSpace(i.Category) ? "OTHER" : i.Category!.ToUpperInvariant())
            .OrderBy(g => g.Key);

        var flattened = byCategory.SelectMany(g => g).ToList();
        var (visible, overflow) = ListPage.Paginate(flattened);
        var visibleSet = new HashSet<string>(visible.Select(i => i.Id));

        foreach (var group in byCategory)
        {
            var groupVisible = group.Where(i => visibleSet.Contains(i.Id)).ToList();
            if (groupVisible.Count == 0)
            {
                continue;
            }

            BitmapFont.DrawText(image, group.Key, Margin, y, CategoryScale, Canvas.Black);
            y += BitmapFont.GlyphHeight * CategoryScale + 10;

            foreach (var item in groupVisible)
            {
                DrawRow(image, regions, Margin, y, width - 2 * Margin, item);
                y += RowHeight;
            }

            y += CategoryGap;
        }

        if (overflow > 0)
        {
            BitmapFont.DrawText(image, $"+{overflow} MORE", Margin, y, RowScale, Canvas.Black);
        }

        return new ScreenRenderResult(image, regions);
    }

    private static void DrawRow(Image<L8> image, List<HitRegion> regions, int x, int y, int width, ShoppingItem item)
    {
        string checkbox = item.Checked ? "[X]" : "[ ]";
        string line = $"{checkbox} {item.Name}";
        BitmapFont.DrawText(image, line, x, y, RowScale, Canvas.Black);

        if (item.Checked)
        {
            int textWidth = BitmapFont.MeasureWidth(line, RowScale);
            int strikeY = y + (BitmapFont.GlyphHeight * RowScale) / 2;
            Canvas.StrikeThrough(image, x, strikeY, textWidth, Canvas.Black);
        }

        int rowHeight = BitmapFont.GlyphHeight * RowScale + 16;
        regions.Add(new HitRegion(new Rectangle(x, y - 8, width, rowHeight), new BoardAction.ToggleShoppingItem(item.Id)));
    }
}
