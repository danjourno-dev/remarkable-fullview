using Fullview.Domain.Entities;

namespace Fullview.Api.Calendar;

/// <summary>Pure diff, no I/O: finds stored <see cref="AgendaEvent"/> rows for one calendar
/// that no longer have a live Google event behind them. Exists because per-event delta
/// tracking (the "cancelled" and "moved" branches in <see cref="GoogleCalendarPullService"/>)
/// both key off Google's own event id, which the Work mirror's wipe-and-rebuild churn can
/// change on every edit — when it does, neither branch ever fires and the stale row is
/// orphaned forever. This mark-and-sweep pass is the backstop: it doesn't care why a row's
/// Google id vanished, only that it did.</summary>
public static class CalendarReconciler
{
    public static IReadOnlyList<AgendaEvent> FindOrphans(
        IReadOnlyList<SyncEntity> allEntities,
        string calendarId,
        IReadOnlySet<string> liveExternalIds,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        var idPrefix = $"google-{calendarId}-";
        var orphans = new List<AgendaEvent>();

        foreach (var entity in allEntities)
        {
            if (entity is not AgendaEvent agendaEvent
                || agendaEvent.Deleted
                || agendaEvent.Source != AgendaEventSource.GoogleCalendar
                || !agendaEvent.Id.StartsWith(idPrefix, StringComparison.Ordinal)
                || agendaEvent.Start < windowStart
                || agendaEvent.Start > windowEnd)
            {
                continue;
            }

            if (agendaEvent.ExternalId is not null && liveExternalIds.Contains(agendaEvent.ExternalId))
            {
                continue;
            }

            orphans.Add(agendaEvent);
        }

        return orphans;
    }
}
