namespace Fullview.Rendering.Layout;

/// <summary>
/// B2's "max ~7 items visible per list (overflow = '+4 more')" rule, shared by every
/// list-shaped screen (Todos, Agenda, Shopping).
/// </summary>
public static class ListPage
{
    public const int MaxVisibleItems = 7;

    /// <summary>Returns the items to render plus how many were left off (0 if none).</summary>
    public static (IReadOnlyList<T> Visible, int Overflow) Paginate<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= MaxVisibleItems)
        {
            return (items, 0);
        }

        var visible = new List<T>(MaxVisibleItems - 1);
        for (int i = 0; i < MaxVisibleItems - 1; i++)
        {
            visible.Add(items[i]);
        }

        return (visible, items.Count - visible.Count);
    }
}
