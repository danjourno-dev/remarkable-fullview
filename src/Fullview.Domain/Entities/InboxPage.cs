namespace Fullview.Domain.Entities;

public enum InboxPageState
{
    Queued,
    Processed,
    Filed
}

public sealed class InboxPage : SyncEntity
{
    public override string EntityType => "InboxPage";

    public InboxPageState State { get; set; } = InboxPageState.Queued;
    public string? S3Key { get; set; }
    public string? Notes { get; set; }
}
