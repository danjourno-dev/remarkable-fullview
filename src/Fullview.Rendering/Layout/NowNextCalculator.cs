using Fullview.Domain.Entities;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Pure logic behind the Now/Next strip (B3): merges both contexts' agendas and picks the
/// current and next timed commitment, regardless of which mode the board is in. All-day
/// events never occupy Now/Next — they're shown as a band on the Agenda/Today screens instead.
/// </summary>
public static class NowNextCalculator
{
    public static StripData Compute(IReadOnlyList<AgendaEvent> events, DateTimeOffset now)
    {
        var timed = events.Where(e => !e.IsAllDay && !e.Deleted).ToList();

        var current = timed
            .Where(e => e.Start <= now && now < e.End)
            .OrderBy(e => e.Start)
            .FirstOrDefault();

        var next = timed
            .Where(e => e.Start > now)
            .OrderBy(e => e.Start)
            .FirstOrDefault();

        var nowEntry = current is null ? null : new StripEntry(current.Title, current.Context);
        var nextEntry = next is null ? null : new StripEntry(next.Title, next.Context);
        string? timeUntil = next is null ? null : FormatTimeUntil(next.Start - now);

        return new StripData(nowEntry, nextEntry, timeUntil);
    }

    private static string FormatTimeUntil(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        int totalMinutes = (int)Math.Round(span.TotalMinutes);
        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;
        return hours > 0 ? $"{hours}H {minutes}M" : $"{minutes}M";
    }
}
