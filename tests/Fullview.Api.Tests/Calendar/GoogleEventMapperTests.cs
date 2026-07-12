using Fullview.Api.Calendar;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Google.Apis.Calendar.v3.Data;

namespace Fullview.Api.Tests.Calendar;

public class GoogleEventMapperTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-12T08:00:00Z");

    [Fact]
    public void Map_TimedEvent_ProducesReadOnlyGoogleCalendarAgendaEvent()
    {
        var googleEvent = new Event
        {
            Id = "evt-1",
            ETag = "etag-1",
            Summary = "Standup",
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-07-12T09:00:00+01:00") },
            End = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-07-12T09:15:00+01:00") }
        };

        var mapped = GoogleEventMapper.Map(googleEvent, "work-cal", SyncContext.Work, Now);

        Assert.NotNull(mapped);
        Assert.Equal("Standup", mapped!.Title);
        Assert.False(mapped.IsAllDay);
        Assert.Equal(AgendaEventSource.GoogleCalendar, mapped.Source);
        Assert.True(mapped.ReadOnly);
        Assert.Equal(SyncContext.Work, mapped.Context);
        Assert.Equal("evt-1", mapped.ExternalId);
        Assert.Equal(GoogleEventMapper.BuildEntityId("work-cal", "evt-1"), mapped.Id);
    }

    [Fact]
    public void Map_AllDayEvent_UsesDateFieldAndFlagsIsAllDay()
    {
        var googleEvent = new Event
        {
            Id = "evt-2",
            Summary = "Bank holiday",
            Start = new EventDateTime { Date = "2026-08-31" },
            End = new EventDateTime { Date = "2026-09-01" }
        };

        var mapped = GoogleEventMapper.Map(googleEvent, "personal-cal", SyncContext.Personal, Now);

        Assert.NotNull(mapped);
        Assert.True(mapped!.IsAllDay);
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero), mapped.Start);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), mapped.End);
    }

    [Fact]
    public void Map_CancelledEvent_ReturnsTombstone()
    {
        var googleEvent = new Event { Id = "evt-3", Status = "cancelled" };

        var mapped = GoogleEventMapper.Map(googleEvent, "work-cal", SyncContext.Work, Now);

        Assert.NotNull(mapped);
        Assert.True(mapped!.Deleted);
        Assert.Equal(GoogleEventMapper.BuildEntityId("work-cal", "evt-3"), mapped.Id);
    }

    [Fact]
    public void Map_SameGoogleEvent_AlwaysProducesSameEntityId()
    {
        var first = GoogleEventMapper.BuildEntityId("cal-a", "evt-x");
        var second = GoogleEventMapper.BuildEntityId("cal-a", "evt-x");

        Assert.Equal(first, second);
    }

    [Fact]
    public void Map_DifferentCalendars_ProduceDifferentEntityIds()
    {
        var forWork = GoogleEventMapper.BuildEntityId("work-cal", "evt-x");
        var forPersonal = GoogleEventMapper.BuildEntityId("personal-cal", "evt-x");

        Assert.NotEqual(forWork, forPersonal);
    }

    [Fact]
    public void Map_SameICalUidDifferentGoogleId_ProducesSameEntityId()
    {
        // Simulates the Work mirror's wipe-and-rebuild: Outlook keeps the iCalUID stable
        // but Google mints a new internal id on every recreate.
        var beforeRebuild = new Event
        {
            Id = "abc123",
            ICalUID = "outlook-uid-1@outlook.com",
            Summary = "Stand-up",
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-07-13T09:00:00+01:00") },
            End = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-07-13T09:15:00+01:00") }
        };
        var afterRebuild = new Event
        {
            Id = "xyz789",
            ICalUID = "outlook-uid-1@outlook.com",
            Summary = "Stand-up",
            Start = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-07-13T09:00:00+01:00") },
            End = new EventDateTime { DateTimeDateTimeOffset = DateTimeOffset.Parse("2026-07-13T09:15:00+01:00") }
        };

        var mappedBefore = GoogleEventMapper.Map(beforeRebuild, "work-cal", SyncContext.Work, Now);
        var mappedAfter = GoogleEventMapper.Map(afterRebuild, "work-cal", SyncContext.Work, Now);

        Assert.Equal(mappedBefore!.Id, mappedAfter!.Id);
    }
}
