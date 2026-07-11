namespace Fullview.Rendering.Layout;

/// <summary>
/// The board's screens (B4 nav order). <see cref="Recipe"/> is reached only by tapping a
/// meal — it is deliberately excluded from <see cref="ScreenSet.NavigationOrder"/>.
/// Routines are v1.5 (Stage 8) and have no screen yet.
/// </summary>
public enum ScreenKind
{
    Today,
    Todos,
    Agenda,
    Meals,
    Shopping,
    Recipe
}
