namespace Fullview.Rendering.Layout;

/// <summary>
/// The board's screens (B4 nav order). <see cref="Recipe"/> is reached only by tapping a
/// meal — it is deliberately excluded from <see cref="ScreenSet.NavigationOrder"/>. Todos and
/// shopping items live only in Today's panels now (no standalone full screen); Routines are
/// v1.5 (Stage 8) and have no screen yet.
/// </summary>
public enum ScreenKind
{
    Today,
    Agenda,
    Meals,
    Recipe
}
