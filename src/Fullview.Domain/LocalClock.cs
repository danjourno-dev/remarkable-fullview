namespace Fullview.Domain;

/// <summary>
/// Converts UTC instants to the configured wall-clock timezone. The reMarkable 1's OS clock is
/// NTP-synced (the instant is correct) but ships with no local timezone configured —
/// <c>/etc/localtime</c> points at Universal, so <see cref="TimeZoneInfo.Local"/> is UTC and every
/// <c>.ToLocalTime()</c>/<c>.LocalDateTime</c> call silently returns UTC instead of wall-clock
/// time. Use this instead of those anywhere "today" or a displayed clock time matters.
///
/// Reads the IANA id from <c>FULLVIEW_TIMEZONE</c> (set in <c>/etc/fullview.env</c>, see
/// docs/device-setup.md) so users outside the UK can override it; defaults to
/// <c>Europe/London</c>. Resolved once per process — <c>/etc/fullview.env</c> is sourced at
/// process start, not re-read live.
/// </summary>
public static class LocalClock
{
    private const string DefaultTimeZoneId = "Europe/London";

    private static readonly Lazy<TimeZoneInfo> TimeZone = new(ResolveTimeZone);

    public static DateTimeOffset ToLocal(this DateTimeOffset utc) =>
        TimeZoneInfo.ConvertTime(utc, TimeZone.Value);

    private static TimeZoneInfo ResolveTimeZone()
    {
        var configuredId = Environment.GetEnvironmentVariable("FULLVIEW_TIMEZONE");

        if (!string.IsNullOrWhiteSpace(configuredId) && TryFind(configuredId, out var configured))
        {
            return configured;
        }

        if (TryFind(DefaultTimeZoneId, out var london))
        {
            return london;
        }

        return UkDstFallback.Instance;
    }

    private static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = null!;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = null!;
            return false;
        }
    }
}

/// <summary>
/// Hand-rolled UK DST rule (BST = last Sunday of March 01:00 UTC to last Sunday of October 02:00
/// UTC) for the rare case tzdata isn't present at all — last-resort fallback used only when
/// neither the configured <c>FULLVIEW_TIMEZONE</c> id nor the Europe/London default resolve.
/// </summary>
file static class UkDstFallback
{
    public static readonly TimeZoneInfo Instance = TimeZoneInfo.CreateCustomTimeZone(
        "UK-DST-Fallback",
        TimeSpan.Zero,
        "UK (fallback DST rule)",
        "GMT",
        "BST",
        new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 1, 0, 0), 3, 5, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 10, 5, DayOfWeek.Sunday))
        });
}
