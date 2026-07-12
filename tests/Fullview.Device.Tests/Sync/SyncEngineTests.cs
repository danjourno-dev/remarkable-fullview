using System.Net;
using System.Net.Http.Json;
using Fullview.Device.Storage;
using Fullview.Device.Sync;
using Fullview.Domain;
using Fullview.Domain.Entities;
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
        new(_store, _settings, new SyncClient(new HttpClient(handler) { BaseAddress = new Uri("http://device.local") }));

    [Fact]
    public async Task SyncOnceAsync_Success_DrainsOutboxAndStampsLastSyncedAt()
    {
        _store.Save(MakeTodo("t1", "Reply to recruiter"));
        var handler = new StubHandler(getAll: [], onPut: _ => { });
        var engine = MakeEngine(handler);

        var result = await engine.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.True(result.Changed);
        Assert.Equal(0, _store.OutboxCount());
        Assert.Equal(["t1"], handler.PutRequests);
        Assert.NotNull(_settings.GetLastSyncedAt());
    }

    [Fact]
    public async Task SyncOnceAsync_PutFailureMidDrain_LeavesRemainingOutboxIntact()
    {
        _store.Save(MakeTodo("t1", "First"));
        _store.Save(MakeTodo("t2", "Second"));
        var handler = new StubHandler(getAll: [], onPut: id =>
        {
            if (id == "t2")
            {
                throw new HttpRequestException("network down");
            }
        });
        var engine = MakeEngine(handler);

        var result = await engine.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Failed, result.Outcome);
        Assert.Equal(1, _store.OutboxCount());
        var remaining = Assert.Single(_store.ReadOutbox());
        Assert.Equal("t2", remaining.Entity.Id);
        Assert.Null(_settings.GetLastSyncedAt());
    }

    [Fact]
    public async Task SyncOnceAsync_EmptyOutbox_AppliesSnapshotAndStampsLastSyncedAt()
    {
        var remoteTodo = MakeTodo("remote1", "From server");
        var handler = new StubHandler(getAll: [remoteTodo], onPut: _ => { });
        var engine = MakeEngine(handler);

        var result = await engine.SyncOnceAsync(CancellationToken.None);

        Assert.Equal(SyncOutcome.Succeeded, result.Outcome);
        Assert.True(result.Changed);
        var todo = Assert.Single(_store.Query<Todo>());
        Assert.Equal("From server", todo.Title);
        Assert.NotNull(_settings.GetLastSyncedAt());
    }

    [Fact]
    public async Task SyncOnceAsync_SnapshotOlderThanLocal_DoesNotOverwriteLocal()
    {
        var local = MakeTodo("t1", "Local title");
        local.UpdatedAt = DateTimeOffset.UtcNow;
        _store.SaveSeed(local);

        var remote = MakeTodo("t1", "Stale remote title");
        remote.UpdatedAt = local.UpdatedAt.AddMinutes(-5);
        var handler = new StubHandler(getAll: [remote], onPut: _ => { });
        var engine = MakeEngine(handler);

        await engine.SyncOnceAsync(CancellationToken.None);

        var todo = Assert.Single(_store.Query<Todo>());
        Assert.Equal("Local title", todo.Title);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly IReadOnlyList<SyncEntity> _getAll;
        private readonly Action<string> _onPut;

        public List<string> PutRequests { get; } = [];

        public StubHandler(IReadOnlyList<SyncEntity> getAll, Action<string> onPut)
        {
            _getAll = getAll;
            _onPut = onPut;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Put)
            {
                string id = request.RequestUri!.Segments[^1];
                PutRequests.Add(id);
                _onPut(id);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(_getAll, options: DeviceJson.Options)
            };
            return Task.FromResult(response);
        }
    }
}
