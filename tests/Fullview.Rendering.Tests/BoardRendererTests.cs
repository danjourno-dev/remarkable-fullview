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
    }

    [Fact]
    public void Render_TodoHitRegion_IsOffsetIntoBodyArea()
    {
        var state = EmptyState(SyncContext.Personal, ScreenKind.Today) with
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
    public void Render_RemindersPanel_OnlyShowsCurrentModeContext()
    {
        var state = EmptyState(SyncContext.Personal, ScreenKind.Today) with
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
    public void Render_AgendaScreen_ExcludesEventsNotOnToday()
    {
        var today = DateOnly.FromDateTime(new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero).LocalDateTime);
        var todayStart = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(9);
        var state = EmptyState(SyncContext.Work, ScreenKind.Agenda) with
        {
            AgendaEvents = new[]
            {
                new AgendaEvent
                {
                    Id = "today", Context = SyncContext.Work, UpdatedAt = todayStart, UpdatedBy = "test",
                    Title = "Today meeting", Start = todayStart, End = todayStart.AddHours(1)
                },
                new AgendaEvent
                {
                    Id = "yesterday", Context = SyncContext.Work, UpdatedAt = todayStart, UpdatedBy = "test",
                    Title = "Stray dev event", Start = todayStart.AddDays(-1), End = todayStart.AddDays(-1).AddHours(1)
                }
            }
        };

        var result = BoardRenderer.Render(1404, 1872, state);

        int bodyY = Fullview.Rendering.Layout.Header.Height + Fullview.Rendering.Layout.NowNextStrip.Height;
        const int margin = 24;
        const int rowHeight = 105;
        int secondRowY = bodyY + margin + 32 + margin + rowHeight;

        byte darkest = 255;
        for (int py = secondRowY; py < secondRowY + rowHeight; py++)
        {
            for (int px = margin; px < 1404 - margin; px++)
            {
                byte value = result.Image[px, py].PackedValue;
                if (value < darkest)
                {
                    darkest = value;
                }
            }
        }

        Assert.Equal(255, darkest);
    }

    [Fact]
    public void Render_UnknownRecipeId_DoesNotThrow()
    {
        var state = EmptyState(SyncContext.Personal, ScreenKind.Recipe) with { OpenRecipeId = "missing" };

        var exception = Record.Exception(() => BoardRenderer.Render(1404, 1872, state));

        Assert.Null(exception);
    }
}
