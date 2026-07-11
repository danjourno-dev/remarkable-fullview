using Fullview.Domain.Entities;

namespace Fullview.Domain.Sync;

public sealed class SyncResponse
{
    /// <summary>New cursor to send on the client's next `/sync` call.</summary>
    public required string Cursor { get; set; }

    /// <summary>Everything that changed since the request's cursor, including tombstones
    /// (Deleted=true) so peers converge on deletions too.</summary>
    public List<SyncEntity> Delta { get; set; } = [];
}
