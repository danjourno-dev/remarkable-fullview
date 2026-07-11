using System.Text.Json;
using Fullview.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Fullview.Device.Storage;

/// <summary>
/// Local CRUD over the SQLite `entities` table, mirroring DynamoSyncStore's shape (whole
/// entity as a JSON blob + queryable columns) so the same polymorphic JSON round-trips both
/// places. Every real mutation (<see cref="Save"/>) also queues an outbox row in the same
/// transaction (B5: "outbox in same transaction as every local write") — Stage 5 drains it.
/// </summary>
public sealed class DeviceStore
{
    private readonly DeviceDatabase _database;

    public DeviceStore(DeviceDatabase database)
    {
        _database = database;
    }

    public IReadOnlyList<T> Query<T>() where T : SyncEntity
    {
        string entityType = typeof(T).Name;
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT data FROM entities WHERE entity_type = $type;";
        command.Parameters.AddWithValue("$type", entityType);

        var results = new List<T>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var entity = JsonSerializer.Deserialize<T>(reader.GetString(0), DeviceJson.Options);
            if (entity is not null)
            {
                results.Add(entity);
            }
        }

        return results;
    }

    /// <summary>Persists a real local mutation: writes the entity row and queues an outbox
    /// entry for Stage 5's sync drain, in one transaction.</summary>
    public void Save(SyncEntity entity)
    {
        string json = JsonSerializer.Serialize(entity, DeviceJson.Options);

        using var transaction = _database.Connection.BeginTransaction();
        UpsertEntityRow(transaction, entity, json);

        using (var outboxCommand = _database.Connection.CreateCommand())
        {
            outboxCommand.Transaction = transaction;
            outboxCommand.CommandText = """
                INSERT INTO outbox (entity_id, entity_type, payload, created_at)
                VALUES ($id, $type, $payload, $createdAt);
                """;
            outboxCommand.Parameters.AddWithValue("$id", entity.Id);
            outboxCommand.Parameters.AddWithValue("$type", entity.EntityType);
            outboxCommand.Parameters.AddWithValue("$payload", json);
            outboxCommand.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            outboxCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Bootstraps fabricated demo data directly into the entities table, bypassing
    /// the outbox — seed rows aren't real mutations to sync, they're a starting point for
    /// living with the board offline (Checkpoint 4.1).</summary>
    public void SaveSeed(SyncEntity entity)
    {
        string json = JsonSerializer.Serialize(entity, DeviceJson.Options);
        using var transaction = _database.Connection.BeginTransaction();
        UpsertEntityRow(transaction, entity, json);
        transaction.Commit();
    }

    public void ToggleTodoCompleted(string todoId, string deviceId)
    {
        var todo = Query<Todo>().FirstOrDefault(t => t.Id == todoId);
        if (todo is null)
        {
            return;
        }

        todo.Completed = !todo.Completed;
        todo.UpdatedAt = DateTimeOffset.UtcNow;
        todo.UpdatedBy = deviceId;
        Save(todo);
    }

    public void ToggleShoppingItemChecked(string itemId, string deviceId)
    {
        var item = Query<ShoppingItem>().FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return;
        }

        item.Checked = !item.Checked;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.UpdatedBy = deviceId;
        Save(item);
    }

    private void UpsertEntityRow(SqliteTransaction transaction, SyncEntity entity, string json)
    {
        using var command = _database.Connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO entities (id, entity_type, context, updated_at, deleted, data)
            VALUES ($id, $type, $context, $updatedAt, $deleted, $data)
            ON CONFLICT(entity_type, id) DO UPDATE SET
                context = excluded.context,
                updated_at = excluded.updated_at,
                deleted = excluded.deleted,
                data = excluded.data;
            """;
        command.Parameters.AddWithValue("$id", entity.Id);
        command.Parameters.AddWithValue("$type", entity.EntityType);
        command.Parameters.AddWithValue("$context", entity.Context.ToString());
        command.Parameters.AddWithValue("$updatedAt", entity.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deleted", entity.Deleted ? 1 : 0);
        command.Parameters.AddWithValue("$data", json);
        command.ExecuteNonQuery();
    }
}
