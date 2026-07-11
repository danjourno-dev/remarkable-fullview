using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;

namespace Fullview.Rendering.Tests;

public class BoardRendererTests
{
    private static BoardState EmptyState(SyncContext mode, ScreenKind screen) => new(
        mode,
        screen,
        OpenRecipeId: null,
        Todos: Array.Empty<Todo>(),
        AgendaEvents: Array.Empty<AgendaEvent>(),
        Meals: Array.Empty<Meal>(),
        ShoppingItems: Array.Empty<ShoppingItem>(),
        Recipes: Array.Empty<Recipe>(),
        InboxPages: Array.Empty<InboxPage>(),
        Now: new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Render_ProducesFullBoardSizedImage()
    {
        var result = BoardRenderer.Render(1404, 1872, EmptyState(SyncContext.Personal, ScreenKind.Today));

        Assert.Equal(1404, result.Image.Width);
        Assert.Equal(1872, result.Image.Height);
    }

    [Fact]
    public void Render_IncludesEdgeNavRegions()
    {
        var result = BoardRenderer.Render(1404, 1872, EmptyState(SyncContext.Personal, ScreenKind.Today));

        Assert.Contains(result.Regions, r => r.Action is BoardAction.NavigatePrevious);
        Assert.Contains(result.Regions, r => r.Action is BoardAction.NavigateNext);
    }

    [Fact]
    public void Render_HasNoTapTargetForModeSwitch()
    {
        // Mockup v4: mode switches via the reMarkable's physical hardware button, not a tap.
        var result = BoardRenderer.Render(1404, 1872, EmptyState(SyncContext.Personal, ScreenKind.Today));

        Assert.DoesNotContain(result.Regions, r => r.Action is BoardAction.ToggleMode);
    }

    [Fact]
    public void Render_TodayScreen_PanelsLinkToTheirFullScreens()
    {
        var result = BoardRenderer.Render(1404, 1872, EmptyState(SyncContext.Personal, ScreenKind.Today));

        Assert.Contains(result.Regions, r => r.Action is BoardAction.NavigateToScreen(ScreenKind.Agenda));
        Assert.Contains(result.Regions, r => r.Action is BoardAction.NavigateToScreen(ScreenKind.Meals));
        Assert.Contains(result.Regions, r => r.Action is BoardAction.NavigateToScreen(ScreenKind.Shopping));
        Assert.Contains(result.Regions, r => r.Action is BoardAction.NavigateToScreen(ScreenKind.Todos));
    }

    [Fact]
    public void Render_TodoHitRegion_IsOffsetIntoBodyArea()
    {
        var state = EmptyState(SyncContext.Personal, ScreenKind.Todos) with
        {
            Todos = new[]
            {
                new Todo { Id = "t1", Context = SyncContext.Personal, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "test", Title = "Book gym" }
            }
        };

        var result = BoardRenderer.Render(1404, 1872, state);

        var todoRegion = Assert.Single(result.Regions, r => r.Action is BoardAction.ToggleTodo);
        Assert.True(todoRegion.Bounds.Y >= NowNextStrip.Height, "Todo row must be composited below the strip, not overlapping it.");
    }

    [Fact]
    public void Render_TodosScreen_OnlyShowsCurrentModeContext()
    {
        var state = EmptyState(SyncContext.Personal, ScreenKind.Todos) with
        {
            Todos = new[]
            {
                new Todo { Id = "p1", Context = SyncContext.Personal, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "test", Title = "Personal task" },
                new Todo { Id = "w1", Context = SyncContext.Work, UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = "test", Title = "Work task" }
            }
        };

        var result = BoardRenderer.Render(1404, 1872, state);

        Assert.Single(result.Regions, r => r.Action is BoardAction.ToggleTodo action && action.TodoId == "p1");
        Assert.DoesNotContain(result.Regions, r => r.Action is BoardAction.ToggleTodo action && action.TodoId == "w1");
    }

    [Fact]
    public void Render_UnknownRecipeId_DoesNotThrow()
    {
        var state = EmptyState(SyncContext.Personal, ScreenKind.Recipe) with { OpenRecipeId = "missing" };

        var exception = Record.Exception(() => BoardRenderer.Render(1404, 1872, state));

        Assert.Null(exception);
    }
}
