using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

public enum RoutineType
{
    MorningPersonal,
    EveningPersonal,
    WorkStartup,
    WorkShutdown
}

public sealed class Routine : SyncEntity
{
    [JsonIgnore]
    public override string EntityType => "Routine";

    public required string Name { get; set; }
    public required RoutineType Type { get; set; }

    /// <summary>Checklist item labels, in display order. The shutdown routine's last item
    /// is "choose tomorrow's 3" (Stage 8) but that's just a label — no special handling here.</summary>
    public List<string> Items { get; set; } = [];
}
