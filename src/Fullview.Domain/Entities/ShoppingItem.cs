using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

public sealed class ShoppingItem : SyncEntity
{
    [JsonIgnore]
    public override string EntityType => "ShoppingItem";

    public required string Name { get; set; }
    public string? Category { get; set; }
    public bool Checked { get; set; }
}
