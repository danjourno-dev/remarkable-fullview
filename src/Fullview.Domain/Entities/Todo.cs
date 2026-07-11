using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

public enum TodoPriority
{
    Focus,
    Normal,
    Someday
}

public enum TodoEnergy
{
    QuickWin,
    Deep
}

public sealed class Todo : SyncEntity
{
    [JsonIgnore]
    public override string EntityType => "Todo";

    public required string Title { get; set; }
    public TodoPriority Priority { get; set; } = TodoPriority.Normal;
    public DateOnly? DueDate { get; set; }
    public TodoEnergy? Energy { get; set; }
    public bool Completed { get; set; }
}
