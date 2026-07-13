using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fullview.Domain.Entities;

/// <summary>
/// System.Text.Json's built-in polymorphic reader (<see cref="JsonPolymorphicAttribute"/>)
/// only recognizes the `entityType` discriminator when it's the *first* property in the
/// JSON object — anything else falls back to constructing the abstract <see cref="SyncEntity"/>
/// base type and throws <see cref="NotSupportedException"/>. JSON objects have no defined
/// property order, so that's not something callers (the web app's object literals, the
/// device's serializer, any future client) can be relied on to preserve. This converter
/// buffers the object, reads the discriminator wherever it appears, then dispatches to the
/// concrete type — order-independent by construction.
/// </summary>
public sealed class SyncEntityJsonConverter : JsonConverter<SyncEntity>
{
    private static readonly IReadOnlyDictionary<string, Type> TypesByDiscriminator = new Dictionary<string, Type>
    {
        ["Todo"] = typeof(Todo),
        ["AgendaEvent"] = typeof(AgendaEvent),
        ["Meal"] = typeof(Meal),
        ["ShoppingItem"] = typeof(ShoppingItem),
        ["Recipe"] = typeof(Recipe),
        ["Routine"] = typeof(Routine),
        ["RoutineCheck"] = typeof(RoutineCheck),
        ["InboxPage"] = typeof(InboxPage)
    };

    public override SyncEntity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        JsonElement? discriminator = null;
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, "entityType", StringComparison.OrdinalIgnoreCase))
            {
                discriminator = property.Value;
                break;
            }
        }

        if (discriminator is not { ValueKind: JsonValueKind.String } || discriminator.Value.GetString() is not { } discriminatorValue
            || !TypesByDiscriminator.TryGetValue(discriminatorValue, out var type))
        {
            throw new JsonException("Entity payload is missing a recognized 'entityType' discriminator.");
        }

        return (SyncEntity)(JsonSerializer.Deserialize(root.GetRawText(), type, options)
            ?? throw new JsonException("Entity payload deserialized to null."));
    }

    public override void Write(Utf8JsonWriter writer, SyncEntity value, JsonSerializerOptions options)
    {
        using var document = JsonSerializer.SerializeToDocument(value, value.GetType(), options);

        writer.WriteStartObject();
        writer.WriteString("entityType", value.EntityType);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            property.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}
