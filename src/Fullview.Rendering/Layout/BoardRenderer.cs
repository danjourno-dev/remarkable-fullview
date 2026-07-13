using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Screens;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Top-level render entry point: composes the header, the shared strip, the current screen's
/// body, the footer, and the left/right edge-tap navigation zones into one frame with a
/// merged hit-region list.
/// </summary>
public static class BoardRenderer
{
    public const int EdgeNavWidth = 90;

    /// <summary>The count AgendaScreen actually renders (mode-filtered, today-only) — used
    /// by Program.cs's ScrollAgenda handling to clamp BoardState.AgendaScrollOffset against
    /// the same set the screen pages through, not the full unfiltered AgendaEvents list.</summary>
    public static int AgendaEntryCount(BoardState state) =>
        TodayAgenda(state, DateOnly.FromDateTime(state.Now.ToLocal().DateTime)).Count;

    public static ScreenRenderResult Render(int width, int height, BoardState state, string version = "dev")
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));
        var regions = new List<HitRegion>();

        var today = DateOnly.FromDateTime(state.Now.ToLocal().DateTime);
        string inboxStatus = InboxStatus(state);

        var header = Header.Render(width, state.Mode, today, inboxStatus);
        Canvas.Composite(image, header, 0, 0);

        var stripData = NowNextCalculator.Compute(state.AgendaEvents, state.Now);
        var (strip, syncButtonBounds) = NowNextStrip.Render(width, stripData);
        Canvas.Composite(image, strip, 0, Header.Height);
        regions.Add(new HitRegion(syncButtonBounds.WithOffset(0, Header.Height), new BoardAction.SyncNow()));

        int bodyY = Header.Height + NowNextStrip.Height;
        int bodyHeight = height - bodyY - Footer.Height;
        var body = RenderBody(width, bodyHeight, state, today);

        Canvas.Composite(image, body.Image, 0, bodyY);
        foreach (var region in body.Regions)
        {
            regions.Add(region with { Bounds = region.Bounds.WithOffset(0, bodyY) });
        }

        string syncStatus = SyncStatus(state);
        var (footer, syncStatusBounds) = Footer.Render(width, inboxStatus, syncStatus, state.Mode, version);
        Canvas.Composite(image, footer, 0, height - Footer.Height);
        regions.Add(new HitRegion(syncStatusBounds.WithOffset(0, height - Footer.Height), new BoardAction.SyncNow()));

        regions.Add(new HitRegion(new Rectangle(0, bodyY, EdgeNavWidth, bodyHeight), new BoardAction.NavigatePrevious()));
        regions.Add(new HitRegion(new Rectangle(width - EdgeNavWidth, bodyY, EdgeNavWidth, bodyHeight), new BoardAction.NavigateNext()));

        return new ScreenRenderResult(image, regions);
    }

    private static ScreenRenderResult RenderBody(int width, int height, BoardState state, DateOnly today)
    {
        var mode = state.Mode;

        return state.CurrentScreen switch
        {
            ScreenKind.Today => TodayScreen.Render(width, height, BuildTodayData(state, today), state.TodayAgendaScrollOffset),
            ScreenKind.Agenda => AgendaScreen.Render(width, height, TodayAgenda(state, today), state.Now, state.AgendaScrollOffset),
            ScreenKind.Meals => MealsScreen.Render(width, height, Active(state.Meals), RecipesById(state)),
            ScreenKind.Recipe => RenderRecipe(width, height, state),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state.CurrentScreen, "Unknown screen kind.")
        };
    }

    private static ScreenRenderResult RenderRecipe(int width, int height, BoardState state)
    {
        var recipe = state.Recipes.FirstOrDefault(r => r.Id == state.OpenRecipeId && !r.Deleted);
        if (recipe is null)
        {
            var image = new Image<L8>(width, height, new L8(Canvas.White));
            AppFont.DrawText(image, "RECIPE NOT FOUND", 24, 24, AppFont.Bold(22), Canvas.Black);
            return new ScreenRenderResult(image, Array.Empty<HitRegion>());
        }

        return RecipeScreen.Render(width, height, recipe);
    }

    private static IReadOnlyList<AgendaEvent> TodayAgenda(BoardState state, DateOnly today) =>
        FilterByContext(state.AgendaEvents, state.Mode)
            .Where(e => DateOnly.FromDateTime(e.Start.ToLocal().DateTime) == today)
            .ToList();

    private static TodayScreenData BuildTodayData(BoardState state, DateOnly today)
    {
        var todayAgenda = TodayAgenda(state, today);

        var activeTodos = Active(state.Todos);

        var todaysMeals = Active(state.Meals).Where(m => m.Date == today).OrderBy(m => m.Slot).ToList();
        string mealsSummary = todaysMeals.Count == 0
            ? "—"
            : string.Join("  ", todaysMeals.Select(m => $"{(m.Slot == MealSlot.Breakfast ? "B" : "D")}: {m.Description ?? "—"}"));

        var shoppingItems = Active(state.ShoppingItems);

        // Reminders carries both contexts unfiltered; TodayScreen picks the one matching Mode
        // (Work reminders in Work Ops, Personal in Life Ops) — unlike NowNextStrip's Now/Next,
        // which stays cross-context.
        var workReminders = activeTodos.Where(t => t.Context == SyncContext.Work).OrderBy(t => t.Completed).ToList();
        var personalReminders = activeTodos.Where(t => t.Context == SyncContext.Personal).OrderBy(t => t.Completed).ToList();

        // Work-only panels: repurpose Todos rather than pulling Routine/RoutineCheck forward
        // from Stage 8 (see ScreenKind's "Routines are v1.5" note) or inventing a "waiting on"
        // entity that doesn't exist yet.
        var workTodos = activeTodos.Where(t => t.Context == SyncContext.Work && !t.Completed).ToList();
        var waitingOn = workTodos.Where(t => t.Priority == TodoPriority.Focus).ToList();
        var shutdown = workTodos.Where(t => t.Priority != TodoPriority.Focus).ToList();

        return new TodayScreenData(
            today,
            todayAgenda,
            mealsSummary,
            shoppingItems,
            waitingOn,
            shutdown,
            workReminders,
            personalReminders,
            state.Mode);
    }

    private static string InboxStatus(BoardState state)
    {
        int queuedPages = state.InboxPages.Count(p => !p.Deleted && p.State == InboxPageState.Queued);
        return queuedPages == 0 ? "ALL CLEAR" : $"{queuedPages} PAGE{(queuedPages == 1 ? "" : "S")}";
    }

    private static string SyncStatus(BoardState state)
    {
        string pending = state.PendingSyncCount == 0 ? "" : $" · {state.PendingSyncCount} PENDING";
        return state.LastSyncedAt is { } at ? $"SYNCED {at.ToLocal():HH:mm}{pending}" : $"NOT SYNCED{pending}";
    }

    private static IReadOnlyList<T> FilterByContext<T>(IReadOnlyList<T> entities, SyncContext mode) where T : SyncEntity =>
        entities.Where(e => !e.Deleted && e.Context == mode).ToList();

    private static IReadOnlyList<T> Active<T>(IReadOnlyList<T> entities) where T : SyncEntity =>
        entities.Where(e => !e.Deleted).ToList();

    private static IReadOnlyDictionary<string, Recipe> RecipesById(BoardState state) =>
        Active(state.Recipes).ToDictionary(r => r.Id);
}

file static class RectangleExtensions
{
    public static Rectangle WithOffset(this Rectangle rect, int dx, int dy) =>
        new(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
}
