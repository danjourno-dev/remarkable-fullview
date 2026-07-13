using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using Fullview.Rendering.Screens;

namespace Fullview.Rendering.Tests;

public class TodayScreenTests
{
    private const int Width = 1404;
    private const int Height = 1872;

    // Mirrors TodayScreen's private grid/panel constants so tests can locate the mini Agenda
    // panel's rows without exposing internal layout details as public API.
    private const int Margin = 24;
    private const int PanelGap = 20;
    private const int PanelPad = 18;
    private const int TitleLineHeight = 22;

    private static AgendaEvent Event(string title, DateTimeOffset start) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Context = SyncContext.Work,
            UpdatedAt = start,
            UpdatedBy = "test",
            Title = title,
            Start = start,
            End = start.AddHours(1),
            IsAllDay = false,
        };

    private static TodayScreenData Data(IReadOnlyList<AgendaEvent> agenda) => new(
        Date: DateOnly.FromDateTime(DateTime.Today),
        TodayAgenda: agenda,
        MealsSummary: "",
        ShoppingItems: Array.Empty<ShoppingItem>(),
        WaitingOn: Array.Empty<Todo>(),
        Shutdown: Array.Empty<Todo>(),
        WorkReminders: Array.Empty<Todo>(),
        PersonalReminders: Array.Empty<Todo>(),
        Mode: SyncContext.Personal);

    [Theory]
    [InlineData(-180, 2)]
    [InlineData(90, -1)]
    [InlineData(0, 0)]
    public void RowsForDrag_ConvertsFbPixelDeltaToRows(int fbDeltaY, int expectedRows) =>
        Assert.Equal(expectedRows, TodayScreen.RowsForDrag(fbDeltaY));

    [Fact]
    public void Render_MoreEventsThanPanelCapacity_ScrollingReachesTheRest()
    {
        var now = new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero);
        int panelHeight = TodayScreen.AgendaPanelHeight(Height);
        int capacity = TodayScreen.AgendaCapacity(panelHeight);

        var events = Enumerable.Range(0, capacity + 3)
            .Select(i => Event($"Event {i}", now.AddHours(i)))
            .ToArray();

        var firstPage = TodayScreen.Render(Width, Height, Data(events));
        Assert.True(RowHasContent(firstPage, capacity - 1), "Expected the panel to fill to capacity.");

        int maxOffset = events.Length - capacity;
        var lastPage = TodayScreen.Render(Width, Height, Data(events), agendaScrollOffset: maxOffset);
        Assert.True(RowHasContent(lastPage, capacity - 1), "Expected the last event to be reachable by scrolling.");
        Assert.False(ImagesEqual(firstPage, lastPage), "Expected scrolling to change the panel's content.");

        // Scrolling further than the max offset clamps instead of going blank.
        var overscrolled = TodayScreen.Render(Width, Height, Data(events), agendaScrollOffset: 1000);
        Assert.True(ImagesEqual(lastPage, overscrolled), "Expected overscroll to clamp at the same offset as the last page.");
    }

    private static bool ImagesEqual(ScreenRenderResult a, ScreenRenderResult b)
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (a.Image[x, y].PackedValue != b.Image[x, y].PackedValue)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool RowHasContent(ScreenRenderResult result, int rowIndex)
    {
        // Restricted to the top-left (Agenda) panel's own column so title/row text from the
        // sibling panels at the same y-band doesn't produce a false positive.
        int colWidth = (Width - 2 * Margin - PanelGap) / 2;
        int contentTop = PanelPad + TitleLineHeight + 10 + 16;
        int y = Margin + contentTop + rowIndex * TodayScreen.RowHeight;

        for (int py = y; py < y + TodayScreen.RowHeight - 10; py++)
        {
            for (int px = Margin; px < Margin + colWidth; px++)
            {
                if (result.Image[px, py].PackedValue < 255)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
