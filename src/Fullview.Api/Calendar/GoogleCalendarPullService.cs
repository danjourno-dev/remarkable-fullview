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

    /// <summary>How often to force a full (non-incremental) fetch purely to run
    /// <see cref="CalendarReconciler"/>, even when the incremental sync token is still
    /// valid. Google's token can stay valid for a long time, and per-event delta tracking
    /// (the "cancelled"/"moved" branches below) both key off Google's own event id — which
    /// the Work mirror's wipe-and-rebuild churn can change on every edit, silently orphaning
    /// rows that a delta would never surface. This is the backstop that catches those.</summary>
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromHours(6);

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
        var now = DateTimeOffset.UtcNow;
        var lastReconciled = await _stateStore.GetLastReconciledAsync(calendar.Id, ct);
        var dueForReconciliation = lastReconciled is null || now - lastReconciled.Value >= ReconciliationInterval;

        var syncToken = dueForReconciliation ? null : await _stateStore.GetSyncTokenAsync(calendar.Id, ct);
        var isFullFetch = syncToken is null;

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
            isFullFetch = true;
        }

        // Only a full fetch enumerates every currently-live event in the window, so only
        // then do we have a complete enough picture to safely diff against — an incremental
        // page is just a set of changes, not a live-vs-gone snapshot.
        var liveExternalIds = isFullFetch ? new HashSet<string>() : null;

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
                    // A moved event (changed start/end) keeps the same Google event id but
                    // mints a new content-derived entity id (BuildEntityId). Google never
                    // sends a "cancelled" notice for the old time slot in that case, so
                    // without this check the old row would never be tombstoned and would
                    // sync down to every client forever as a stale duplicate.
                    if (googleEvent.Id is not null)
                    {
                        var previousEntityId = await _indexStore.GetEntityIdAsync(calendar.Id, googleEvent.Id, ct);
                        if (previousEntityId is not null && previousEntityId != mapped.Id)
                        {
                            await _syncStore.PutAsync(
                                GoogleEventMapper.Tombstone(previousEntityId, googleEvent.Id, calendar.Context, now), ct);
                        }
                    }

                    await _syncStore.PutAsync(mapped, ct);
                    if (googleEvent.Id is not null)
                    {
                        await _indexStore.SaveAsync(calendar.Id, googleEvent.Id, mapped.Id, ct);
                        liveExternalIds?.Add(googleEvent.Id);
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

        if (liveExternalIds is not null)
        {
            var allEntities = await _syncStore.GetAllAsync(ct);
            var orphans = CalendarReconciler.FindOrphans(
                allEntities, calendar.Id, liveExternalIds, now - LookBack, now + LookAhead);

            foreach (var orphan in orphans)
            {
                await _syncStore.PutAsync(GoogleEventMapper.Tombstone(orphan.Id, orphan.ExternalId, calendar.Context, now), ct);
                if (orphan.ExternalId is not null)
                {
                    await _indexStore.DeleteAsync(calendar.Id, orphan.ExternalId, ct);
                }
            }

            await _stateStore.SaveLastReconciledAsync(calendar.Id, now, ct);
        }
    }

    private Task<Google.Apis.Calendar.v3.Data.Events> FetchPageAsync(
        string calendarId, string? syncToken, DateTimeOffset now, string? pageToken, CancellationToken ct)
    {
        var request = _calendarService.Events.List(calendarId);
        request.SingleEvents = true;
        request.PageToken = pageToken;

        if (!string.IsNullOrEmpty(syncToken))
        {
            // TimeMin/TimeMax/OrderBy are invalid alongside SyncToken — Google's incremental
            // sync already scopes results to what changed, not to a time window, and (per
            // Google's docs) omits nextSyncToken from the response entirely whenever OrderBy
            // is set, which would permanently prevent this service from ever re-entering
            // incremental mode.
            request.SyncToken = syncToken;
        }
        else
        {
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
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
