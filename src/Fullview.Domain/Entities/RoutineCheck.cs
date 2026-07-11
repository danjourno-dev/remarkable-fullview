using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

/// <summary>One day's tick state for one item of a <see cref="Routine"/>'s checklist.
/// Checklists reset daily (Stage 8), so a check is keyed by (RoutineId, Date, ItemIndex)
/// rather than living on the Routine itself.</summary>
public sealed class RoutineCheck : SyncEntity
{
    [JsonIgnore]
    public override string EntityType => "RoutineCheck";

    public required string RoutineId { get; set; }
    public required DateOnly Date { get; set; }
    public required int ItemIndex { get; set; }
    public bool Checked { get; set; }
}
