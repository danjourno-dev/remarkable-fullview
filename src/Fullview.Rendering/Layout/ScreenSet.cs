using Fullview.Domain;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Per-mode screen sets (B3): Personal = Today, Meals; Work = Today, Agenda. Edge-tap
/// navigation cycles through whichever list matches the current mode. Todos and shopping
/// items are reached via Today's panels rather than their own screens.
/// </summary>
public static class ScreenSet
{
    private static readonly List<ScreenKind> Personal = [ScreenKind.Today, ScreenKind.Meals];
    private static readonly List<ScreenKind> Work = [ScreenKind.Today, ScreenKind.Agenda];

    public static IReadOnlyList<ScreenKind> NavigationOrder(SyncContext mode) =>
        mode == SyncContext.Work ? Work : Personal;

    public static ScreenKind Next(SyncContext mode, ScreenKind current) => Step(mode, current, +1);

    public static ScreenKind Previous(SyncContext mode, ScreenKind current) => Step(mode, current, -1);

    private static ScreenKind Step(SyncContext mode, ScreenKind current, int delta)
    {
        var order = NavigationOrder(mode);
        int index = -1;
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == current)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            // Current screen isn't in this mode's set (e.g. Recipe, or Agenda while Personal) —
            // land on the first screen of the new set rather than guessing an offset.
            return order[0];
        }

        int next = ((index + delta) % order.Count + order.Count) % order.Count;
        return order[next];
    }
}
