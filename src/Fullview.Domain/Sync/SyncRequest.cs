using Fullview.Domain.Entities;

namespace Fullview.Domain.Sync;

/// <summary>Request body for the single `/sync` endpoint used by both device and web
/// (B5). Same shape for every client — there is no device-specific or web-specific
/// variant.</summary>
public sealed class SyncRequest
{
    public required string DeviceId { get; set; }

    /// <summary>Last cursor this client saw, or null/empty for a first-ever sync.</summary>
    public string? Cursor { get; set; }

    /// <summary>Mutations queued locally since the last successful sync. ULID-keyed and
    /// idempotent — replaying an entry here more than once is safe.</summary>
    public List<SyncEntity> Outbox { get; set; } = [];
}
