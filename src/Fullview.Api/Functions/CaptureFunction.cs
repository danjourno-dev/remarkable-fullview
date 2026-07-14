using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.S3;
using Fullview.Api.Capture;
using Fullview.Api.Sync;

namespace Fullview.Api.Functions;

/// <summary>
/// `PUT /captures/{pageId}` — the device uploads a changed Inbox page's raw `.rm` bytes here
/// instead of writing to S3 directly (see docs/plans/implementation.md Stage 7): it never
/// holds S3 credentials, only the same `x-api-key` it already uses for `/entities`. Body is
/// binary (`Content-Type: application/octet-stream`); API Gateway HTTP APIs base64-encode
/// non-text bodies automatically, hence <c>IsBase64Encoded</c> below rather than any
/// binaryMediaTypes configuration (that's a REST-API-v1 concept, not applicable to the HTTP
/// API this stack uses).
/// </summary>
public sealed class CaptureFunction
{
    private static readonly CaptureService Service = new(new S3CaptureStore(
        new AmazonS3Client(),
        Environment.GetEnvironmentVariable("FULLVIEW_INBOX_BUCKET_NAME")
            ?? throw new InvalidOperationException("FULLVIEW_INBOX_BUCKET_NAME must be set on the Lambda's environment.")));

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        if (request.RequestContext.Http.Method != "PUT")
        {
            return BadRequest($"Unsupported method: {request.RequestContext.Http.Method}");
        }

        var pageId = request.PathParameters is not null && request.PathParameters.TryGetValue("pageId", out var id) ? id : null;
        if (string.IsNullOrWhiteSpace(pageId))
        {
            return BadRequest("Route must include a pageId.");
        }

        if (!CaptureService.IsValidPageId(pageId))
        {
            return BadRequest($"'{pageId}' is not a valid page id.");
        }

        if (string.IsNullOrEmpty(request.Body))
        {
            return BadRequest("Request body is required.");
        }

        byte[] content = request.IsBase64Encoded
            ? Convert.FromBase64String(request.Body)
            : System.Text.Encoding.UTF8.GetBytes(request.Body);

        string key = await Service.UploadPageAsync(pageId, content, CancellationToken.None);

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body = JsonSerializer.Serialize(new { s3Key = key }, SyncJson.Options)
        };
    }

    private static APIGatewayHttpApiV2ProxyResponse BadRequest(string message) => new()
    {
        StatusCode = 400,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(new { error = message }, SyncJson.Options)
    };
}
