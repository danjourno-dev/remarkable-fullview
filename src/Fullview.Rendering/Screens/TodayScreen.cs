using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Screens;

/// <summary>Pre-filtered data for TodayScreen — filtering by mode/date is the caller's job
/// (BoardRenderer), this screen only lays out what it's given. WorkReminders/PersonalReminders
/// both carry the full unfiltered set; the Reminders panel itself picks whichever one matches
/// <see cref="Mode"/> (Work reminders in Work Ops, Personal in Life Ops) — unlike
/// NowNextStrip's Now/Next, which stays cross-context. WaitingOn and Shutdown are Work-only and
/// empty in Personal mode.</summary>
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
/// Shutdown in Work mode) on the bottom. Agenda and Meals panels end with a "[ TAP TO OPEN ]"
/// hit region that jumps to their full screen; todo/shopping-style panels have no full screen
/// of their own — each row is a <see cref="ToggleableRow"/> spanning the whole row.
/// </summary>
public static class TodayScreen
{
    private const int Margin = 24;
    private const int PanelGap = 20;
    private const int PanelPad = 18;
    private const int TitleSize = 22;
    private const int RowSize = 32;
    private const int HintSize = 16;
    public const int RowHeight = 90;
    private const byte Black = Canvas.Black;

    private static readonly Font TitleFont = AppFont.Bold(TitleSize);
    private static readonly Font PanelLabelFont = AppFont.Regular(TitleSize);
    private static readonly Font RowFont = AppFont.Regular(RowSize);
    private static readonly Font HintFont = AppFont.Regular(HintSize);

    // Panels are cached independently by role (not grid position) and only re-rasterized when
    // their own content key changes. A single-row tap (e.g. toggling one todo) invalidates one
    // panel; the other three are blitted from cache instead of re-running SixLabors' glyph
    // rasterization, which is the actual bottleneck on the rM1's CPU (see AppFont.DrawText).
    private static readonly Dictionary<string, (string ContentKey, Image<L8> Image)> PanelCache = new();

    // The frame/title/rule/hint chrome never changes for a given panel role, size, and hint
    // flag — a todo/shopping toggle invalidates the panel's row content but not its title, so
    // there's no reason to re-rasterize the title text on every single-row tap. Cached
    // separately from PanelCache (which is keyed by row content) and reused across renders.
    private static readonly Dictionary<string, (int Width, int Height, Image<L8> Image)> ChromeCache = new();

    /// <summary>Converts a raw framebuffer-pixel drag delta into a row count for the mini
    /// Agenda panel, whose row height differs from the full-screen Agenda's.</summary>
    public static int RowsForDrag(int fbDeltaY) => (int)Math.Round(-fbDeltaY / (double)RowHeight);

    /// <summary>How many agenda rows fit in the mini panel's content area, given the panel's
    /// pixel height. Used both to window the visible rows and to clamp the scroll offset.</summary>
    public static int AgendaCapacity(int panelHeight)
    {
        int contentTop = PanelPad + AppFont.LineHeight(TitleFont) + 10 + 16;
        int contentBottom = panelHeight - PanelPad - AppFont.LineHeight(HintFont) - 10;
        return Math.Max(1, (contentBottom - contentTop) / RowHeight + 1);
    }

    /// <summary>The mini Agenda panel's pixel height for a given full screen height, mirroring
    /// the grid math in Render. Lets callers outside this class (e.g. the drag-scroll clamp in
    /// Program.cs's Apply) compute AgendaCapacity without duplicating the grid layout.</summary>
    public static int AgendaPanelHeight(int screenHeight) => (screenHeight - 2 * Margin - PanelGap) / 2;

