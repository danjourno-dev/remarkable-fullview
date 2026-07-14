using Fullview.Api.Calendar;
using Fullview.Domain;
using Fullview.Domain.Entities;

namespace Fullview.Api.Tests.Calendar;

public class CalendarReconcilerTests
{
    private static readonly DateTimeOffset WindowStart = DateTimeOffset.Parse("2026-07-13T00:00:00Z");
    private static readonly DateTimeOffset WindowEnd = DateTimeOffset.Parse("2026-07-22T00:00:00Z");

    private static AgendaEvent GoogleEvent(string id, string? externalId, DateTimeOffset start, bool deleted = false) =>
        new()
        {
            Id = id,
            Context = SyncContext.Work,
            UpdatedAt = start,
            UpdatedBy = "google-calendar-pull",
            Deleted = deleted,
            Title = "Test",
            Start = start,
            End = start.AddMinutes(30),
            Source = AgendaEventSource.GoogleCalendar,
            ExternalId = externalId,
            ReadOnly = true
        };

    [Fact]
    public void FindOrphans_RowWhoseGoogleIdIsNoLongerLive_IsReturned()
    {
        // Simulates the reported bug: the Work mirror churns the Google event id on every
        // edit, so a row saved under a since-superseded id never gets a "cancelled" delta.
        var stale = GoogleEvent("google-work-cal-testtoggle-1000-2000", "old-google-id", WindowStart.AddDays(1));
        var live = new HashSet<string> { "new-google-id" };

        var orphans = CalendarReconciler.FindOrphans([stale], "work-cal", live, WindowStart, WindowEnd);

        Assert.Single(orphans);
        Assert.Equal(stale.Id, orphans[0].Id);
    }

    [Fact]
    public void FindOrphans_RowWithLiveGoogleId_IsNotReturned()
    {
        var current = GoogleEvent("google-work-cal-testtoggle-3000-4000", "new-google-id", WindowStart.AddDays(1));
        var live = new HashSet<string> { "new-google-id" };

        var orphans = CalendarReconciler.FindOrphans([current], "work-cal", live, WindowStart, WindowEnd);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FindOrphans_AlreadyDeletedRow_IsNotReturned()
    {
        var tombstoned = GoogleEvent("google-work-cal-testtoggle-1000-2000", "old-google-id", WindowStart.AddDays(1), deleted: true);
        var live = new HashSet<string>();

        var orphans = CalendarReconciler.FindOrphans([tombstoned], "work-cal", live, WindowStart, WindowEnd);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FindOrphans_RowFromADifferentCalendar_IsNotReturned()
    {
        var otherCalendar = GoogleEvent("google-personal-cal-testtoggle-1000-2000", "old-google-id", WindowStart.AddDays(1));
        var live = new HashSet<string>();

        var orphans = CalendarReconciler.FindOrphans([otherCalendar], "work-cal", live, WindowStart, WindowEnd);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FindOrphans_RowStartingOutsideTheFetchWindow_IsNotReturned()
    {
        // A row Google's windowed full-fetch would never have surfaced either way, so its
        // absence from liveExternalIds says nothing about whether it's actually gone.
        var farFuture = GoogleEvent("google-work-cal-testtoggle-9000-9999", "old-google-id", WindowEnd.AddDays(30));
        var live = new HashSet<string>();

        var orphans = CalendarReconciler.FindOrphans([farFuture], "work-cal", live, WindowStart, WindowEnd);

        Assert.Empty(orphans);
    }

    [Fact]
    public void FindOrphans_NativeEvent_IsNotReturned()
    {
        var native = new AgendaEvent
        {
            Id = "some-ulid",
            Context = SyncContext.Work,
            UpdatedAt = WindowStart,
            UpdatedBy = "device-1",
            Title = "Not from Google",
            Start = WindowStart.AddDays(1),
            End = WindowStart.AddDays(1).AddMinutes(30),
            Source = AgendaEventSource.Native
        };
        var live = new HashSet<string>();

        var orphans = CalendarReconciler.FindOrphans([native], "work-cal", live, WindowStart, WindowEnd);

        Assert.Empty(orphans);
    }
}
