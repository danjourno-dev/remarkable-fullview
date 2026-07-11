using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.Serialization.SystemTextJson;

[assembly: LambdaSerializer(typeof(DefaultLambdaJsonSerializer))]

namespace Fullview.Api.Functions;

/// <summary>
/// Stage 1 placeholder Lambda — proves the OIDC deploy pipeline end to end.
/// Real sync/capture/auth handlers land in later stages.
/// </summary>
public sealed class HealthFunction
{
    public APIGatewayHttpApiV2ProxyResponse FunctionHandler(
        APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context)
    {
        return new APIGatewayHttpApiV2ProxyResponse
        {
            StatusCode = 200,
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            Body = "{\"status\":\"ok\"}"
        };
    }
}
