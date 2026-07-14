using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Fullview.Domain.Entities;

namespace Fullview.Api.Sync;

/// <summary>Single-table store (B5): `pk`/`sk` for point lookups and for the full-list read
/// (a plain partition-key equality query — the whole user's data lives in one partition, so
/// no secondary index is needed). Entities are stored whole as a `data` JSON blob (via
/// <see cref="SyncEntity"/>'s polymorphic serialization) alongside a handful of queryable
/// scalar attributes — this avoids hand-mapping eight different entity shapes onto
/// DynamoDB attributes individually.</summary>
public sealed class DynamoSyncStore : ISyncStore
{
    private readonly Table _table;

    public DynamoSyncStore(IAmazonDynamoDB client, string tableName)
    {
        _table = Table.LoadTable(client, tableName);
    }

    public async Task<SyncEntity?> GetAsync(string sortKey, CancellationToken ct)
    {
        var document = await _table.GetItemAsync(UserPartition.Pk, sortKey, ct);
        return document is null ? null : Deserialize(document);
    }

    public async Task PutAsync(SyncEntity entity, CancellationToken ct)
    {
        var updatedAt = entity.UpdatedAt.ToUniversalTime().ToString("O");

        var document = new Document
        {
            ["pk"] = UserPartition.Pk,
            ["sk"] = entity.SortKey,
            ["entityType"] = entity.EntityType,
            ["context"] = entity.Context.ToString(),
            ["updatedAt"] = updatedAt,
            ["deleted"] = entity.Deleted,
            ["data"] = JsonSerializer.Serialize<SyncEntity>(entity, SyncJson.Options)
        };

        // A completed reminder self-destructs 24h after it was completed: stamp the native
        // DynamoDB TTL attribute (epoch seconds) so the service reaper hard-deletes the row.
        // We only set `ttl` while Completed — PutItem replaces the whole item, so un-completing
        // a Todo (a fresh PutAsync with Completed=false) drops the attribute and cancels expiry.
        if (CompletionTtlEpochSeconds(entity) is { } ttl)
        {
            document["ttl"] = ttl;
        }

        await _table.PutItemAsync(document, ct);
    }

    public async Task<IReadOnlyList<SyncEntity>> GetAllAsync(CancellationToken ct)
    {
        var config = new QueryOperationConfig
        {
            KeyExpression = new Expression
            {
                ExpressionStatement = "pk = :pk",
                ExpressionAttributeValues = { [":pk"] = UserPartition.Pk }
            }
        };
        var search = _table.Query(config);
        var items = new List<SyncEntity>();

        do
        {
            var page = await search.GetNextSetAsync(ct);
            items.AddRange(page.Select(Deserialize));
        } while (!search.IsDone);

        return items;
    }

    /// <summary>Native-TTL expiry stamp (Unix epoch seconds) for an entity, or null if it
    /// should never auto-expire. A completed <see cref="Todo"/> expires 24h after completion;
    /// <see cref="SyncEntity.UpdatedAt"/> is the LWW clock, so on a Completed=true write it is
    /// the completion time.</summary>
    public static long? CompletionTtlEpochSeconds(SyncEntity entity) =>
        entity is Todo { Completed: true }
            ? entity.UpdatedAt.ToUniversalTime().AddHours(24).ToUnixTimeSeconds()
            : null;

    private static SyncEntity Deserialize(Document document)
    {
        var json = document["data"].AsString();
        return JsonSerializer.Deserialize<SyncEntity>(json, SyncJson.Options)
            ?? throw new InvalidOperationException($"Stored entity data for sk={document["sk"].AsString()} did not deserialize.");
    }
}
