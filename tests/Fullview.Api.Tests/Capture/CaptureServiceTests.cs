using Fullview.Api.Capture;

namespace Fullview.Api.Tests.Capture;

public class CaptureServiceTests
{
    [Fact]
    public async Task UploadPageAsync_ValidPageId_WritesToInboxKeyAndReturnsIt()
    {
        var store = new InMemoryCaptureStore();
        var service = new CaptureService(store);
        byte[] bytes = [1, 2, 3];

        string key = await service.UploadPageAsync("page-123", bytes, CancellationToken.None);

        Assert.Equal("inbox/page-123.rm", key);
        Assert.Equal(bytes, store.Objects[key]);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData("has spaces")]
    public async Task UploadPageAsync_InvalidPageId_Throws(string pageId)
    {
        var store = new InMemoryCaptureStore();
        var service = new CaptureService(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.UploadPageAsync(pageId, [1], CancellationToken.None));
    }

    [Theory]
    [InlineData("page-123", true)]
    [InlineData("ABCDEF-0123", true)]
    [InlineData("..", false)]
    [InlineData("../../etc", false)]
    [InlineData("a/b", false)]
    public void IsValidPageId_MatchesExpected(string pageId, bool expected)
    {
        Assert.Equal(expected, CaptureService.IsValidPageId(pageId));
    }
}
