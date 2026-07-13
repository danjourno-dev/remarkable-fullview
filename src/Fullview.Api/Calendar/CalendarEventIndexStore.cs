using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;

namespace Fullview.Api.Calendar;

/// <summary>Maps a Google-minted event id (which churns across the Work mirror's
/// wipe-and-rebuild cycle, per Stage 6.6) to the content-derived <see cref="Fullview.Domain.Entities.AgendaEvent"/>
/// id it was last mapped to (<see cref="GoogleEventMapper.BuildEntityId"/>). A cancellation
/// notice from Google carries only its own event id — never title/start/end — so without
/// this index there'd be no way to know which content-keyed row to tombstone. Same table,
/// own pk/sk so it never collides with (or shows up in) the device/web sync entities —
/// puller-internal bookkeeping, not something a client ever reads.</summary>
public sealed class CalendarEventIndexStore
{
    private const string Pk = "CALIDX";

    private readonly Table _table;

    public CalendarEventIndexStore(IAmazonDynamoDB client, string tableName)
    {
        _table = Table.LoadTable(client, tableName);
    }

    public async Task<string?> GetEntityIdAsync(string calendarId, string googleEventId, CancellationToken ct)
    {
        var document = await _table.GetItemAsync(Pk, SortKey(calendarId, googleEventId), ct);
        return document is null || !document.TryGetValue("entityId", out var value) ? null : value.AsString();
    }

    public Task SaveAsync(string calendarId, string googleEventId, string entityId, CancellationToken ct)
    {
        var document = new Document
        {
            ["pk"] = Pk,
            ["sk"] = SortKey(calendarId, googleEventId),
            ["entityId"] = entityId
        };

        return _table.PutItemAsync(document, ct);
    }

    public Task DeleteAsync(string calendarId, string googleEventId, CancellationToken ct) =>
        _table.DeleteItemAsync(Pk, SortKey(calendarId, googleEventId), ct);

    private static string SortKey(string calendarId, string googleEventId) => $"{calendarId}#{googleEventId}";
}
