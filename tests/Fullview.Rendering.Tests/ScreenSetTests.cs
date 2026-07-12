using Fullview.Domain;
using Fullview.Rendering.Layout;

namespace Fullview.Rendering.Tests;

public class ScreenSetTests
{
    [Fact]
    public void NavigationOrder_Personal_ExcludesAgenda()
    {
        var order = ScreenSet.NavigationOrder(SyncContext.Personal);

        Assert.DoesNotContain(ScreenKind.Agenda, order);
        Assert.Contains(ScreenKind.Meals, order);
    }

    [Fact]
    public void NavigationOrder_Work_ExcludesMeals()
    {
        var order = ScreenSet.NavigationOrder(SyncContext.Work);

        Assert.Contains(ScreenKind.Agenda, order);
        Assert.DoesNotContain(ScreenKind.Meals, order);
    }

    [Fact]
    public void Next_WrapsAroundToFirstScreen()
    {
        var last = ScreenSet.NavigationOrder(SyncContext.Personal)[^1];

        var next = ScreenSet.Next(SyncContext.Personal, last);

        Assert.Equal(ScreenSet.NavigationOrder(SyncContext.Personal)[0], next);
    }

    [Fact]
    public void Previous_WrapsAroundToLastScreen()
    {
        var first = ScreenSet.NavigationOrder(SyncContext.Personal)[0];

        var previous = ScreenSet.Previous(SyncContext.Personal, first);

        Assert.Equal(ScreenSet.NavigationOrder(SyncContext.Personal)[^1], previous);
    }

    [Fact]
    public void Next_FromScreenNotInCurrentModeSet_LandsOnFirstScreen()
    {
        // Recipe is never in a mode's navigation order (reached only via a Meals tap).
        var next = ScreenSet.Next(SyncContext.Personal, ScreenKind.Recipe);

        Assert.Equal(ScreenSet.NavigationOrder(SyncContext.Personal)[0], next);
    }
}
