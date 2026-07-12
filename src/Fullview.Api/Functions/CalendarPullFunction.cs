using System.Text.Json;
using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Fullview.Api.Calendar;
using Fullview.Api.Sync;

namespace Fullview.Api.Functions;

/// <summary>
/// Stage 6.5 — EventBridge-scheduled sweep (every 15 min) over every calendar in the
/// FULLVIEW_GOOGLE_CALENDARS_PARAM config list. Context-agnostic by design: this class
/// has no idea "Work" or "Personal" mean anything, it just does what the config says.
/// </summary>
public sealed class CalendarPullFunction
{
    private static readonly AmazonSimpleSystemsManagementClient Ssm = new();
    private static readonly JsonSerializerOptions CalendarConfigJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private static readonly string TableName = Environment.GetEnvironmentVariable("FULLVIEW_TABLE_NAME")
        ?? throw new InvalidOperationException("FULLVIEW_TABLE_NAME must be set on the Lambda's environment.");
    private static readonly string OAuthClientParam = Environment.GetEnvironmentVariable("FULLVIEW_GOOGLE_OAUTH_CLIENT_PARAM")
        ?? throw new InvalidOperationException("FULLVIEW_GOOGLE_OAUTH_CLIENT_PARAM must be set on the Lambda's environment.");
    private static readonly string RefreshTokenParam = Environment.GetEnvironmentVariable("FULLVIEW_GOOGLE_REFRESH_TOKEN_PARAM")
        ?? throw new InvalidOperationException("FULLVIEW_GOOGLE_REFRESH_TOKEN_PARAM must be set on the Lambda's environment.");
    private static readonly string CalendarsParam = Environment.GetEnvironmentVariable("FULLVIEW_GOOGLE_CALENDARS_PARAM")
        ?? throw new InvalidOperationException("FULLVIEW_GOOGLE_CALENDARS_PARAM must be set on the Lambda's environment.");

    public async Task FunctionHandler(object input, ILambdaContext context)
    {
        var credentials = await FetchOAuthClientAsync();
        var refreshToken = await FetchParameterAsync(RefreshTokenParam, withDecryption: true);
        var calendars = await FetchCalendarsAsync();

        var syncStore = new DynamoSyncStore(new AmazonDynamoDBClient(), TableName);
        var stateStore = new CalendarSyncStateStore(new AmazonDynamoDBClient(), TableName);
        var service = new GoogleCalendarPullService(syncStore, stateStore, credentials, refreshToken, context);

        await service.PullAllAsync(calendars, CancellationToken.None);
    }

    private static async Task<GoogleOAuthCredentials> FetchOAuthClientAsync()
    {
        var json = await FetchParameterAsync(OAuthClientParam, withDecryption: true);
        try
        {
            return JsonSerializer.Deserialize<GoogleOAuthCredentials>(json, CalendarConfigJson)
                ?? throw new InvalidOperationException($"SSM parameter {OAuthClientParam} did not contain valid OAuth client JSON.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"SSM parameter {OAuthClientParam} is not valid JSON (see docs/device-setup.md checkpoint 6.5.1 for the expected {{\"clientId\":...,\"clientSecret\":...}} shape).", ex);
        }
    }

    private static async Task<IReadOnlyList<CalendarConfig>> FetchCalendarsAsync()
    {
        var json = await FetchParameterAsync(CalendarsParam, withDecryption: false);
        try
        {
            return JsonSerializer.Deserialize<List<CalendarConfig>>(json, CalendarConfigJson)
                ?? throw new InvalidOperationException($"SSM parameter {CalendarsParam} did not contain a valid calendar list.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"SSM parameter {CalendarsParam} is not valid JSON (see docs/device-setup.md's calendar config section for the expected [{{\"id\":...,\"context\":...}}] shape).", ex);
        }
    }

    private static async Task<string> FetchParameterAsync(string name, bool withDecryption)
    {
        var response = await Ssm.GetParameterAsync(new GetParameterRequest { Name = name, WithDecryption = withDecryption });
        return response.Parameter.Value;
    }
}
