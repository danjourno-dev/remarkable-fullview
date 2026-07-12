using Fullview.Device.Storage;
using Fullview.Domain;
using Microsoft.Data.Sqlite;

namespace Fullview.Device.Tests;

public class DeviceSettingsTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DeviceDatabase _database;
    private readonly DeviceSettings _settings;

    public DeviceSettingsTests()
    {
        // Unique db name per test instance so state can't leak across tests via the shared cache.
        string dbName = $"devicesettings-tests-{Guid.NewGuid():N}";
        _keepAlive = new SqliteConnection($"Data Source=file:{dbName}?mode=memory&cache=shared");
        _keepAlive.Open();
        _database = DeviceDatabase.Open($"file:{dbName}?mode=memory&cache=shared");
        _settings = new DeviceSettings(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
        _keepAlive.Dispose();
    }

    [Fact]
    public void GetMode_DefaultsToPersonal_WhenNeverSet()
    {
        Assert.Equal(SyncContext.Personal, _settings.GetMode());
    }

    [Fact]
    public void SetMode_ThenGetMode_RoundTrips()
    {
        _settings.SetMode(SyncContext.Work);

        Assert.Equal(SyncContext.Work, _settings.GetMode());
    }

    [Fact]
    public void SetMode_Twice_UpdatesRatherThanThrowing()
    {
        _settings.SetMode(SyncContext.Work);
        _settings.SetMode(SyncContext.Personal);

        Assert.Equal(SyncContext.Personal, _settings.GetMode());
    }

    [Fact]
    public void GetLastSyncedAt_DefaultsToNull_WhenNeverSet()
    {
        Assert.Null(_settings.GetLastSyncedAt());
    }

    [Fact]
    public void SetLastSyncedAt_ThenGetLastSyncedAt_RoundTrips()
    {
        var at = new DateTimeOffset(2026, 7, 12, 9, 30, 0, TimeSpan.Zero);

        _settings.SetLastSyncedAt(at);

        Assert.Equal(at, _settings.GetLastSyncedAt());
    }
}
