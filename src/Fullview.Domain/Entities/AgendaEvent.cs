namespace Fullview.Domain.Entities;

/// <summary>Where an agenda event came from (B5). <see cref="GoogleCalendar"/> events are
/// pulled read-only mirrors (Stage 6.5) and are exempt from LWW — the puller always wins.
/// Stage 2 only models the shape; the puller itself lands in Stage 6.5.</summary>
public enum AgendaEventSource
{
    Native,
    GoogleCalendar
}

public sealed class AgendaEvent : SyncEntity
{
    public override string EntityType => "AgendaEvent";

    public required string Title { get; set; }
    public required DateTimeOffset Start { get; set; }
    public required DateTimeOffset End { get; set; }
    public bool IsAllDay { get; set; }

    public AgendaEventSource Source { get; set; } = AgendaEventSource.Native;

    /// <summary>Google's event id — idempotency key for pulled events. Null for Source=Native.</summary>
    public string? ExternalId { get; set; }
    public string? ExternalEtag { get; set; }

    /// <summary>True for pulled events: a mirror, not a master, so the device UI never
    /// offers an edit affordance for it.</summary>
    public bool ReadOnly { get; set; }
}
