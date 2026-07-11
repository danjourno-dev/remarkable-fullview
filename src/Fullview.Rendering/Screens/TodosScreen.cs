using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Screens;

/// <summary>
/// Flat todo list (B3): priority + energy tags, optional due date, tap to complete
/// (strikethrough). Renders the screen *body* only — the caller composites strip/footer.
/// </summary>
public static class TodosScreen
{
    private const int Margin = 24;
    private const int HeaderScale = 4;
    private const int RowScale = 3;
    private const int RowHeight = 84;

    public static ScreenRenderResult Render(int width, int height, IReadOnlyList<Todo> todos)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));
        var regions = new List<HitRegion>();

        BitmapFont.DrawText(image, "TODOS", Margin, Margin, HeaderScale, Canvas.Black);

        int y = Margin + BitmapFont.GlyphHeight * HeaderScale + Margin;
        var ordered = todos
            .OrderBy(t => t.Completed)
            .ThenBy(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ToList();

        var (visible, overflow) = ListPage.Paginate(ordered);

        foreach (var todo in visible)
        {
            DrawRow(image, regions, Margin, y, width - 2 * Margin, todo);
            y += RowHeight;
        }

        if (overflow > 0)
        {
            BitmapFont.DrawText(image, $"+{overflow} MORE", Margin, y + 10, RowScale, Canvas.Black);
        }

        return new ScreenRenderResult(image, regions);
    }

    private static void DrawRow(Image<L8> image, List<HitRegion> regions, int x, int y, int width, Todo todo)
    {
        string checkbox = todo.Completed ? "[X]" : "[ ]";
        string tags = $"{PriorityTag(todo.Priority)}{EnergyTag(todo.Energy)}";
        string due = todo.DueDate is { } d ? $" DUE {d.Year}-{d.Month:00}-{d.Day:00}" : "";
        string line = $"{checkbox} {todo.Title} {tags}{due}";

        BitmapFont.DrawText(image, line, x, y, RowScale, Canvas.Black);

        if (todo.Completed)
        {
            int textWidth = BitmapFont.MeasureWidth(line, RowScale);
            int strikeY = y + (BitmapFont.GlyphHeight * RowScale) / 2;
            Canvas.StrikeThrough(image, x, strikeY, textWidth, Canvas.Black);
        }

        int rowHeight = BitmapFont.GlyphHeight * RowScale + 20;
        regions.Add(new HitRegion(new Rectangle(x, y - 10, width, rowHeight), new BoardAction.ToggleTodo(todo.Id)));
    }

    private static string PriorityTag(TodoPriority priority) => priority switch
    {
        TodoPriority.Focus => " FOCUS",
        TodoPriority.Someday => " SOMEDAY",
        _ => ""
    };

    private static string EnergyTag(TodoEnergy? energy) => energy switch
    {
        TodoEnergy.QuickWin => " QUICK",
        TodoEnergy.Deep => " DEEP",
        _ => ""
    };
}
