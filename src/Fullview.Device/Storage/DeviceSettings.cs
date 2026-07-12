using Fullview.Domain;

namespace Fullview.Device.Storage;

/// <summary>
/// Device-local UI/sync bookkeeping state, never itself synced (B5): current mode, plus
/// when the last successful sync completed. Mode defaults to Personal on first boot (no row
/// yet) rather than requiring an explicit initial value; last-synced defaults to "never
/// synced" the same way.
/// </summary>
public sealed class DeviceSettings
{
    private const string ModeKey = "mode";
    private const string LastSyncedAtKey = "last_synced_at";
    private readonly DeviceDatabase _database;

    public DeviceSettings(DeviceDatabase database)
    {
        _database = database;
    }

    public SyncContext GetMode()
    {
        string? value = Get(ModeKey);
        return value is not null && Enum.TryParse<SyncContext>(value, out var mode) ? mode : SyncContext.Personal;
    }

    public void SetMode(SyncContext mode) => Set(ModeKey, mode.ToString());

    /// <summary>When the last successful `/sync` call completed, or null if this device has
    /// never synced. Drives the footer's "SYNCED HH:MM" text.</summary>
    public DateTimeOffset? GetLastSyncedAt()
    {
        string? value = Get(LastSyncedAtKey);
        return value is not null && DateTimeOffset.TryParse(value, out var at) ? at : null;
    }

    public void SetLastSyncedAt(DateTimeOffset at) => Set(LastSyncedAtKey, at.ToString("O"));

    private string? Get(string key)
    {
        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private void Set(string key, string value)
    {
        using var command = _database.Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value) VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
