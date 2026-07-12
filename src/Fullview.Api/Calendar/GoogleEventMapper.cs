using Fullview.Domain;
using Fullview.Domain.Entities;
using Google.Apis.Calendar.v3.Data;

namespace Fullview.Api.Calendar;

/// <summary>Maps a Google Calendar API event onto the domain's <see cref="AgendaEvent"/>
/// shape. Pure/no I/O so it's unit-testable without a live Google API call.</summary>
public static class GoogleEventMapper
{
    /// <summary>Deterministic entity id so re-pulling the same Google event always upserts
    /// the same row instead of creating a duplicate. Keyed on <c>iCalUID</c> rather than
    /// Google's own <c>id</c>: the Work mirror's wipe-and-rebuild churn (Stage 6.6) deletes
    /// and recreates every event on that calendar every 30 minutes, minting a fresh
    /// Google-internal <c>id</c> each time. <c>iCalUID</c> is the RFC5545 identifier Outlook
    /// assigns and Google preserves verbatim across that recreate cycle, so it's the only
    /// field stable enough to dedupe on for that calendar.</summary>
    public static string BuildEntityId(string calendarId, string iCalUid) =>
        $"google-{calendarId}-{iCalUid}";

    /// <summary>Null return means "tombstone" — either Google reports the event cancelled,
    /// or it's missing the fields needed to render (defensive; shouldn't happen for
    /// singleEvents=true results).</summary>
    public static AgendaEvent? Map(Event googleEvent, string calendarId, SyncContext context, DateTimeOffset now)
    {
        // Cancellation notifications sometimes omit iCalUID, so fall back to id — losing
        // dedupe on that rare path is harmless since a tombstone has no future rows to merge with.
        var id = BuildEntityId(calendarId, googleEvent.ICalUID ?? googleEvent.Id);

        if (googleEvent.Status == "cancelled")
        {
            return new AgendaEvent
            {
                Id = id,
                Context = context,
                UpdatedAt = now,
                UpdatedBy = "google-calendar-pull",
                Deleted = true,
                Title = string.Empty,
                Start = now,
                End = now,
                Source = AgendaEventSource.GoogleCalendar,
                ExternalId = googleEvent.Id,
                ReadOnly = true
            };
        }

        var (start, end, isAllDay) = ResolveTimes(googleEvent);
        if (start is null || end is null)
        {
            return null;
        }

        return new AgendaEvent
        {
            Id = id,
            Context = context,
            UpdatedAt = now,
            UpdatedBy = "google-calendar-pull",
            Title = googleEvent.Summary ?? "(no title)",
            Start = start.Value,
            End = end.Value,
            IsAllDay = isAllDay,
            Source = AgendaEventSource.GoogleCalendar,
            ExternalId = googleEvent.Id,
            ExternalEtag = googleEvent.ETag,
            ReadOnly = true
        };
    }

    // All-day events carry Date (no time component) instead of DateTime — the two shapes
    // are mutually exclusive on Google's EventDateTime.
    private static (DateTimeOffset? Start, DateTimeOffset? End, bool IsAllDay) ResolveTimes(Event googleEvent)
    {
        var start = googleEvent.Start;
        var end = googleEvent.End;
        if (start is null || end is null)
        {
            return (null, null, false);
        }

        if (start.DateTimeDateTimeOffset is not null && end.DateTimeDateTimeOffset is not null)
        {
            return (start.DateTimeDateTimeOffset.Value, end.DateTimeDateTimeOffset.Value, false);
        }

        if (start.Date is not null && end.Date is not null
            && DateOnly.TryParse(start.Date, out var startDate)
            && DateOnly.TryParse(end.Date, out var endDate))
        {
            return (
                new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                true);
        }

        return (null, null, false);
    }
}
