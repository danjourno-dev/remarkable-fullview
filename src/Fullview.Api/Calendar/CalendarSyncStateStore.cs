using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;

namespace Fullview.Api.Calendar;

/// <summary>Per-calendar Google `nextSyncToken` plus reconciliation bookkeeping, in the same
/// table as everything else but under its own pk/sk so it never collides with (or shows up
/// in) the device/web sync entities — this is puller-internal bookkeeping, not something a
/// client ever reads.</summary>
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
        if (document is null || !document.TryGetValue("syncToken", out var value))
        {
            return null;
        }

        var token = value.AsString();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    /// <summary>An UpdateItem (not PutItem) so this never clobbers <c>lastReconciled</c> on
    /// the same item — a null token (discard on 410 Gone, or to force a full refetch) is
    /// stored as an empty string rather than deleting the item.</summary>
    public Task SaveSyncTokenAsync(string calendarId, string? syncToken, CancellationToken ct) =>
        _table.UpdateItemAsync(new Document
        {
            ["pk"] = Pk,
            ["sk"] = calendarId,
            ["syncToken"] = syncToken ?? string.Empty
        }, ct);

    /// <summary>Last time <see cref="CalendarReconciler"/> ran a full mark-and-sweep against
    /// this calendar. Null means "never" — the next pull forces a full (non-incremental)
    /// fetch to seed one.</summary>
    public async Task<DateTimeOffset?> GetLastReconciledAsync(string calendarId, CancellationToken ct)
    {
        var document = await _table.GetItemAsync(Pk, calendarId, ct);
        if (document is null || !document.TryGetValue("lastReconciled", out var value))
        {
            return null;
        }

        var raw = value.AsString();
        return string.IsNullOrEmpty(raw) ? null : DateTimeOffset.Parse(raw);
    }

    public Task SaveLastReconciledAsync(string calendarId, DateTimeOffset timestamp, CancellationToken ct) =>
        _table.UpdateItemAsync(new Document
        {
            ["pk"] = Pk,
            ["sk"] = calendarId,
            ["lastReconciled"] = timestamp.ToUniversalTime().ToString("O")
        }, ct);
}
