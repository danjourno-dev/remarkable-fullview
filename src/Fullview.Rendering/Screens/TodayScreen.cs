using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Screens;

/// <summary>Pre-filtered data for TodayScreen — filtering by mode/date is the caller's job
/// (BoardRenderer), this screen only lays out what it's given. WorkReminders/PersonalReminders
/// are cross-context (shown identically in both modes, like NowNextStrip's Now/Next); WaitingOn
/// and Shutdown are Work-only and empty in Personal mode.</summary>
public sealed record TodayScreenData(
    DateOnly Date,
    IReadOnlyList<AgendaEvent> TodayAgenda,
    string MealsSummary,
    IReadOnlyList<ShoppingItem> ShoppingItems,
    IReadOnlyList<Todo> WaitingOn,
    IReadOnlyList<Todo> Shutdown,
    IReadOnlyList<Todo> WorkReminders,
    IReadOnlyList<Todo> PersonalReminders,
    SyncContext Mode);

/// <summary>
/// Today dashboard (mockup v4): a 2x2 grid of double-ruled panels — Agenda + (Meals in
/// Personal mode / Waiting On in Work mode) on top, Reminders + (Shopping in Personal mode /
/// Shutdown in Work mode) on the bottom. Every panel ends with a "[ TAP TO OPEN ]" hit region
/// that jumps to the matching full screen; todo/shopping-style panels also have a per-row
/// toggle hit region above that line.
/// </summary>
public static class TodayScreen
{
    private const int Margin = 24;
    private const int PanelGap = 20;
    private const int PanelPad = 18;
    private const int TitleScale = 3;
    private const int RowScale = 2;
    private const int HintScale = 2;
    private const int RowHeight = 40;
    private const byte Black = Canvas.Black;

