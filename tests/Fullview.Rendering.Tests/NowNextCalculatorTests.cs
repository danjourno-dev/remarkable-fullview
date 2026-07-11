using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;

namespace Fullview.Rendering.Tests;

public class NowNextCalculatorTests
{
    private static AgendaEvent Event(string title, SyncContext context, DateTimeOffset start, DateTimeOffset end, bool allDay = false) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            Context = context,
            UpdatedAt = start,
            UpdatedBy = "test",
            Title = title,
            Start = start,
            End = end,
            IsAllDay = allDay
        };

    [Fact]
    public void Compute_PicksCurrentEventAcrossBothContexts()
    {
        var now = new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("Work standup", SyncContext.Work, now.AddMinutes(-30), now.AddMinutes(30)),
            Event("Pick up Talia", SyncContext.Personal, now.AddHours(2), now.AddHours(3)),
        };

        var strip = NowNextCalculator.Compute(events, now);

        Assert.NotNull(strip.Now);
        Assert.Equal("Work standup", strip.Now!.Label);
        Assert.Equal(SyncContext.Work, strip.Now.Context);
        Assert.NotNull(strip.Next);
        Assert.Equal("Pick up Talia", strip.Next!.Label);
        Assert.Equal("2H 0M", strip.TimeUntilNext);
    }

    [Fact]
    public void Compute_IgnoresAllDayEvents()
    {
        var now = new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("Bank holiday", SyncContext.Personal, now.AddHours(-1), now.AddHours(23), allDay: true),
        };

        var strip = NowNextCalculator.Compute(events, now);

        Assert.Null(strip.Now);
        Assert.Null(strip.Next);
    }

    [Fact]
    public void Compute_NoNextEvent_LeavesTimeUntilNextNull()
    {
        var now = new DateTimeOffset(2026, 7, 9, 10, 0, 0, TimeSpan.Zero);

        var strip = NowNextCalculator.Compute(Array.Empty<AgendaEvent>(), now);

        Assert.Null(strip.Now);
        Assert.Null(strip.Next);
        Assert.Null(strip.TimeUntilNext);
    }
}
