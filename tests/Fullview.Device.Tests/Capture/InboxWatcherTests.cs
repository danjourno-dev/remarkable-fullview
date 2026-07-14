using Fullview.Device.Capture;
using Fullview.Device.Storage;
using Microsoft.Data.Sqlite;

namespace Fullview.Device.Tests.Capture;

public class InboxWatcherTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly DeviceDatabase _database;
    private readonly CaptureStore _captureStore;
    private readonly string _notebookPath;

    public InboxWatcherTests()
    {
        string dbName = $"inboxwatcher-tests-{Guid.NewGuid():N}";
        _keepAlive = new SqliteConnection($"Data Source=file:{dbName}?mode=memory&cache=shared");
        _keepAlive.Open();
        _database = DeviceDatabase.Open($"file:{dbName}?mode=memory&cache=shared");
        _captureStore = new CaptureStore(_database);

        _notebookPath = Path.Combine(Path.GetTempPath(), $"inboxwatcher-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_notebookPath);
    }

    public void Dispose()
    {
        _database.Dispose();
        _keepAlive.Dispose();
        Directory.Delete(_notebookPath, recursive: true);
    }

    [Fact]
    public void ScanAndEnqueue_NewPage_QueuesIt()
    {
        File.WriteAllBytes(Path.Combine(_notebookPath, "page-1.rm"), [1, 2, 3]);

        int queued = InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);

        Assert.Equal(1, queued);
        var item = Assert.Single(_captureStore.ReadOutbox());
        Assert.Equal("page-1", item.PageId);
    }

    [Fact]
    public void ScanAndEnqueue_UnchangedPage_DoesNotRequeueAfterUpload()
    {
        var filePath = Path.Combine(_notebookPath, "page-1.rm");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);
        var queuedItem = Assert.Single(_captureStore.ReadOutbox());
        _captureStore.MarkUploaded(queuedItem.Seq, queuedItem.PageId, queuedItem.ContentHash);

        int queued = InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);

        Assert.Equal(0, queued);
        Assert.Empty(_captureStore.ReadOutbox());
    }

    [Fact]
    public void ScanAndEnqueue_ChangedPageAfterUpload_RequeuesIt()
    {
        var filePath = Path.Combine(_notebookPath, "page-1.rm");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);
        var firstQueued = Assert.Single(_captureStore.ReadOutbox());
        _captureStore.MarkUploaded(firstQueued.Seq, firstQueued.PageId, firstQueued.ContentHash);

        File.WriteAllBytes(filePath, [4, 5, 6]);
        int queued = InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);

        Assert.Equal(1, queued);
        var item = Assert.Single(_captureStore.ReadOutbox());
        Assert.Equal("page-1", item.PageId);
        Assert.NotEqual(firstQueued.ContentHash, item.ContentHash);
    }

    [Fact]
    public void ScanAndEnqueue_MissingDirectory_ReturnsZero()
    {
        int queued = InboxWatcher.ScanAndEnqueue(Path.Combine(_notebookPath, "does-not-exist"), _captureStore);

        Assert.Equal(0, queued);
    }

    [Fact]
    public void ScanAndEnqueue_IgnoresNonRmFiles()
    {
        File.WriteAllBytes(Path.Combine(_notebookPath, "page-1.metadata"), [1]);

        int queued = InboxWatcher.ScanAndEnqueue(_notebookPath, _captureStore);

        Assert.Equal(0, queued);
    }
}
