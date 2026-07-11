using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

public enum MealSlot
{
    Breakfast,
    Dinner
}

public sealed class Meal : SyncEntity
{
    [JsonIgnore]
    public override string EntityType => "Meal";

    public required DateOnly Date { get; set; }
    public required MealSlot Slot { get; set; }
    public string? RecipeId { get; set; }
    public string? Description { get; set; }
}
