using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

public sealed class Recipe : SyncEntity
{
    [JsonIgnore]
    public override string EntityType => "Recipe";

    public required string Title { get; set; }
    public List<string> Ingredients { get; set; } = [];
    public List<string> Steps { get; set; } = [];
}
