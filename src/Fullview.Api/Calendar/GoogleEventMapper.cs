using Fullview.Domain;
using Fullview.Domain.Entities;
using Google.Apis.Calendar.v3.Data;

namespace Fullview.Api.Calendar;

/// <summary>Maps a Google Calendar API event onto the domain's <see cref="AgendaEvent"/>
/// shape. Pure/no I/O so it's unit-testable without a live Google API call.</summary>
public static class GoogleEventMapper
{
    /// <summary>Deterministic entity id so re-pulling the same Google event always upserts
    /// the same row instead of creating a duplicate. Keyed on the event's own content
    /// (title/start/end) rather than any Google- or Outlook-minted identifier: the Work
    /// mirror's wipe-and-rebuild churn (Stage 6.6) deletes and recreates every event on
    /// that calendar every 30 minutes, and neither Google's <c>id</c> nor Outlook's
    /// <c>iCalUID</c> survive that cycle stably — content is the only thing that does.
    /// Ticks (not a formatted timestamp) sidestep any need to sanitize the time component;
    /// <see cref="NormalizeTitle"/> strips the title down to characters safe for a DynamoDB
    /// sort key.</summary>
    public static string BuildEntityId(string calendarId, string title, DateTimeOffset start, DateTimeOffset end) =>
        $"google-{calendarId}-{NormalizeTitle(title)}-{start.UtcTicks}-{end.UtcTicks}";

    private static string NormalizeTitle(string title)
    {
        var normalized = new string(title.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return normalized.Length == 0 ? "untitled" : normalized;
    }

    /// <summary>Null return means "tombstone" — either Google reports the event cancelled,
    /// or it's missing the fields needed to render (defensive; shouldn't happen for
    /// singleEvents=true results).</summary>
    /// <param name="knownEntityId">For a cancellation notice, the content-derived id this
    /// Google event id last resolved to (looked up by the caller from
    /// <see cref="CalendarEventIndexStore"/>). Cancellation notices carry only Google's own
    /// (churning) id, never title/start/end, so this is the only way to tombstone the row
    /// the live event actually lives under. Null if we've never seen this Google id before,
    /// in which case there's nothing to tombstone.</param>
    public static AgendaEvent? Map(Event googleEvent, string calendarId, SyncContext context, DateTimeOffset now, string? knownEntityId = null)
    {
        if (googleEvent.Status == "cancelled")
        {
            if (knownEntityId is null)
            {
                return null;
            }

            return new AgendaEvent
            {
                Id = knownEntityId,
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

        var title = googleEvent.Summary ?? "(no title)";

        return new AgendaEvent
        {
            Id = BuildEntityId(calendarId, title, start.Value, end.Value),
            Context = context,
            UpdatedAt = now,
            UpdatedBy = "google-calendar-pull",
            Title = title,
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
