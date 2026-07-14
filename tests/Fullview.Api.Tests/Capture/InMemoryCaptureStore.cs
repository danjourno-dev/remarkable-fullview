using Fullview.Api.Capture;

namespace Fullview.Api.Tests.Capture;

public sealed class InMemoryCaptureStore : ICaptureStore
{
    public Dictionary<string, byte[]> Objects { get; } = [];

    public Task PutAsync(string key, byte[] content, CancellationToken ct)
    {
        Objects[key] = content;
        return Task.CompletedTask;
    }
}
