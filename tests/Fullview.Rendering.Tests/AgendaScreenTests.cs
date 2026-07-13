using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using Fullview.Rendering.Screens;

namespace Fullview.Rendering.Tests;

public class AgendaScreenTests
{
    private static AgendaEvent Event(string title, DateTimeOffset start, DateTimeOffset end, bool allDay = false) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Context = SyncContext.Work,
            UpdatedAt = start,
            UpdatedBy = "test",
            Title = title,
            Start = start,
            End = end,
            IsAllDay = allDay
        };

    [Fact]
    public void Render_PastEvent_IsLighterThanUpcomingEvent()
    {
        var now = new DateTimeOffset(2026, 7, 9, 11, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("Past standup", now.AddHours(-2), now.AddHours(-1)),
            Event("Upcoming review", now.AddHours(1), now.AddHours(2)),
        };

        var result = AgendaScreen.Render(1404, 1872, events, now);

        byte DarkestPixelInRow(int rowIndex)
        {
            const int margin = 24;
            const int rowHeight = 105;
            int y = margin + 32 + margin + rowIndex * rowHeight;

            byte darkest = 255;
            for (int py = y; py < y + rowHeight - 10; py++)
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

            return darkest;
        }

        byte pastDarkest = DarkestPixelInRow(0);
        byte upcomingDarkest = DarkestPixelInRow(1);

        Assert.Equal(0, upcomingDarkest);
        Assert.True(pastDarkest > upcomingDarkest, $"Expected past event pixels ({pastDarkest}) to be lighter than upcoming event pixels ({upcomingDarkest}).");
    }

    [Fact]
    public void Render_AllDayPastEvent_StaysFullyDark()
    {
        var now = new DateTimeOffset(2026, 7, 9, 23, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("Company holiday", now.Date, now.Date.AddDays(1), allDay: true),
        };

        var result = AgendaScreen.Render(1404, 1872, events, now);

        const int margin = 24;
        const int rowHeight = 105;
        int y = margin + 32 + margin;

        byte darkest = 255;
        for (int py = y; py < y + rowHeight; py++)
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

        Assert.Equal(0, darkest);
    }

    [Fact]
    public void Render_MoreThanAPageOfEvents_ShowsOnePageAndScrollingReachesTheRest()
    {
        var now = new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 10)
            .Select(i => Event($"Event {i}", now.AddHours(i), now.AddHours(i + 1)))
            .ToArray();

        var firstPage = AgendaScreen.Render(1404, 1872, events, now);
        Assert.True(RowHasContent(firstPage, 6), "Expected all 7 rows of the first page to render.");
        Assert.False(RowHasContent(firstPage, 7), "Expected no leftover '+N more' row.");

        int maxOffset = ListPage.MaxScrollOffset(events.Length);
        Assert.Equal(3, maxOffset);

        var lastPage = AgendaScreen.Render(1404, 1872, events, now, maxOffset);
        Assert.True(RowHasContent(lastPage, 6), "Expected the 10th event to be reachable by scrolling.");
        Assert.False(RowHasContent(lastPage, 7));

        // Scrolling further than the max offset clamps instead of going blank.
        var overscrolled = AgendaScreen.Render(1404, 1872, events, now, scrollOffset: 1000);
        Assert.True(RowHasContent(overscrolled, 6));
    }

    private static bool RowHasContent(ScreenRenderResult result, int rowIndex)
    {
        const int margin = 24;
        const int rowHeight = 105;
        int y = margin + 32 + margin + rowIndex * rowHeight;

        for (int py = y; py < y + rowHeight - 10; py++)
        {
            for (int px = margin; px < 1404 - margin; px++)
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
