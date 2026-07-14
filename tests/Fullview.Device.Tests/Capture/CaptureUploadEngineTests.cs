using System.Net;
using System.Net.Http.Json;
using Fullview.Device.Capture;
using Fullview.Device.Storage;
using Fullview.Domain.Entities;
using Microsoft.Data.Sqlite;

namespace Fullview.Device.Tests.Capture;

public class CaptureUploadEngineTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DeviceDatabase _database;
    private readonly DeviceStore _store;
    private readonly CaptureStore _captureStore;
    private readonly string _notebookPath;

    public CaptureUploadEngineTests()
    {
        string dbName = $"captureengine-tests-{Guid.NewGuid():N}";
        _keepAlive = new SqliteConnection($"Data Source=file:{dbName}?mode=memory&cache=shared");
        _keepAlive.Open();
        _database = DeviceDatabase.Open($"file:{dbName}?mode=memory&cache=shared");
        _store = new DeviceStore(_database);
        _captureStore = new CaptureStore(_database);

        _notebookPath = Path.Combine(Path.GetTempPath(), $"captureengine-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_notebookPath);
    }

    public void Dispose()
    {
        _database.Dispose();
        _keepAlive.Dispose();
        Directory.Delete(_notebookPath, recursive: true);
    }

    private CaptureUploadEngine MakeEngine(HttpMessageHandler handler) => new(
        _captureStore, _store,
        new CaptureClient(new HttpClient(handler) { BaseAddress = new Uri("http://device.local") }),
        "test-device");

    [Fact]
    public async Task DrainAsync_QueuedPage_UploadsAndCreatesInboxPageEntity()
    {
        var filePath = Path.Combine(_notebookPath, "page-1.rm");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);

        var handler = new StubHandler(pageId => $"inbox/{pageId}.rm");
        var engine = MakeEngine(handler);

        int uploaded = await engine.DrainAsync(CancellationToken.None);

        Assert.Equal(1, uploaded);
        Assert.Equal(0, _captureStore.OutboxCount());
        var page = Assert.Single(_store.Query<InboxPage>());
        Assert.Equal("page-1", page.Id);
        Assert.Equal("inbox/page-1.rm", page.S3Key);
        Assert.Equal(InboxPageState.Queued, page.State);
        Assert.Equal(1, _store.OutboxCount());
    }

    [Fact]
    public async Task DrainAsync_UploadFails_LeavesCaptureOutboxIntact()
    {
        var filePath = Path.Combine(_notebookPath, "page-1.rm");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);

        var handler = new StubHandler(_ => throw new HttpRequestException("network down"));
        var engine = MakeEngine(handler);

        int uploaded = await engine.DrainAsync(CancellationToken.None);

        Assert.Equal(0, uploaded);
        Assert.Equal(1, _captureStore.OutboxCount());
        Assert.Empty(_store.Query<InboxPage>());
    }

    [Fact]
    public async Task DrainAsync_UploadSucceeds_RepeatDrainSkipsUnchangedPage()
    {
        var filePath = Path.Combine(_notebookPath, "page-1.rm");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);
        var handler = new StubHandler(pageId => $"inbox/{pageId}.rm");
        var engine = MakeEngine(handler);
        await engine.DrainAsync(CancellationToken.None);

        int requeued = InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);

        Assert.Equal(0, requeued);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<string, string> _onUpload;

        public StubHandler(Func<string, string> onUpload)
        {
            _onUpload = onUpload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string pageId = request.RequestUri!.Segments[^1];
            string s3Key = _onUpload(pageId);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { s3Key })
            };
            return Task.FromResult(response);
        }
    }
}
