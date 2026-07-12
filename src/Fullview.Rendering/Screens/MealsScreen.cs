using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Screens;

/// <summary>Week grid of meals (B3): breakfast/dinner slots, tap a meal with a linked recipe
/// to open the recipe screen.</summary>
public static class MealsScreen
{
    private const int Margin = 24;
    private const int HeaderSize = 32;
    private const int DateSize = 22;
    private const int RowSize = 22;
    private const int RowHeight = 60;
    private const int DateGap = 16;

    public static ScreenRenderResult Render(
        int width,
        int height,
        IReadOnlyList<Meal> meals,
        IReadOnlyDictionary<string, Recipe> recipesById)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));
        var regions = new List<HitRegion>();

        var headerFont = AppFont.Bold(HeaderSize);
        AppFont.DrawText(image, "MEALS", Margin, Margin, headerFont, Canvas.Black);

        int y = Margin + AppFont.LineHeight(headerFont) + Margin;

        var byDate = meals.OrderBy(m => m.Date).ThenBy(m => m.Slot).GroupBy(m => m.Date);
        var dateFont = AppFont.Bold(DateSize);
        var rowFont = AppFont.Regular(RowSize);

        foreach (var day in byDate)
        {
            if (y > height - RowHeight)
            {
                break;
            }

            AppFont.DrawText(image, FormatDate(day.Key), Margin, y, dateFont, Canvas.Black);
            y += AppFont.LineHeight(dateFont) + 10;

            foreach (var meal in day)
            {
                string slotLabel = meal.Slot == MealSlot.Breakfast ? "B" : "D";
                string description = meal.Description
                    ?? (meal.RecipeId is not null && recipesById.TryGetValue(meal.RecipeId, out var recipe) ? recipe.Title : "—");
                string line = $"{slotLabel}: {description}";

                AppFont.DrawText(image, line, Margin, y, rowFont, Canvas.Black);

                if (meal.RecipeId is not null)
                {
                    int textWidth = AppFont.MeasureWidth(line, rowFont);
                    int rowHeight = AppFont.LineHeight(rowFont) + 12;
                    regions.Add(new HitRegion(
                        new Rectangle(Margin, y - 6, textWidth, rowHeight),
                        new BoardAction.OpenRecipe(meal.RecipeId)));
                }

                y += RowHeight;
            }

            y += DateGap;
        }

        return new ScreenRenderResult(image, regions);
    }

    private static string FormatDate(DateOnly date) => $"{DayName(date.DayOfWeek)} {date.Day:00} {MonthName(date.Month)}";

    private static string DayName(DayOfWeek day) => day.ToString()[..3].ToUpperInvariant();

    private static string MonthName(int month) => new DateOnly(2000, month, 1).ToString("MMM").ToUpperInvariant();
}
