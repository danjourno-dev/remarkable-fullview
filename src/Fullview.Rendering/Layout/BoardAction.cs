namespace Fullview.Rendering.Layout;

/// <summary>
/// What tapping a hit region does. Kept as a closed record hierarchy (rather than a
/// loose string action id) so Program.cs's dispatch loop is exhaustively pattern-matchable.
/// </summary>
public abstract record BoardAction
{
    public sealed record ToggleMode : BoardAction;

    public sealed record NavigatePrevious : BoardAction;

    public sealed record NavigateNext : BoardAction;

    public sealed record ToggleTodo(string TodoId) : BoardAction;

    public sealed record ToggleShoppingItem(string ItemId) : BoardAction;

    public sealed record OpenRecipe(string RecipeId) : BoardAction;

    /// <summary>Jumps straight to a screen (mockup v4's Today-panel "[ TAP TO OPEN ]" links) —
    /// unlike NavigatePrevious/Next this ignores the current mode's ScreenSet order, the same
    /// way OpenRecipe does.</summary>
    public sealed record NavigateToScreen(ScreenKind Screen) : BoardAction;

    /// <summary>Manual sync, triggered by tapping the footer's sync-status text or the
    /// Now/Next strip's sync button (Stage 5) — always gives immediate feedback, unlike
    /// <see cref="BackgroundSync"/>.</summary>
    public sealed record SyncNow : BoardAction;

    /// <summary>Auto-triggered sync (wifi reconnect, fallback timer) — distinct from
    /// <see cref="SyncNow"/> so Program.cs can skip a full render/e-ink refresh when nothing
    /// changed, instead of always giving user-facing feedback.</summary>
    public sealed record BackgroundSync : BoardAction;
}
