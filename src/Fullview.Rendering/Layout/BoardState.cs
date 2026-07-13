using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Rendering.Layout;

/// <summary>Everything BoardRenderer needs for one frame. Mode/current-screen are
/// device-local UI state (B5); the entity lists are the full local store snapshot —
/// BoardRenderer does the mode filtering, not the caller.</summary>
public sealed record BoardState(
    SyncContext Mode,
    ScreenKind CurrentScreen,
    string? OpenRecipeId,
    IReadOnlyList<Todo> Todos,
    IReadOnlyList<AgendaEvent> AgendaEvents,
    IReadOnlyList<Meal> Meals,
    IReadOnlyList<ShoppingItem> ShoppingItems,
    IReadOnlyList<Recipe> Recipes,
    IReadOnlyList<InboxPage> InboxPages,
    DateTimeOffset Now,
    DateTimeOffset? LastSyncedAt = null,
    int PendingSyncCount = 0,
    int AgendaScrollOffset = 0,
    int TodayAgendaScrollOffset = 0)
{
    public BoardState WithMode(SyncContext mode) =>
        this with { Mode = mode, CurrentScreen = ScreenSet.NavigationOrder(mode)[0], AgendaScrollOffset = 0, TodayAgendaScrollOffset = 0 };

    public BoardState WithScreen(ScreenKind screen) =>
        this with { CurrentScreen = screen, OpenRecipeId = null, AgendaScrollOffset = 0, TodayAgendaScrollOffset = 0 };

    public BoardState WithOpenRecipe(string recipeId) => this with { CurrentScreen = ScreenKind.Recipe, OpenRecipeId = recipeId };
}
