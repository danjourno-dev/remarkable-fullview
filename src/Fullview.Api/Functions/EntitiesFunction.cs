using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Fullview.Api.Sync;
using Fullview.Domain.Entities;

namespace Fullview.Api.Functions;

/// <summary>
/// The `/entities` protocol used by both device and web: GET (full list, including
/// tombstones), POST (create — 409 if the Id/EntityType already exists), PUT `/{id}`
/// (last-write-wins upsert — what the device outbox drain uses for every queued mutation,
/// create or update, since replay must stay idempotent).
/// </summary>
public sealed class EntitiesFunction
{
    private static readonly SyncService Service = new(new DynamoSyncStore(
        new AmazonDynamoDBClient(),
        Environment.GetEnvironmentVariable("FULLVIEW_TABLE_NAME")
            ?? throw new InvalidOperationException("FULLVIEW_TABLE_NAME must be set on the Lambda's environment.")));

    public async Task<APIGatewayHttpApiV2ProxyResponse> FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        return request.RequestContext.Http.Method switch
        {
            "GET" => await HandleGetAsync(),
            "POST" => await HandlePostAsync(request),
            "PUT" => await HandlePutAsync(request),
            var method => BadRequest($"Unsupported method: {method}")
        };
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandleGetAsync()
    {
        var entities = await Service.GetAllAsync(CancellationToken.None);
        return Ok(entities);
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandlePostAsync(APIGatewayHttpApiV2ProxyRequest request)
    {
        if (!TryParseBody(request.Body, out var entity, out var error))
        {
            return BadRequest(error);
        }

        var created = await Service.CreateAsync(entity!, CancellationToken.None);
        if (!created)
        {
            return new APIGatewayHttpApiV2ProxyResponse
            {
                StatusCode = 409,
                Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                Body = JsonSerializer.Serialize(new { error = $"Entity {entity!.SortKey} already exists." }, SyncJson.Options)
            };
        }

        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 201,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body = JsonSerializer.Serialize<SyncEntity>(entity!, SyncJson.Options)
        };
    }

    private async Task<APIGatewayHttpApiV2ProxyResponse> HandlePutAsync(APIGatewayHttpApiV2ProxyRequest request)
    {
        if (!TryParseBody(request.Body, out var entity, out var error))
        {
            return BadRequest(error);
        }

        var routeId = request.PathParameters is not null && request.PathParameters.TryGetValue("id", out var id) ? id : null;
        if (string.IsNullOrWhiteSpace(routeId))
        {
            return BadRequest("Route must include an id.");
        }

        if (entity!.Id != routeId)
        {
            return BadRequest("Body Id must match the route id.");
        }

        await Service.ApplyMutationAsync(entity, CancellationToken.None);

        return Ok(entity);
    }

    private static bool TryParseBody(string? body, out SyncEntity? entity, out string error)
    {
        entity = null;
        error = string.Empty;

        if (string.IsNullOrEmpty(body))
        {
            error = "Request body is required.";
            return false;
        }

        try
        {
            entity = JsonSerializer.Deserialize<SyncEntity>(body, SyncJson.Options);
        }
        catch (JsonException)
        {
            error = "Request body is not valid JSON.";
            return false;
        }

        if (entity is null)
        {
            error = "Request body is not valid JSON.";
            return false;
        }

        return true;
    }

    private static APIGatewayHttpApiV2ProxyResponse Ok<T>(T body) => new()
    {
        StatusCode = 200,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(body, SyncJson.Options)
    };

    private static APIGatewayHttpApiV2ProxyResponse BadRequest(string message) => new()
    {
        StatusCode = 400,
        Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
        Body = JsonSerializer.Serialize(new { error = message }, SyncJson.Options)
    };
}
