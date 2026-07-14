using Amazon.S3;
using Amazon.S3.Model;

namespace Fullview.Api.Capture;

public sealed class S3CaptureStore(IAmazonS3 s3, string bucketName) : ICaptureStore
{
    public async Task PutAsync(string key, byte[] content, CancellationToken ct)
    {
        using var stream = new MemoryStream(content);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = stream,
            ContentType = "application/octet-stream"
        }, ct);
    }
}
