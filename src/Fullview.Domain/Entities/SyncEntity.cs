using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

/// <summary>
/// Common sync metadata carried by every row (B5): PK is the single-user constant
/// <see cref="UserPartition.Pk"/>, SK is <see cref="SortKey"/>. <see cref="UpdatedAt"/> is
/// the last-write-wins clock and the delta-query cursor value; <see cref="Deleted"/> is a
/// tombstone, not a hard delete, so peers can converge on deletions too.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "entityType")]
[JsonDerivedType(typeof(Todo), "Todo")]
[JsonDerivedType(typeof(AgendaEvent), "AgendaEvent")]
[JsonDerivedType(typeof(Meal), "Meal")]
[JsonDerivedType(typeof(ShoppingItem), "ShoppingItem")]
[JsonDerivedType(typeof(Recipe), "Recipe")]
[JsonDerivedType(typeof(Routine), "Routine")]
[JsonDerivedType(typeof(RoutineCheck), "RoutineCheck")]
[JsonDerivedType(typeof(InboxPage), "InboxPage")]
public abstract class SyncEntity
{
    /// <summary>ULID minted by whichever client created the entity.</summary>
    public required string Id { get; set; }

    public required SyncContext Context { get; set; }

    /// <summary>LWW clock. Client-set (device/web local time), never server-generated,
    /// so replaying the same mutation twice is naturally idempotent.</summary>
    public required DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Device id, or "web", of whoever produced this version.</summary>
    public required string UpdatedBy { get; set; }

    public bool Deleted { get; set; }

    [JsonIgnore]
    public abstract string EntityType { get; }

    [JsonIgnore]
    public string SortKey => $"{EntityType}#{Id}";
}

/// <summary>Single-user v1 partition key (B5 — auth is a single API key, Cognito is v2).</summary>
public static class UserPartition
{
    public const string Pk = "USER#dan";
}
