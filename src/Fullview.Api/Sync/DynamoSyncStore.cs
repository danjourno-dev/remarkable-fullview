using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Fullview.Domain.Entities;

namespace Fullview.Api.Sync;

/// <summary>Single-table store (B5): `pk`/`sk` for point lookups, `gsi1pk`/`gsi1sk` for the
/// delta query ordered by UpdatedAt. Entities are stored whole as a `data` JSON blob (via
/// <see cref="SyncEntity"/>'s polymorphic serialization) alongside a handful of queryable
/// scalar attributes — this avoids hand-mapping eight different entity shapes onto
/// DynamoDB attributes individually.</summary>
public sealed class DynamoSyncStore : ISyncStore
{
    private const string Gsi1Name = "gsi1";
    private static readonly string MinCursor = DateTimeOffset.MinValue.ToString("O");

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
            ["data"] = JsonSerializer.Serialize<SyncEntity>(entity),
            ["gsi1pk"] = UserPartition.Pk,
            ["gsi1sk"] = updatedAt
        };

        await _table.PutItemAsync(document, ct);
    }

    public async Task<(IReadOnlyList<SyncEntity> Items, string Cursor)> QueryDeltaAsync(string? cursor, CancellationToken ct)
    {
        var since = string.IsNullOrEmpty(cursor) ? MinCursor : cursor;

        var config = new QueryOperationConfig
        {
            IndexName = Gsi1Name,
            KeyExpression = new Expression
            {
                ExpressionStatement = "gsi1pk = :pk and gsi1sk > :since",
                ExpressionAttributeValues =
                {
                    [":pk"] = UserPartition.Pk,
                    [":since"] = since
                }
            }
        };

        var search = _table.Query(config);
        var items = new List<SyncEntity>();
        string newCursor = since;

        do
        {
            var page = await search.GetNextSetAsync(ct);
            foreach (var document in page)
            {
                items.Add(Deserialize(document));
                newCursor = document["updatedAt"].AsString();
            }
        } while (!search.IsDone);

        return (items, cursor is null && items.Count == 0 ? MinCursor : newCursor);
    }

    private static SyncEntity Deserialize(Document document)
    {
        var json = document["data"].AsString();
        return JsonSerializer.Deserialize<SyncEntity>(json)
            ?? throw new InvalidOperationException($"Stored entity data for sk={document["sk"].AsString()} did not deserialize.");
    }
}
