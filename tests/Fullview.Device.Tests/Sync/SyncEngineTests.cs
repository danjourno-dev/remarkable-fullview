using System.Net;
using System.Net.Http.Json;
using Fullview.Device.Storage;
using Fullview.Device.Sync;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Domain.Sync;
using Microsoft.Data.Sqlite;

namespace Fullview.Device.Tests.Sync;

public class SyncEngineTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DeviceDatabase _database;
    private readonly DeviceStore _store;
    private readonly DeviceSettings _settings;

    public SyncEngineTests()
    {
        string dbName = $"syncengine-tests-{Guid.NewGuid():N}";
        _keepAlive = new SqliteConnection($"Data Source=file:{dbName}?mode=memory&cache=shared");
        _keepAlive.Open();
        _database = DeviceDatabase.Open($"file:{dbName}?mode=memory&cache=shared");
        _store = new DeviceStore(_database);
        _settings = new DeviceSettings(_database);
    }

    public void Dispose()
    {
        _database.Dispose();
        _keepAlive.Dispose();
    }

    private static Todo MakeTodo(string id, string title) => new()
    {
        Id = id,
        Context = SyncContext.Personal,
        UpdatedAt = DateTimeOffset.UtcNow,
        UpdatedBy = "test-device",
        Title = title
    };

    private SyncEngine MakeEngine(HttpMessageHandler handler) =>
        new(_store, _settings, new SyncClient(new HttpClient(handler) { BaseAddress = new Uri("http://device.local") }), "test-device");

    [Fact]
    public async Task SyncOnceAsync_Success_DrainsOutboxAdvancesCursorAndStampsLastSyncedAt()
    {
        _store.Save(MakeTodo("t1", "Reply to recruiter"));
        var handler = new StubHandler((_, _) => new SyncResponse { Cursor = "cursor-1" });
        var engine = MakeEngine(handler);

        var outcome = await engine.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Succeeded, outcome);
        Assert.Equal(0, _store.OutboxCount());
        Assert.Equal("cursor-1", _settings.GetSyncCursor());
        Assert.NotNull(_settings.GetLastSyncedAt());
    }

    [Fact]
    public async Task SyncOnceAsync_Failure_LeavesOutboxAndCursorUntouched()
    {
        _store.Save(MakeTodo("t1", "Reply to recruiter"));
        var handler = new StubHandler((_, _) => throw new HttpRequestException("network down"));
        var engine = MakeEngine(handler);

        var outcome = await engine.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Failed, outcome);
        Assert.Equal(1, _store.OutboxCount());
        Assert.Null(_settings.GetSyncCursor());
        Assert.Null(_settings.GetLastSyncedAt());
    }

    [Fact]
    public async Task SyncOnceAsync_EmptyOutbox_StillAppliesDeltaAndAdvancesCursor()
    {
        var remoteTodo = MakeTodo("remote1", "From server");
        var handler = new StubHandler((_, _) => new SyncResponse
        {
            Cursor = "cursor-2",
            Delta = [remoteTodo]
        });
        var engine = MakeEngine(handler);

        var outcome = await engine.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Succeeded, outcome);
        Assert.Equal("cursor-2", _settings.GetSyncCursor());
        var todo = Assert.Single(_store.Query<Todo>());
        Assert.Equal("From server", todo.Title);
    }

    [Fact]
    public async Task SyncOnceAsync_DeltaOlderThanLocal_DoesNotOverwriteLocal()
    {
        var local = MakeTodo("t1", "Local title");
        local.UpdatedAt = DateTimeOffset.UtcNow;
        _store.SaveSeed(local);

        var remote = MakeTodo("t1", "Stale remote title");
        remote.UpdatedAt = local.UpdatedAt.AddMinutes(-5);
        var handler = new StubHandler((_, _) => new SyncResponse { Cursor = "cursor-3", Delta = [remote] });
        var engine = MakeEngine(handler);

        await engine.SyncOnceAsync(CancellationToken.None);

        var todo = Assert.Single(_store.Query<Todo>());
        Assert.Equal("Local title", todo.Title);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, SyncResponse> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, SyncResponse> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = _respond(request, cancellationToken);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(body, options: DeviceJson.Options)
            };
            return Task.FromResult(response);
        }
    }
}
