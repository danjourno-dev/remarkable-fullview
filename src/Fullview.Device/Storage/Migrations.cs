namespace Fullview.Device.Storage;

internal sealed record Migration(long Version, string Sql);

/// <summary>
/// Ordered schema migrations, applied via PRAGMA user_version (SQLite's built-in schema
/// version counter — no separate version table needed). <see cref="Entities"/> mirrors the
/// server's DynamoSyncStore shape (whole entity as a JSON blob + queryable columns) so the
/// same polymorphic JSON round-trips both places. <see cref="Outbox"/> queues every local
/// mutation for Stage 5's sync drain; <see cref="Settings"/> holds device-local UI state
/// (current mode) that is never synced (B5).
/// </summary>
internal static class Migrations
{
    public static readonly IReadOnlyList<Migration> All =
    [
        new Migration(1, """
            CREATE TABLE entities (
                id TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                context TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                deleted INTEGER NOT NULL DEFAULT 0,
                data TEXT NOT NULL,
                PRIMARY KEY (entity_type, id)
            );

            CREATE TABLE outbox (
                seq INTEGER PRIMARY KEY AUTOINCREMENT,
                entity_id TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """),
    ];
}
