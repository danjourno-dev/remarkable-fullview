using Fullview.Device.Storage;

namespace Fullview.Device.Capture;

public sealed record CaptureOutboxItem(long Seq, string PageId, string FilePath, string ContentHash);

/// <summary>
/// Local SQLite state for Stage 7 capture uploads: `capture_pages` remembers the last
/// successfully-uploaded content hash per page (so <see cref="InboxWatcher"/> only re-queues a
/// page whose bytes actually changed), `capture_outbox` queues pages waiting to be PUT to
/// `/captures/{pageId}`.
/// </summary>
public sealed class CaptureStore
{
    private readonly DeviceDatabase _database;

    public CaptureStore(DeviceDatabase database)
    {
        _database = database;
    }

    public string? GetKnownHash(string pageId)
    {
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT content_hash FROM capture_pages WHERE page_id = $pageId;";
        command.Parameters.AddWithValue("$pageId", pageId);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Queues a page for upload, replacing any earlier still-pending queue entry for
    /// the same page — only the most recent bytes on disk matter, an older queued version of
    /// the same page is never worth uploading once a newer one exists.</summary>
    public void Enqueue(string pageId, string filePath, string contentHash)
    {
        using var transaction = _database.Connection.BeginTransaction();

        using (var deleteCommand = _database.Connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM capture_outbox WHERE page_id = $pageId;";
            deleteCommand.Parameters.AddWithValue("$pageId", pageId);
            deleteCommand.ExecuteNonQuery();
        }

        using (var insertCommand = _database.Connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO capture_outbox (page_id, file_path, content_hash, queued_at)
                VALUES ($pageId, $filePath, $contentHash, $queuedAt);
                """;
            insertCommand.Parameters.AddWithValue("$pageId", pageId);
            insertCommand.Parameters.AddWithValue("$filePath", filePath);
            insertCommand.Parameters.AddWithValue("$contentHash", contentHash);
            insertCommand.Parameters.AddWithValue("$queuedAt", DateTimeOffset.UtcNow.ToString("O"));
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<CaptureOutboxItem> ReadOutbox()
    {
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT seq, page_id, file_path, content_hash FROM capture_outbox ORDER BY seq;";

        var results = new List<CaptureOutboxItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new CaptureOutboxItem(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return results;
    }

    public int OutboxCount()
    {
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM capture_outbox;";
        return (int)(long)command.ExecuteScalar()!;
    }

    /// <summary>Called after a page's bytes are successfully PUT to the API: removes its
    /// capture_outbox row (up through <paramref name="seq"/>, same "delete what was actually
    /// sent" contract as <see cref="Storage.DeviceStore.DeleteOutboxThrough"/>) and records the
    /// uploaded hash so InboxWatcher won't re-queue the same bytes again.</summary>
    public void MarkUploaded(long seq, string pageId, string contentHash)
    {
        using var transaction = _database.Connection.BeginTransaction();

        using (var deleteCommand = _database.Connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM capture_outbox WHERE seq <= $seq;";
            deleteCommand.Parameters.AddWithValue("$seq", seq);
            deleteCommand.ExecuteNonQuery();
        }

        using (var upsertCommand = _database.Connection.CreateCommand())
        {
            upsertCommand.Transaction = transaction;
            upsertCommand.CommandText = """
                INSERT INTO capture_pages (page_id, content_hash, uploaded_at)
                VALUES ($pageId, $contentHash, $uploadedAt)
                ON CONFLICT(page_id) DO UPDATE SET
                    content_hash = excluded.content_hash,
                    uploaded_at = excluded.uploaded_at;
                """;
            upsertCommand.Parameters.AddWithValue("$pageId", pageId);
            upsertCommand.Parameters.AddWithValue("$contentHash", contentHash);
            upsertCommand.Parameters.AddWithValue("$uploadedAt", DateTimeOffset.UtcNow.ToString("O"));
            upsertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
