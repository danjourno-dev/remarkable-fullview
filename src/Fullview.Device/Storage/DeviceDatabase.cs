using Microsoft.Data.Sqlite;

namespace Fullview.Device.Storage;

/// <summary>Owns the SQLite connection and applies pending migrations on open.</summary>
public sealed class DeviceDatabase : IDisposable
{
    public SqliteConnection Connection { get; }

    private DeviceDatabase(SqliteConnection connection)
    {
        Connection = connection;
    }

    public static DeviceDatabase Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        // Stage 5: the headless FULLVIEW_MODE=sync-once process (systemd timer, RTC wake) and
        // the foreground AppLoad app can both hold this file open at once. WAL lets readers
        // and a writer coexist instead of failing immediately on SQLITE_BUSY; busy_timeout
        // covers writer-vs-writer contention by retrying instead of throwing.
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        var database = new DeviceDatabase(connection);
        database.Migrate();
        return database;
    }

    private void Migrate()
    {
        long currentVersion = GetUserVersion();
        foreach (var migration in Migrations.All)
        {
            if (migration.Version <= currentVersion)
            {
                continue;
            }

            using var transaction = Connection.BeginTransaction();
            using (var command = Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                command.ExecuteNonQuery();
            }

            SetUserVersion(migration.Version, transaction);
            transaction.Commit();
        }
    }

    private long GetUserVersion()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }

    private void SetUserVersion(long version, SqliteTransaction transaction)
    {
        using var command = Connection.CreateCommand();
        command.Transaction = transaction;
        // PRAGMA doesn't accept parameters; the version is our own migration list, never user input.
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    public void Dispose() => Connection.Dispose();
}
