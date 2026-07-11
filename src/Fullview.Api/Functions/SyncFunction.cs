using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Fullview.Api.Sync;
using Fullview.Domain.Sync;

namespace Fullview.Api.Functions;

/// <summary>
/// Stage 2 — the single `/sync` endpoint used by both device and web (B5): idempotent
/// outbox apply with last-write-wins conflict resolution, then delta pull since the
/// caller's cursor.
/// </summary>
public sealed class SyncFunction
{
    private static readonly SyncService Service = new(new DynamoSyncStore(
        new AmazonDynamoDBClient(),
        Environment.GetEnvironmentVariable("FULLVIEW_TABLE_NAME")
            ?? throw new InvalidOperationException("FULLVIEW_TABLE_NAME must be set on the Lambda's environment.")));

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        if (string.IsNullOrEmpty(request.Body))
        {
            return BadRequest("Request body is required.");
        }

        SyncRequest? syncRequest;
        try
        {
            syncRequest = JsonSerializer.Deserialize<SyncRequest>(request.Body);
        }
        catch (JsonException)
        {
            return BadRequest("Request body is not valid JSON.");
        }

        if (syncRequest is null || string.IsNullOrWhiteSpace(syncRequest.DeviceId))
        {
            return BadRequest("deviceId is required.");
        }

        var response = await Service.ApplyAndPullAsync(syncRequest, CancellationToken.None);

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body = JsonSerializer.Serialize(response)
        };
    }

    private static APIGatewayHttpApiV2ProxyResponse BadRequest(string message) => new()
    {
        StatusCode = 400,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(new { error = message })
    };
}
