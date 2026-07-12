using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;

namespace Fullview.Api.Calendar;

/// <summary>Per-calendar Google `nextSyncToken`, in the same table as everything else but
/// under its own pk/sk so it never collides with (or shows up in) the device/web sync
/// entities — this is puller-internal bookkeeping, not something a client ever reads.</summary>
public sealed class CalendarSyncStateStore
{
    private const string Pk = "CALSYNC";

    private readonly Table _table;

    public CalendarSyncStateStore(IAmazonDynamoDB client, string tableName)
    {
        _table = Table.LoadTable(client, tableName);
    }

    public async Task<string?> GetSyncTokenAsync(string calendarId, CancellationToken ct)
    {
        var document = await _table.GetItemAsync(Pk, calendarId, ct);
        return document is null || !document.TryGetValue("syncToken", out var value) ? null : value.AsString();
    }

    public async Task SaveSyncTokenAsync(string calendarId, string? syncToken, CancellationToken ct)
    {
        if (syncToken is null)
        {
            await _table.DeleteItemAsync(Pk, calendarId, ct);
            return;
        }

        var document = new Document
        {
            ["pk"] = Pk,
            ["sk"] = calendarId,
            ["syncToken"] = syncToken
        };

        await _table.PutItemAsync(document, ct);
    }
}
