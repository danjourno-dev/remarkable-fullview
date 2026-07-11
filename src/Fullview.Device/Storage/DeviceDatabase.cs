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
