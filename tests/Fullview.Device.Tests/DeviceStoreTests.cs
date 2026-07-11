using Fullview.Device.Storage;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Fullview.Device.Tests;

public class DeviceStoreTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DeviceDatabase _database;
    private readonly DeviceStore _store;

    public DeviceStoreTests()
    {
        // A shared-cache in-memory db needs one connection held open for the DB to survive;
        // DeviceDatabase.Open makes its own connection to the same URI. Each test gets its own
        // unique db name so state from one test can't leak into another.
        string dbName = $"devicestore-tests-{Guid.NewGuid():N}";
        _keepAlive = new SqliteConnection($"Data Source=file:{dbName}?mode=memory&cache=shared");
        _keepAlive.Open();
        _database = DeviceDatabase.Open($"file:{dbName}?mode=memory&cache=shared");
        _store = new DeviceStore(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
        _keepAlive.Dispose();
    }

    private static Todo MakeTodo(string id, string title, SyncContext context = SyncContext.Personal) => new()
    {
        Id = id,
        Context = context,
        UpdatedAt = DateTimeOffset.UtcNow,
        UpdatedBy = "test-device",
        Title = title
    };

    [Fact]
    public void Save_ThenQuery_RoundTripsTheEntity()
    {
        _store.Save(MakeTodo("t1", "Book Yael gym session"));

        var todos = _store.Query<Todo>();

        var todo = Assert.Single(todos);
        Assert.Equal("Book Yael gym session", todo.Title);
    }

    [Fact]
    public void Save_SameIdTwice_UpsertsRatherThanDuplicating()
    {
        _store.Save(MakeTodo("t1", "Original title"));
        var updated = MakeTodo("t1", "Updated title");
        _store.Save(updated);

        var todos = _store.Query<Todo>();

        var todo = Assert.Single(todos);
        Assert.Equal("Updated title", todo.Title);
    }

    [Fact]
    public void Save_QueuesAnOutboxRowInTheSameTransaction()
    {
        _store.Save(MakeTodo("t1", "Reply to recruiter"));

        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM outbox WHERE entity_id = 't1';";
        long count = (long)command.ExecuteScalar()!;

        Assert.Equal(1, count);
    }

    [Fact]
    public void SaveSeed_DoesNotQueueAnOutboxRow()
    {
        _store.SaveSeed(MakeTodo("seed1", "Seed todo"));

        using var command = _database.Connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM outbox;";
        long count = (long)command.ExecuteScalar()!;

        Assert.Equal(0, count);
    }

    [Fact]
    public void Query_OnlyReturnsMatchingEntityType()
    {
        _store.SaveSeed(MakeTodo("t1", "A todo"));
        _store.SaveSeed(new ShoppingItem
        {
            Id = "s1",
            Context = SyncContext.Personal,
            UpdatedAt = DateTimeOffset.UtcNow,
            UpdatedBy = "test-device",
            Name = "Milk"
        });

        Assert.Single(_store.Query<Todo>());
        Assert.Single(_store.Query<ShoppingItem>());
    }

    [Fact]
    public void ToggleTodoCompleted_FlipsCompletedAndStampsUpdatedBy()
    {
        _store.SaveSeed(MakeTodo("t1", "Book gym"));

        _store.ToggleTodoCompleted("t1", "device-42");

        var todo = Assert.Single(_store.Query<Todo>());
        Assert.True(todo.Completed);
        Assert.Equal("device-42", todo.UpdatedBy);
    }

    [Fact]
    public void ToggleTodoCompleted_UnknownId_DoesNotThrow()
    {
        var exception = Record.Exception(() => _store.ToggleTodoCompleted("missing", "device-42"));

        Assert.Null(exception);
    }
}