    public static ScreenRenderResult Render(int width, int height, TodayScreenData data)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));
        var regions = new List<HitRegion>();

        int colWidth = (width - 2 * Margin - PanelGap) / 2;
        int rowHeight = (height - 2 * Margin - PanelGap) / 2;

        var topLeft = new Rectangle(Margin, Margin, colWidth, rowHeight);
        var topRight = new Rectangle(Margin + colWidth + PanelGap, Margin, colWidth, rowHeight);
        var bottomLeft = new Rectangle(Margin, Margin + rowHeight + PanelGap, colWidth, rowHeight);
        var bottomRight = new Rectangle(Margin + colWidth + PanelGap, Margin + rowHeight + PanelGap, colWidth, rowHeight);

        DrawAgendaPanel(image, regions, topLeft, data.TodayAgenda);

        if (data.Mode == SyncContext.Work)
        {
            DrawTodoPanel(image, regions, topRight, "WAITING ON", $"{data.WaitingOn.Count} OPEN",
                new[] { (Label: (string?)null, Todos: data.WaitingOn) }, ScreenKind.Todos);
            DrawTodoPanel(image, regions, bottomRight, "SHUTDOWN", $"{data.Shutdown.Count(t => !t.Completed)} LEFT",
                new[] { (Label: (string?)null, Todos: data.Shutdown) }, ScreenKind.Todos);
        }
        else
        {
            DrawMealsPanel(image, regions, topRight, data.MealsSummary);
            DrawShoppingSummaryPanel(image, regions, bottomRight, data.ShoppingItems);
        }

        DrawTodoPanel(image, regions, bottomLeft, "REMINDERS", null,
            new[] { (Label: (string?)"WORK", Todos: data.WorkReminders), (Label: (string?)"PERSONAL", Todos: data.PersonalReminders) },
            ScreenKind.Todos);

        return new ScreenRenderResult(image, regions);
    }

    private static void DrawAgendaPanel(Image<L8> image, List<HitRegion> regions, Rectangle rect, IReadOnlyList<AgendaEvent> agenda)
    {
        var lines = agenda
            .OrderBy(e => e.Start)
            .Select(e => $"{(e.IsAllDay ? "ALL DAY" : e.Start.ToLocalTime().ToString("HH:mm"))} {e.Title}")
            .ToList();

        DrawPanelFrame(image, rect, "AGENDA", null, out int contentTop);
        DrawLines(image, rect, contentTop, lines);
        AddHintRegion(regions, rect, new BoardAction.NavigateToScreen(ScreenKind.Agenda));
    }

    private static void DrawMealsPanel(Image<L8> image, List<HitRegion> regions, Rectangle rect, string summary)
    {
        DrawPanelFrame(image, rect, "MEALS", null, out int contentTop);
        DrawLines(image, rect, contentTop, new[] { summary });
        AddHintRegion(regions, rect, new BoardAction.NavigateToScreen(ScreenKind.Meals));
    }

    private static void DrawShoppingSummaryPanel(Image<L8> image, List<HitRegion> regions, Rectangle rect, IReadOnlyList<ShoppingItem> items)
    {
        DrawPanelFrame(image, rect, "SHOPPING", $"{items.Count} ITEMS", out int contentTop);

        int innerX = rect.X + PanelPad;
        int maxY = rect.Y + rect.Height - PanelPad - BitmapFont.GlyphHeight * HintScale - 10;
        int y = contentTop;

        foreach (var item in items.OrderBy(i => i.Checked))
        {
            if (y > maxY)
            {
                break;
            }

            DrawShoppingRow(image, regions, innerX, y, rect.Width - 2 * PanelPad, item);
            y += RowHeight;
        }

        AddHintRegion(regions, rect, new BoardAction.NavigateToScreen(ScreenKind.Shopping));
    }

    private static void DrawShoppingRow(Image<L8> image, List<HitRegion> regions, int x, int y, int width, ShoppingItem item)
    {
        string checkbox = item.Checked ? "[X]" : "[ ]";
        string line = $"{checkbox} {item.Name}";
        BitmapFont.DrawText(image, line, x, y, RowScale, Black);

        if (item.Checked)
        {
            int textWidth = BitmapFont.MeasureWidth(line, RowScale);
            int strikeY = y + (BitmapFont.GlyphHeight * RowScale) / 2;
            Canvas.StrikeThrough(image, x, strikeY, textWidth, Black);
        }

        int rowHeight = BitmapFont.GlyphHeight * RowScale + 12;
        regions.Add(new HitRegion(new Rectangle(x, y - 6, width, rowHeight), new BoardAction.ToggleShoppingItem(item.Id)));
    }

    private static void DrawTodoPanel(
        Image<L8> image,
        List<HitRegion> regions,
        Rectangle rect,
        string title,
        string? rightLabel,
        IReadOnlyList<(string? Label, IReadOnlyList<Todo> Todos)> sections,
        ScreenKind openScreen)
    {
        DrawPanelFrame(image, rect, title, rightLabel, out int contentTop);

        int innerX = rect.X + PanelPad;
        int maxY = rect.Y + rect.Height - PanelPad - BitmapFont.GlyphHeight * HintScale - 10;
        int y = contentTop;

        foreach (var section in sections)
        {
            if (y > maxY)
            {
                break;
            }

            if (section.Label is not null)
            {
                BitmapFont.DrawText(image, section.Label, innerX, y, RowScale - 1, Black);
                y += BitmapFont.GlyphHeight * (RowScale - 1) + 8;
            }

            foreach (var todo in section.Todos)
            {
                if (y > maxY)
                {
                    break;
                }

                DrawTodoRow(image, regions, innerX, y, rect.Width - 2 * PanelPad, todo);
                y += RowHeight;
            }
        }

        AddHintRegion(regions, rect, new BoardAction.NavigateToScreen(openScreen));
    }

    private static void DrawTodoRow(Image<L8> image, List<HitRegion> regions, int x, int y, int width, Todo todo)
    {
        string checkbox = todo.Completed ? "[X]" : "[ ]";
        string line = $"{checkbox} {todo.Title}";
        BitmapFont.DrawText(image, line, x, y, RowScale, Black);

        if (todo.Completed)
        {
            int textWidth = BitmapFont.MeasureWidth(line, RowScale);
            int strikeY = y + (BitmapFont.GlyphHeight * RowScale) / 2;
            Canvas.StrikeThrough(image, x, strikeY, textWidth, Black);
        }

        int rowHeight = BitmapFont.GlyphHeight * RowScale + 12;
        regions.Add(new HitRegion(new Rectangle(x, y - 6, width, rowHeight), new BoardAction.ToggleTodo(todo.Id)));
    }

    private static void DrawLines(Image<L8> image, Rectangle rect, int contentTop, IReadOnlyList<string> lines)
    {
        int innerX = rect.X + PanelPad;
        int y = contentTop;
        int maxY = rect.Y + rect.Height - PanelPad - BitmapFont.GlyphHeight * HintScale - 10;

        foreach (var line in lines)
        {
            if (y > maxY)
            {
                break;
            }

            BitmapFont.DrawText(image, line, innerX, y, RowScale, Black);
            y += RowHeight;
        }
    }

    /// <summary>Draws a panel's double-ruled box, title row, and divider; returns the y
    /// coordinate content rows should start at.</summary>
    private static void DrawPanelFrame(Image<L8> image, Rectangle rect, string title, string? rightLabel, out int contentTop)
    {
        Canvas.DrawFrame(image, rect.X, rect.Y, rect.Width, rect.Height);

        int innerX = rect.X + PanelPad;
        int innerY = rect.Y + PanelPad;
        BitmapFont.DrawText(image, title, innerX, innerY, TitleScale, Black);

        if (rightLabel is not null)
        {
            int labelWidth = BitmapFont.MeasureWidth(rightLabel, RowScale);
            BitmapFont.DrawText(image, rightLabel, rect.X + rect.Width - PanelPad - labelWidth, innerY + 6, RowScale, Black);
        }

        int ruleY = innerY + BitmapFont.GlyphHeight * TitleScale + 10;
        Canvas.FillRect(image, innerX, ruleY, rect.Width - 2 * PanelPad, 2, Black);

        contentTop = ruleY + 16;

        string hint = "[ TAP TO OPEN ]";
        int hintWidth = BitmapFont.MeasureWidth(hint, HintScale);
        int hintX = rect.X + (rect.Width - hintWidth) / 2;
        int hintY = rect.Y + rect.Height - PanelPad - BitmapFont.GlyphHeight * HintScale;
        BitmapFont.DrawText(image, hint, hintX, hintY, HintScale, Black);
    }

    private static void AddHintRegion(List<HitRegion> regions, Rectangle rect, BoardAction action)
    {
        int hintHeight = BitmapFont.GlyphHeight * HintScale + 2 * PanelPad;
        regions.Add(new HitRegion(new Rectangle(rect.X, rect.Y + rect.Height - hintHeight, rect.Width, hintHeight), action));
    }
}
