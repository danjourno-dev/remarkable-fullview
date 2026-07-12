using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;

namespace Fullview.Api.Functions;

/// <summary>
/// REQUEST authorizer for the HTTP API's protected routes. Single-user v1 auth model (see
/// docs/plans/implementation.md): one shared API key held as a SecureString in SSM Parameter
/// Store, checked against the `x-api-key` header. Cognito is out of scope until v2 — nobody
/// signs up, there's just one caller (the device and, later, the web app) that either knows
/// the key or doesn't.
/// </summary>
public sealed class AuthorizerFunction
{
    private static readonly AmazonSimpleSystemsManagementClient Ssm = new();

    private static readonly string ParameterName = Environment.GetEnvironmentVariable("FULLVIEW_API_KEY_PARAM")
        ?? throw new InvalidOperationException("FULLVIEW_API_KEY_PARAM must be set on the authorizer Lambda's environment.");

    // Cached for the lifetime of the execution environment (Lambda reuses warm instances) so a
    // hot path doesn't hit SSM on every request — API Gateway's own authorizer result cache
    // (AuthorizerResultTtlInSeconds, set in the CDK stack) covers most repeat calls anyway; this
    // just covers cache misses on an already-warm instance.
    private static string? _cachedKey;

    public async Task<APIGatewayCustomAuthorizerV2SimpleResponse> FunctionHandler(
        APIGatewayCustomAuthorizerV2Request request, ILambdaContext context)
    {
        string? presented = request.Headers is not null && request.Headers.TryGetValue("x-api-key", out var value)
            ? value
            : null;

        if (string.IsNullOrEmpty(presented))
        {
            return new APIGatewayCustomAuthorizerV2SimpleResponse { IsAuthorized = false };
        }

        var expected = _cachedKey ??= await FetchKeyAsync();
        var isAuthorized = !string.IsNullOrEmpty(expected) && FixedTimeEquals(presented, expected);

        return new APIGatewayCustomAuthorizerV2SimpleResponse { IsAuthorized = isAuthorized };
    }

    private static async Task<string> FetchKeyAsync()
    {
        var response = await Ssm.GetParameterAsync(new GetParameterRequest
        {
            Name = ParameterName,
            WithDecryption = true
        });
        return response.Parameter.Value;
    }

    private static bool FixedTimeEquals(string presented, string expected)
    {
        var presentedBytes = System.Text.Encoding.UTF8.GetBytes(presented);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        return presentedBytes.Length == expectedBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(presentedBytes, expectedBytes);
    }
}
