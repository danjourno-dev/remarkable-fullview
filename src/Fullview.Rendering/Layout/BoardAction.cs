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

    /// <summary>Manual sync, triggered by tapping the footer's sync-status text (Stage 5).</summary>
    public sealed record SyncNow : BoardAction;
}