    public static ScreenRenderResult Render(int width, int height, TodayScreenData data, int agendaScrollOffset = 0)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));
        var regions = new List<HitRegion>();

        int colWidth = (width - 2 * Margin - PanelGap) / 2;
        int rowHeight = (height - 2 * Margin - PanelGap) / 2;

        var topLeft = new Rectangle(Margin, Margin, colWidth, rowHeight);
        var topRight = new Rectangle(Margin + colWidth + PanelGap, Margin, colWidth, rowHeight);
        var bottomLeft = new Rectangle(Margin, Margin + rowHeight + PanelGap, colWidth, rowHeight);
        var bottomRight = new Rectangle(Margin + colWidth + PanelGap, Margin + rowHeight + PanelGap, colWidth, rowHeight);

        DrawAgendaPanel(image, regions, topLeft, data.TodayAgenda, agendaScrollOffset);

        if (data.Mode == SyncContext.Work)
        {
            DrawTodoPanel(image, regions, "waitingOn", topRight, "WAITING ON", $"{data.WaitingOn.Count} OPEN", false,
                id => new BoardAction.ToggleTodo(id), data.WaitingOn);
            DrawTodoPanel(image, regions, "shutdown", bottomRight, "SHUTDOWN", $"{data.Shutdown.Count(t => !t.Completed)} LEFT", false,
                id => new BoardAction.ToggleTodo(id), data.Shutdown);
        }
        else
        {
            DrawMealsPanel(image, regions, topRight, data.MealsSummary);
            DrawShoppingSummaryPanel(image, regions, bottomRight, data.ShoppingItems);
        }

        var reminders = data.Mode == SyncContext.Work ? data.WorkReminders : data.PersonalReminders;
        DrawTodoPanel(image, regions, "reminders", bottomLeft, "REMINDERS", null, false,
            id => new BoardAction.ToggleTodo(id), reminders);

        return new ScreenRenderResult(image, regions);
    }

    /// <summary>Renders (or reuses the cached bitmap for) a panel-sized image and composites it
    /// into <paramref name="image"/> at <paramref name="rect"/>'s position.</summary>
    private static void RenderPanel(Image<L8> image, string cacheId, string contentKey, Rectangle rect, Action<Image<L8>> render)
    {
        if (!PanelCache.TryGetValue(cacheId, out var cached)
            || cached.ContentKey != contentKey
            || cached.Image.Width != rect.Width
            || cached.Image.Height != rect.Height)
        {
            var panel = new Image<L8>(rect.Width, rect.Height, new L8(Canvas.White));
            render(panel);
            cached = (contentKey, panel);
            PanelCache[cacheId] = cached;
        }

        Canvas.Composite(image, cached.Image, rect.X, rect.Y);
    }

    private static void DrawAgendaPanel(Image<L8> image, List<HitRegion> regions, Rectangle rect, IReadOnlyList<AgendaEvent> agenda, int scrollOffset)
    {
        var lines = agenda
            .OrderBy(e => e.Start)
            .Select(e => $"{(e.IsAllDay ? "ALL DAY" : e.Start.ToLocal().ToString("HH:mm"))} {e.Title}")
            .ToList();

        int capacity = AgendaCapacity(rect.Height);
        int offset = Math.Clamp(scrollOffset, 0, Math.Max(0, lines.Count - capacity));
        var visible = lines.Skip(offset).Take(capacity).ToList();

        string contentKey = $"{offset}|" + string.Join(";", visible);
        RenderPanel(image, "agenda", contentKey, rect, panel =>
        {
            DrawPanelFrame(panel, rect.Width, rect.Height, "AGENDA", null, hasHint: true, out int contentTop, out int contentBottom);
            DrawLines(panel, contentTop, contentBottom, visible);
        });

        AddOpenScreenHint(regions, rect, ScreenKind.Agenda);
    }

    private static void DrawMealsPanel(Image<L8> image, List<HitRegion> regions, Rectangle rect, string summary)
    {
        RenderPanel(image, "meals", summary, rect, panel =>
        {
            DrawPanelFrame(panel, rect.Width, rect.Height, "MEALS", null, hasHint: true, out int contentTop, out int contentBottom);
            DrawLines(panel, contentTop, contentBottom, new[] { summary });
        });

        AddOpenScreenHint(regions, rect, ScreenKind.Meals);
    }

    private static void DrawShoppingSummaryPanel(Image<L8> image, List<HitRegion> regions, Rectangle rect, IReadOnlyList<ShoppingItem> items)
    {
        var ordered = items.OrderBy(i => i.Checked).ToList();
        string contentKey = $"{items.Count}|" + string.Join(";", ordered.Select(i => $"{i.Id}:{(i.Checked ? "C" : "O")}:{i.Name}"));

        RenderPanel(image, "shopping", contentKey, rect, panel =>
        {
            DrawPanelFrame(panel, rect.Width, rect.Height, "SHOPPING", $"{items.Count} ITEMS", hasHint: false, out int contentTop, out int contentBottom);

            int innerX = PanelPad;
            int innerWidth = rect.Width - 2 * PanelPad;
            int y = contentTop;
            bool any = false;

            foreach (var item in ordered)
            {
                if (y > contentBottom)
                {
                    break;
                }

                if (!any)
                {
                    Canvas.DrawDivider(panel, innerX, y - 8, innerWidth);
                    any = true;
                }

                DrawShoppingRow(panel, innerX, y, innerWidth, item);
                y += RowHeight;
                Canvas.DrawDivider(panel, innerX, y - 8, innerWidth);
            }
        });

        int localY = PanelPad + AppFont.LineHeight(TitleFont) + 10 + 16;
        int localInnerX = PanelPad;
        int localInnerWidth = rect.Width - 2 * PanelPad;
        int contentBottomLocal = rect.Height - PanelPad;
        foreach (var item in ordered)
        {
            if (localY > contentBottomLocal)
            {
                break;
            }

            regions.Add(new HitRegion(
                new Rectangle(rect.X + localInnerX, rect.Y + localY - ToggleableRow.RegionYOffset, localInnerWidth, RowHeight),
                new BoardAction.ToggleShoppingItem(item.Id)));
            localY += RowHeight;
        }
    }

    private static void DrawShoppingRow(Image<L8> panel, int x, int y, int width, ShoppingItem item) =>
        ToggleableRow.DrawContent(panel, x, y, RowFont, item.Name, item.Checked, Black);

    private static void DrawTodoPanel(
        Image<L8> image,
        List<HitRegion> regions,
        string cacheId,
        Rectangle rect,
        string title,
        string? rightLabel,
        bool hasHint,
        Func<string, BoardAction> action,
        IReadOnlyList<Todo> todos)
    {
        string contentKey = $"{title}|{rightLabel}|" + string.Join(";", todos.Select(t => $"{t.Id}:{(t.Completed ? "C" : "O")}:{t.Title}"));

        RenderPanel(image, cacheId, contentKey, rect, panel =>
        {
            DrawPanelFrame(panel, rect.Width, rect.Height, title, rightLabel, hasHint, out int contentTop, out int contentBottom);

            int innerX = PanelPad;
            int innerWidth = rect.Width - 2 * PanelPad;
            int y = contentTop;
            bool any = false;

            foreach (var todo in todos)
            {
                if (y > contentBottom)
                {
                    break;
                }

                if (!any)
                {
                    Canvas.DrawDivider(panel, innerX, y - 8, innerWidth);
                    any = true;
                }

                ToggleableRow.DrawContent(panel, innerX, y, RowFont, todo.Title, todo.Completed, Black);
                y += RowHeight;
                Canvas.DrawDivider(panel, innerX, y - 8, innerWidth);
            }
        });

        int localInnerX = PanelPad;
        int localInnerWidth = rect.Width - 2 * PanelPad;
        int localContentTop = PanelPad + AppFont.LineHeight(TitleFont) + 10 + 16;
        int localContentBottom = hasHint
            ? rect.Height - PanelPad - AppFont.LineHeight(HintFont) - 10
            : rect.Height - PanelPad;

        int y2 = localContentTop;
        foreach (var todo in todos)
        {
            if (y2 > localContentBottom)
            {
                break;
            }

            regions.Add(new HitRegion(
                new Rectangle(rect.X + localInnerX, rect.Y + y2 - ToggleableRow.RegionYOffset, localInnerWidth, RowHeight),
                action(todo.Id)));
            y2 += RowHeight;
        }
    }

    private static void DrawLines(Image<L8> panel, int contentTop, int contentBottom, IReadOnlyList<string> lines)
    {
        int innerX = PanelPad;
        int y = contentTop;

        foreach (var line in lines)
        {
            if (y > contentBottom)
            {
                break;
            }

            AppFont.DrawText(panel, line, innerX, y, RowFont, Black);
            y += RowHeight;
        }
    }

    /// <summary>Adds the "[ TAP TO OPEN ]" hit region for a panel with a linked full screen, in
    /// absolute board-body coordinates. Kept separate from panel rendering (and always
    /// recomputed) since hit regions are cheap and must stay in sync even when the panel bitmap
    /// itself is served from cache.</summary>
    private static void AddOpenScreenHint(List<HitRegion> regions, Rectangle rect, ScreenKind screen)
    {
        int hintHeight = AppFont.LineHeight(HintFont) + 2 * PanelPad;
        regions.Add(new HitRegion(new Rectangle(rect.X, rect.Y + rect.Height - hintHeight, rect.Width, hintHeight),
            new BoardAction.NavigateToScreen(screen)));
    }

    /// <summary>Draws a panel's double-ruled box, title row, and divider onto a panel-local
    /// image (origin (0,0)); returns the local y range content rows may occupy. When
    /// <paramref name="hasHint"/> is set, reserves space at the bottom for a
    /// "[ TAP TO OPEN ]" label — panels with no full screen of their own (todos, shopping) pass
    /// <see langword="false"/> and get the full panel height for rows instead.</summary>
    private static void DrawPanelFrame(
        Image<L8> panel, int panelWidth, int panelHeight, string title, string? rightLabel,
        bool hasHint, out int contentTop, out int contentBottom)
    {
        Canvas.Composite(panel, GetPanelChrome(title, hasHint, panelWidth, panelHeight), 0, 0);

        int innerY = PanelPad;

        if (rightLabel is not null)
        {
            int labelWidth = AppFont.MeasureWidth(rightLabel, PanelLabelFont);
            AppFont.DrawText(panel, rightLabel, panelWidth - PanelPad - labelWidth, innerY, PanelLabelFont, Black);
        }

        int ruleY = innerY + AppFont.LineHeight(TitleFont) + 10;
        contentTop = ruleY + 16;

        contentBottom = hasHint
            ? panelHeight - PanelPad - AppFont.LineHeight(HintFont) - 10
            : panelHeight - PanelPad;
    }

    /// <summary>Renders (or reuses the cached bitmap for) a panel's static chrome — border,
    /// title, divider rule, and "[ TAP TO OPEN ]" hint if present. None of this depends on row
    /// content, so it's cached independently of <see cref="PanelCache"/> and survives row-level
    /// invalidation (e.g. toggling a single todo).</summary>
    private static Image<L8> GetPanelChrome(string title, bool hasHint, int panelWidth, int panelHeight)
    {
        string key = $"{title}|{hasHint}|{panelWidth}x{panelHeight}";
        if (ChromeCache.TryGetValue(key, out var cached)
            && cached.Width == panelWidth && cached.Height == panelHeight)
        {
            return cached.Image;
        }

        var chrome = new Image<L8>(panelWidth, panelHeight, new L8(Canvas.White));
        Canvas.DrawFrame(chrome, 0, 0, panelWidth, panelHeight);

        int innerX = PanelPad;
        int innerY = PanelPad;
        AppFont.DrawText(chrome, title, innerX, innerY, TitleFont, Black);

        int ruleY = innerY + AppFont.LineHeight(TitleFont) + 10;
        Canvas.FillRect(chrome, innerX, ruleY, panelWidth - 2 * PanelPad, 2, Black);

        if (hasHint)
        {
            string hint = "[ TAP TO OPEN ]";
            int hintWidth = AppFont.MeasureWidth(hint, HintFont);
            int hintX = (panelWidth - hintWidth) / 2;
            int hintY = panelHeight - PanelPad - AppFont.LineHeight(HintFont);
            AppFont.DrawText(chrome, hint, hintX, hintY, HintFont, Black);
        }

        ChromeCache[key] = (panelWidth, panelHeight, chrome);
        return chrome;
    }
}
