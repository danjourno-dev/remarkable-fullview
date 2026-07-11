using Fullview.Domain;

namespace Fullview.Device.Storage;

/// <summary>
/// Device-local UI state that is never synced (B5): today, just the current mode. Defaults
/// to Personal on first boot (no row yet) rather than requiring an explicit initial value.
/// </summary>
public sealed class DeviceSettings
{
    private const string ModeKey = "mode";
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
