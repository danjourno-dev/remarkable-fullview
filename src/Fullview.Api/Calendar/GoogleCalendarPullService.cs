using Amazon.Lambda.Core;
using Fullview.Api.Sync;
using Fullview.Domain.Entities;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Fullview.Api.Calendar;

/// <summary>Stage 6.5's puller: one context-agnostic sweep over every configured calendar.
/// Per the plan's design rule, this class has no work-specific code path — a calendar's
/// <see cref="Fullview.Domain.SyncContext"/> comes entirely from <see cref="CalendarConfig"/>,
/// never inferred from event content.</summary>
public sealed class GoogleCalendarPullService
{
    private static readonly TimeSpan LookBack = TimeSpan.FromDays(1);
    private static readonly TimeSpan LookAhead = TimeSpan.FromDays(8);

    private readonly ISyncStore _syncStore;
    private readonly CalendarSyncStateStore _stateStore;
    private readonly CalendarEventIndexStore _indexStore;
    private readonly CalendarService _calendarService;
    private readonly ILambdaContext? _context;

    public GoogleCalendarPullService(
        ISyncStore syncStore,
        CalendarSyncStateStore stateStore,
        CalendarEventIndexStore indexStore,
        GoogleOAuthCredentials credentials,
        string refreshToken,
        ILambdaContext? context = null)
    {
        _syncStore = syncStore;
        _stateStore = stateStore;
        _indexStore = indexStore;
        _context = context;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = credentials.ClientId, ClientSecret = credentials.ClientSecret },
            Scopes = new[] { CalendarService.Scope.CalendarReadonly },
            DataStore = new NullDataStore()
        });
        var credential = new UserCredential(flow, "fullview", new TokenResponse { RefreshToken = refreshToken });

        _calendarService = new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "remarkable-fullview"
        });
    }

    public async Task PullAllAsync(IReadOnlyList<CalendarConfig> calendars, CancellationToken ct)
    {
        foreach (var calendar in calendars)
        {
            try
            {
                await PullOneAsync(calendar, ct);
            }
            catch (Exception ex)
            {
                // One misbehaving calendar (bad id, revoked share, transient API error)
                // must not stop the rest of the sweep from running.
                _context?.Logger.LogError($"Calendar pull failed for {calendar.Id}: {ex}");
            }
        }
    }

    private async Task PullOneAsync(CalendarConfig calendar, CancellationToken ct)
    {
        var syncToken = await _stateStore.GetSyncTokenAsync(calendar.Id, ct);
        var now = DateTimeOffset.UtcNow;

        Google.Apis.Calendar.v3.Data.Events page;
        try
        {
            page = await FetchPageAsync(calendar.Id, syncToken, now, pageToken: null, ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.Gone)
        {
            // Expired/invalid sync token — discard it and do one full re-fetch (Google's
            // documented recovery for a 410).
            await _stateStore.SaveSyncTokenAsync(calendar.Id, null, ct);
            page = await FetchPageAsync(calendar.Id, syncToken: null, now, pageToken: null, ct);
        }

        while (true)
        {
            foreach (var googleEvent in page.Items ?? Enumerable.Empty<Google.Apis.Calendar.v3.Data.Event>())
            {
                if (googleEvent.Status == "cancelled")
                {
                    var knownEntityId = googleEvent.Id is null
                        ? null
                        : await _indexStore.GetEntityIdAsync(calendar.Id, googleEvent.Id, ct);
                    var tombstone = GoogleEventMapper.Map(googleEvent, calendar.Id, calendar.Context, now, knownEntityId);
                    if (tombstone is not null)
                    {
                        await _syncStore.PutAsync(tombstone, ct);
                        await _indexStore.DeleteAsync(calendar.Id, googleEvent.Id!, ct);
                    }

                    continue;
                }

                var mapped = GoogleEventMapper.Map(googleEvent, calendar.Id, calendar.Context, now);
                if (mapped is not null)
                {
                    await _syncStore.PutAsync(mapped, ct);
                    if (googleEvent.Id is not null)
                    {
                        await _indexStore.SaveAsync(calendar.Id, googleEvent.Id, mapped.Id, ct);
                    }
                }
            }

            if (string.IsNullOrEmpty(page.NextPageToken))
            {
                break;
            }

            page = await FetchPageAsync(calendar.Id, syncToken, now, page.NextPageToken, ct);
        }

        if (!string.IsNullOrEmpty(page.NextSyncToken))
        {
            await _stateStore.SaveSyncTokenAsync(calendar.Id, page.NextSyncToken, ct);
        }
    }

    private Task<Google.Apis.Calendar.v3.Data.Events> FetchPageAsync(
        string calendarId, string? syncToken, DateTimeOffset now, string? pageToken, CancellationToken ct)
    {
        var request = _calendarService.Events.List(calendarId);
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        request.PageToken = pageToken;

        if (!string.IsNullOrEmpty(syncToken))
        {
            // TimeMin/TimeMax are invalid alongside SyncToken — Google's incremental sync
            // already scopes results to what changed, not to a time window.
            request.SyncToken = syncToken;
        }
        else
        {
            request.TimeMinDateTimeOffset = now - LookBack;
            request.TimeMaxDateTimeOffset = now + LookAhead;
        }

        return request.ExecuteAsync(ct);
    }

    /// <summary>The auth library wants a token cache; the Lambda has nothing worth caching
    /// across cold starts (a fresh access token is minted from the refresh token every
    /// invocation), so this is a no-op store.</summary>
    private sealed class NullDataStore : IDataStore
    {
        public Task ClearAsync() => Task.CompletedTask;
        public Task DeleteAsync<T>(string key) => Task.CompletedTask;
        public Task<T?> GetAsync<T>(string key) => Task.FromResult<T?>(default);
        public Task StoreAsync<T>(string key, T value) => Task.CompletedTask;
    }
}
