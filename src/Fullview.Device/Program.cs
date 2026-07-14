using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Fullview.Device;
using Fullview.Device.Capture;
using Fullview.Device.Input;
using Fullview.Device.Logging;
using Fullview.Device.Storage;
using Fullview.Device.Sync;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering;
using Fullview.Rendering.Layout;
using Fullview.Rendering.Screens;
using SixLabors.ImageSharp;

string deviceId = Environment.GetEnvironmentVariable("FULLVIEW_DEVICE_ID") ?? "device";
string? apiBaseUrl = Environment.GetEnvironmentVariable("FULLVIEW_API_BASE_URL");
string? apiKey = Environment.GetEnvironmentVariable("FULLVIEW_API_KEY");
string runMode = Environment.GetEnvironmentVariable("FULLVIEW_MODE") ?? "app";
// Stage 7: the Inbox notebook's own directory under xochitl's storage (see
// docs/device-setup.md's "Inbox capture" section), e.g.
// ~/.local/share/remarkable/xochitl/<notebookUuid> — containing that notebook's
// <pageUuid>.rm files directly. Unset disables capture scanning entirely (same "absent
// config = feature off" convention as FULLVIEW_API_BASE_URL for sync).
string? inboxNotebookPath = Environment.GetEnvironmentVariable("FULLVIEW_INBOX_NOTEBOOK_PATH");

string dbPath = Environment.GetEnvironmentVariable("FULLVIEW_DB_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "fullview.db");
string touchDevicePath = Environment.GetEnvironmentVariable("FULLVIEW_TOUCH_DEVICE")
    ?? Fullview.Device.Native.Evdev.DefaultTouchDevicePath;
string buttonDevicePath = Environment.GetEnvironmentVariable("FULLVIEW_BUTTON_DEVICE")
    ?? Fullview.Device.Native.Evdev.DefaultButtonDevicePath;
string version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? "dev";

Console.WriteLine($"Fullview.Device starting (pid {Environment.ProcessId}), version={version}, db={dbPath}.");

// Diagnostic-only, gated by ENABLE_LOGGING (docs/device-setup.md): if sync mysteriously
// doesn't happen on open, this is the first thing to check with `tail -f fullview.log` — a
// "(not set)" FULLVIEW_API_BASE_URL here despite /etc/fullview-sync.env looking right on disk
// almost always means run.sh's sourcing of that file isn't exporting it into the process
// environment, not a bug in the sync code itself.
DeviceLog.Debug(
    $"[env] FULLVIEW_DEVICE_ID={deviceId}, FULLVIEW_API_BASE_URL={apiBaseUrl ?? "(not set)"}, " +
    $"FULLVIEW_API_KEY={(string.IsNullOrEmpty(apiKey) ? "(not set)" : "(set)")}, FULLVIEW_MODE={runMode}, " +
    $"ENABLE_LOGGING={DeviceLog.Enabled}.");

using var database = DeviceDatabase.Open(dbPath);
var store = new DeviceStore(database);
var settings = new DeviceSettings(database);
var captureStore = new CaptureStore(database);

SeedData.ApplyIfEmpty(store);

// Cheap, local, no network — scan for changed Inbox pages before deciding whether either
// mode below actually has anything worth a network round-trip.
if (inboxNotebookPath is not null)
{
    int queued = InboxWatcher.ScanAndEnqueue(inboxNotebookPath, captureStore);
    if (queued > 0)
    {
        Console.WriteLine($"[capture] Queued {queued} changed Inbox page(s) for upload.");
    }
}

// Headless mode for the systemd RTC-wake timer (tools/device/systemd/fullview-sync.timer):
// no fb0/qtfb/evdev is touched here, so it can never contend with the AppLoad-launched
// foreground app for the framebuffer. Skips the network call entirely when there's nothing
// queued to push, to avoid waking the radio every 30 minutes for no reason.
if (string.Equals(runMode, "sync-once", StringComparison.OrdinalIgnoreCase))
{
    if (store.OutboxCount() == 0 && captureStore.OutboxCount() == 0)
    {
        Console.WriteLine("[sync] sync-once: outbox empty, nothing to push, skipping.");
        Environment.Exit(0);
    }

    if (apiBaseUrl is null)
    {
        Console.WriteLine("[sync] sync-once: FULLVIEW_API_BASE_URL not set, cannot sync.");
        Environment.Exit(1);
    }

    using var syncOnceHandler = CreateHttpHandler();
    using var syncOnceHttp = new HttpClient(syncOnceHandler) { BaseAddress = new Uri(apiBaseUrl!), Timeout = TimeSpan.FromSeconds(20) };
    AddApiKeyHeader(syncOnceHttp, apiKey);

    // Uploads any queued page bytes and queues their InboxPage entities into the normal
    // entity outbox first, so the SyncEngine drain immediately below pushes them in the
    // same run rather than waiting for a second sync-once wake.
    var syncOnceCaptureEngine = new CaptureUploadEngine(captureStore, store, new CaptureClient(syncOnceHttp), deviceId);
    syncOnceCaptureEngine.DrainAsync(CancellationToken.None).GetAwaiter().GetResult();

    var syncOnceEngine = new SyncEngine(store, settings, new SyncClient(syncOnceHttp));
    var syncOnceResult = syncOnceEngine.SyncOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
    Console.WriteLine($"[sync] sync-once: {syncOnceResult.Outcome}.");
    Environment.Exit(syncOnceResult.Outcome == SyncOutcome.Succeeded ? 0 : 1);
}

// Open the framebuffer and put "FullView" on the panel before anything network-blocking
// runs (the startup sync below can spend up to its 8s timeout), so launch feels instant
// instead of leaving the previous screen up while the app loads. AppLoad
// (tools/device/appload/external.manifest.json, qtfb: true) hands us a framebuffer key via
// QTFB_KEY when it launches us from the launcher; its absence means we were hand-launched
// over SSH and should fall back to driving /dev/fb0 and evdev directly.
string? qtfbKeyRaw = Environment.GetEnvironmentVariable("QTFB_KEY");
using IScreen fb = qtfbKeyRaw is { } raw && int.TryParse(raw, out int qtfbKey)
    ? QtfbScreen.Connect(qtfbKey)
    : FramebufferDevice.Open();
Console.WriteLine(fb is QtfbScreen
    ? $"Connected to qtfb (QTFB_KEY={qtfbKeyRaw}) — {fb.Width}x{fb.Height} RGB565."
    : $"Opened {FramebufferDevice.DevicePath} — {fb.Width}x{fb.Height}.");

using (var splash = SplashScreen.Render(fb.Width, fb.Height))
{
    fb.WriteImage(splash);
    // Fast (DU-class) full-panel refresh, not a GC16 one: the wordmark is pure black-on-white
    // with no grays, so it needs no grayscale waveform and DU puts it on the panel in a
    // fraction of a full refresh's time — the whole point of the splash is the quickest
    // possible "app launched" feedback. Any ghosting DU leaves is erased by fb.Flash() below.
    fb.RefreshRegion(new Rectangle(0, 0, fb.Width, fb.Height));
}
Console.WriteLine("Rendered splash screen.");

// A fresh read on every open (Stage 5): catches up on anything that changed via web/other
// devices while this one was closed. Deliberately NOT run inline here — doing so used to
// block the first board render on a full network round-trip (up to the 8s timeout below),
// leaving the splash up while the app "loaded". Instead the engines are wired up now and a
// SyncTick is enqueued once the input loop is running (see below), so the board paints
// immediately from the local cache (B2: stale is fine, never hidden — the footer reads the
// last-known sync time) and the catch-up sync runs on the first loop iteration, after the
// app is on screen. Short timeout so a slow/absent network doesn't stall that sync for long.
SyncEngine? syncEngine = null;
CaptureUploadEngine? captureEngine = null;
if (apiBaseUrl is not null)
{
    var syncHttp = new HttpClient(CreateHttpHandler()) { BaseAddress = new Uri(apiBaseUrl), Timeout = TimeSpan.FromSeconds(8) };
    AddApiKeyHeader(syncHttp, apiKey);
    captureEngine = new CaptureUploadEngine(captureStore, store, new CaptureClient(syncHttp), deviceId);
    syncEngine = new SyncEngine(store, settings, new SyncClient(syncHttp));
}
else
{
    Console.WriteLine("[sync] FULLVIEW_API_BASE_URL not set; sync disabled.");
}

var mode = settings.GetMode();
var state = new BoardState(
    Mode: mode,
    CurrentScreen: ScreenSet.NavigationOrder(mode)[0],
    OpenRecipeId: null,
    Todos: store.Query<Todo>(),
    AgendaEvents: store.Query<AgendaEvent>(),
    Meals: store.Query<Meal>(),
    ShoppingItems: store.Query<ShoppingItem>(),
    Recipes: store.Query<Recipe>(),
    InboxPages: store.Query<InboxPage>(),
    Now: DateTimeOffset.Now,
    LastSyncedAt: settings.GetLastSyncedAt(),
    PendingSyncCount: store.OutboxCount());

var lastRender = BoardRenderer.Render(fb.Width, fb.Height, state, version);

// Clear the splash's ghost before the board goes up: e-ink retains a faint image of the
// "FullView" wordmark that a single GC16 board refresh won't fully erase, leaving it visible
// behind the board. Driving the whole panel to solid black then solid white first (the
// standard e-ink de-ghost flash) resets every cell so the board lands on a clean field. Done
// after the board is rendered so the CPU render work overlaps the splash still being on
// screen, keeping the flash-to-board gap as short as possible.
fb.Flash();

fb.WriteImage(lastRender.Image);
LogRegions(lastRender);
fb.Refresh(fullRefresh: true);
Console.WriteLine("Rendered initial board and requested a full e-ink refresh.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Two producer threads (tap and hardware-button) feed one queue; the main thread is the only
// consumer, so it's the only place BoardState/lastRender ever mutate.
var inputs = new BlockingCollection<DeviceInput>();

// Not `using var`: touch/button are read from background threads for the life of the
// program, so they must only be disposed once those threads are done, at the very end below
// — disposing them as soon as this block exits would close the fds out from under
// RunTouchLoop/RunButtonLoop right after starting them.
EvdevDevice? touch = null;
EvdevDevice? button = null;

// Under qtfb, AppLoad pushes touch/pen/button input back over the same socket the screen
// connection uses, so evdev is never opened — it would just be reading devices xochitl is
// also reading, which is exactly the contention this migration is meant to avoid.
if (fb is QtfbScreen qtfbScreen)
{
    var qtfbInput = new QtfbInputSource(qtfbScreen);
    Console.WriteLine("Listening for qtfb input (Ctrl+C to exit).");
    var qtfbInputThread = new Thread(() => qtfbInput.Run(inputs, cts.Token)) { IsBackground = true };
    qtfbInputThread.Start();
}
else
{
    touch = EvdevDevice.Open(touchDevicePath);
    Console.WriteLine($"Opened touch device {touchDevicePath}. Listening for taps (Ctrl+C to exit).");
    var touchThread = new Thread(() => RunTouchLoop(touch, inputs, fb.Width, fb.Height, cts.Token)) { IsBackground = true };
    touchThread.Start();

    button = EvdevDevice.Open(buttonDevicePath);
    Console.WriteLine($"Opened hardware button device {buttonDevicePath}. Right button switches mode.");
    var buttonThread = new Thread(() => RunButtonLoop(button, inputs, cts.Token)) { IsBackground = true };
    buttonThread.Start();
}

// Keeps the on-device data converging with the server without waiting for the next app
// open/manual tap: reconnect (event-driven) fires almost immediately, the timer is the
// fallback for "wifi never dropped but the app's been open a while". Both are no-ops when
// sync is disabled (FULLVIEW_API_BASE_URL unset).
var lastNetworkTrigger = DateTimeOffset.MinValue;
if (syncEngine is not null)
{
    NetworkChange.NetworkAddressChanged += (_, _) =>
    {
        if (!NetworkInterface.GetIsNetworkAvailable())
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - lastNetworkTrigger < TimeSpan.FromSeconds(10))
        {
            return;
        }

        lastNetworkTrigger = now;
        DeviceLog.Debug("[sync] Network address changed and network is available; enqueuing background sync.");
        inputs.Add(new DeviceInput(DeviceInputKind.SyncTick, 0, 0));
    };
}

using var syncTimer = syncEngine is not null
    ? new Timer(
        _ =>
        {
            DeviceLog.Debug("[sync] Fallback timer fired; enqueuing background sync.");
            inputs.Add(new DeviceInput(DeviceInputKind.SyncTick, 0, 0));
        },
        null,
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(5))
    : null;

// Startup catch-up sync, now that the board is on screen and the loop is about to consume
// input: this replaces the old blocking startup sync. It's processed as the first loop
// iteration via the BackgroundSync path (capture drain + SyncOnceAsync, redraw only if
// something changed), so the network round-trip runs after the app has loaded and — like
// every other sync — on the main thread, never touching the SQLite store off-thread.
if (syncEngine is not null)
{
    DeviceLog.Debug("[sync] Enqueuing startup catch-up sync.");
    inputs.Add(new DeviceInput(DeviceInputKind.SyncTick, 0, 0));
}

foreach (var input in inputs.GetConsumingEnumerable(cts.Token))
{
    BoardAction? action = null;

    // Set when a tap flashed its region inverted: the flash's e-ink update is in flight while
    // the CPU work below (apply/render/diff) runs, and must be waited on before the final blit
    // overwrites those pixels — otherwise the flash may never become visible.
    Rectangle? flashRegion = null;
    uint flashMarker = 0;

    if (input.Kind == DeviceInputKind.HardwareButton)
    {
        action = new BoardAction.ToggleMode();
    }
    else if (input.Kind == DeviceInputKind.SyncTick)
    {
        action = new BoardAction.BackgroundSync();
    }
    else if (input.Kind == DeviceInputKind.Drag)
    {
        // Not a hit-region lookup — a drag can start/end anywhere on the panel, so the row
        // delta (stashed in Y by RunTouchLoop/QtfbInputSource) goes straight to the action.
        action = new BoardAction.ScrollAgenda(input.Y);
    }
    else
    {
        Console.WriteLine($"[debug] Tap mapped to fb ({input.X}, {input.Y}) — fb is {fb.Width}x{fb.Height}.");
        var hit = lastRender.Regions.FirstOrDefault(r => r.Contains(input.X, input.Y));
        if (hit is null)
        {
            Console.WriteLine("[debug] No region matched that tap.");
            continue;
        }

        Console.WriteLine($"[debug] Hit region {hit.Bounds} -> {hit.Action}.");

        action = hit.Action;

        // Close button: quit the app (back to the AppLoad launcher). Handled here rather than in
        // Apply — it changes no board state, it just ends the input loop and lets the graceful
        // shutdown below (dispose devices, "Exiting.") run.
        if (action is BoardAction.CloseApp)
        {
            Console.WriteLine("Close button tapped — exiting.");
            break;
        }

        // The action below (sync, toggle) can take long enough that the tap otherwise looks
        // ignored, so flash the tapped region inverted right away. The full re-render further
        // down always redraws this region back to its correct (non-inverted) state, so there's
        // no explicit "un-invert" step needed. Only the update *request* is sent here — the
        // wait for its physical completion happens just before the final blit below, so the
        // apply/render/diff CPU work runs concurrently with the panel's DU transition instead
        // of after it.
        if (action is BoardAction.SyncNow or BoardAction.ToggleTodo or BoardAction.ToggleShoppingItem)
        {
            Canvas.InvertRect(lastRender.Image, hit.Bounds.X, hit.Bounds.Y, hit.Bounds.Width, hit.Bounds.Height);
            fb.WriteImage(lastRender.Image, hit.Bounds);
            flashRegion = hit.Bounds;
            flashMarker = fb.BeginRefreshRegion(hit.Bounds);
        }
    }

    var swTotal = Stopwatch.StartNew();

    var swApply = Stopwatch.StartNew();
    var stateBeforeApply = state;
    state = Apply(action, state, store, settings, deviceId, syncEngine, captureEngine, captureStore, inboxNotebookPath, fb.Width, fb.Height);
    swApply.Stop();

    // BoardAction.BackgroundSync's Apply case returns the same state reference (no `with`)
    // when the sync failed or nothing changed — a cheap "nothing to redraw" signal that lets
    // a background tick skip the render/blit/refresh-ioctl below entirely. Every other action
    // (including SyncNow and all taps) always returns a new state and renders as before.
    if (ReferenceEquals(state, stateBeforeApply))
    {
        continue;
    }

    state = state with { LastSyncedAt = settings.GetLastSyncedAt(), PendingSyncCount = store.OutboxCount() };

    // Toggles used to sync inline inside Apply, which made the checkbox redraw wait on a full
    // HTTPS round-trip (up to the HttpClient's 8s timeout). Instead, queue a background sync
    // to run *after* this frame is on screen: the main thread is the only consumer of
    // `inputs`, so the SyncTick is processed on the next loop iteration, and BackgroundSync's
    // existing "same reference = nothing to redraw" contract keeps the footer's
    // pending-count/last-synced text converging one frame later.
    if (action is BoardAction.ToggleTodo or BoardAction.ToggleShoppingItem && syncEngine is not null)
    {
        inputs.Add(new DeviceInput(DeviceInputKind.SyncTick, 0, 0));
    }

    RenderDiagnostics.Reset();
    var swRender = Stopwatch.StartNew();
    var previousImage = lastRender.Image;
    lastRender = BoardRenderer.Render(fb.Width, fb.Height, state, version);
    swRender.Stop();
    double textMs = TimeSpan.FromTicks(RenderDiagnostics.TextDrawTicks).TotalMilliseconds;
    double fillRectMs = TimeSpan.FromTicks(RenderDiagnostics.FillRectTicks).TotalMilliseconds;
    double otherRenderMs = swRender.Elapsed.TotalMilliseconds - textMs - fillRectMs;
    Console.WriteLine(
        $"[debug] Render breakdown: text={RenderDiagnostics.TextDrawCalls} calls/{textMs:F1}ms, " +
        $"fillRect={RenderDiagnostics.FillRectCalls} calls/{fillRectMs:F1}ms, other={otherRenderMs:F1}ms.");

    // Diff against the previous frame and blit/refresh only the changed rectangle. The diff is
    // ground truth for what moved on screen: toggling completion re-sorts the panel (completed
    // items sink to the bottom), which can shift every row below the tapped one — not just
    // hitBounds — and the diff picks that up by construction. It also covers un-inverting the
    // tap flash, since InvertRect mutated previousImage in place before the render.
    var swBlit = Stopwatch.StartNew();
    var dirty = FrameDiff.DirtyRect(previousImage, lastRender.Image);
    previousImage.Dispose();
    if (dirty is null)
    {
        // Never expected when a flash was drawn (the inverted pixels always differ from the
        // re-render), but if it somehow happens the flash must still be undone — otherwise the
        // inverted region stays on the panel indefinitely.
        if (flashRegion is { } stale)
        {
            fb.WaitForRefresh(flashMarker);
            fb.WriteImage(lastRender.Image, stale);
            fb.RefreshRegion(stale);
        }

        Console.WriteLine("[debug] Frame is pixel-identical to the previous one — skipping blit and refresh.");
        continue;
    }

    // Hold the tap flash until its DU update has physically completed — everything between
    // BeginRefreshRegion and here (apply/render/diff) ran while the panel was transitioning,
    // so this wait only covers whatever transition time the CPU work didn't already absorb.
    // Timed outside swBlit: it's panel-transition remainder, not blit cost.
    long flashWaitMs = 0;
    if (flashRegion is not null)
    {
        swBlit.Stop();
        var swFlashWait = Stopwatch.StartNew();
        fb.WaitForRefresh(flashMarker);
        flashWaitMs = swFlashWait.ElapsedMilliseconds;
        swBlit.Start();
    }

    fb.WriteImage(lastRender.Image, dirty.Value);
    swBlit.Stop();

    LogRegions(lastRender);

    var swRefresh = Stopwatch.StartNew();
    fb.RefreshRegion(dirty.Value);
    swRefresh.Stop();

    swTotal.Stop();
    Console.WriteLine(
        $"[debug] Timing: db/apply={swApply.ElapsedMilliseconds}ms render={swRender.ElapsedMilliseconds}ms " +
        $"diff+blit={swBlit.ElapsedMilliseconds}ms (dirty rect {dirty.Value} of {fb.Width}x{fb.Height}) " +
        $"flash-wait={flashWaitMs}ms refresh-ioctl={swRefresh.ElapsedMilliseconds}ms " +
        $"total-app={swTotal.ElapsedMilliseconds}ms (excludes physical e-ink transition time, which " +
        "happens in hardware after refresh-ioctl returns).");
}

touch?.Dispose();
button?.Dispose();

Console.WriteLine("Exiting.");

// The reMarkable 1's system libssl.so.3 (OpenSSL 3.2.6, armv7l) has a native time_t
// ABI bug: X509_cmp_time's ASN1_TIME_to_tm parse fails ("format error in certificate's
// notBefore field") for every certificate in a chain, including long-issued roots,
// regardless of whether the cert is actually valid. `openssl s_client` against the same
// host at the same moment validates fine, and .NET's own *managed* NotBefore/NotAfter
// properties parse correctly — only the native chain-build time check is broken. Since
// we can't patch the device's system OpenSSL, re-check validity ourselves using the
// already-correctly-parsed managed properties, and only override the native chain
// status when NotTimeValid is the *only* problem reported and our own check confirms
// the cert is genuinely within its validity window. Any other chain error (untrusted
// root, revoked, name mismatch, tampering) still fails closed exactly as before.
static HttpClientHandler CreateHttpHandler()
{
    var debug = Environment.GetEnvironmentVariable("FULLVIEW_TLS_DEBUG") == "1";
    var handler = new HttpClientHandler();
    handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) =>
    {
        if (errors == System.Net.Security.SslPolicyErrors.None)
        {
            return true;
        }

        if (debug)
        {
            Console.WriteLine($"[tls-debug] SslPolicyErrors={errors}");
        }

        if (errors != System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors || chain is null)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var element in chain.ChainElements)
        {
            foreach (var status in element.ChainElementStatus)
            {
                if (status.Status != X509ChainStatusFlags.NotTimeValid)
                {
                    return false;
                }
            }

            var notBefore = new DateTimeOffset(element.Certificate.NotBefore.ToUniversalTime());
            var notAfter = new DateTimeOffset(element.Certificate.NotAfter.ToUniversalTime());
            if (now < notBefore || now > notAfter)
            {
                return false;
            }
        }

        if (debug)
        {
            Console.WriteLine("[tls-debug] Overriding native NotTimeValid: every cert's managed-parsed NotBefore/NotAfter covers the current time.");
        }

        return true;
    };
    return handler;
}

// Single-user v1 auth (see docs/plans/implementation.md): the API Gateway authorizer checks
// this same header. A missing/wrong key makes every /sync call fail with 401, which
// SyncEngine.SyncOnceAsync already treats like any other network failure (outbox and cursor
// left untouched for the next retry) — so there's nothing special to handle here beyond
// attaching the header when one is configured.
static void AddApiKeyHeader(HttpClient http, string? apiKey)
{
    if (!string.IsNullOrEmpty(apiKey))
    {
        http.DefaultRequestHeaders.Add("x-api-key", apiKey);
    }
    else
    {
        Console.WriteLine("[sync] FULLVIEW_API_KEY not set; /sync calls will be rejected by the API.");
    }
}

// Re-scans the Inbox notebook for pages changed since app open (e.g. a page written earlier
// in this same session) and drains any queued uploads, immediately before every full sync
// cycle — same fire-now-fail-silent contract as the rest of this file: a failed upload just
// leaves the capture outbox for the next trigger to retry.
static void ScanAndDrainCapture(string? inboxNotebookPath, CaptureStore captureStore, CaptureUploadEngine? captureEngine)
{
    if (inboxNotebookPath is null || captureEngine is null)
    {
        return;
    }

    InboxWatcher.ScanAndEnqueue(inboxNotebookPath, captureStore);
    captureEngine.DrainAsync(CancellationToken.None).GetAwaiter().GetResult();
}

static void LogRegions(ScreenRenderResult render)
{
    Console.WriteLine($"[debug] {render.Regions.Count} hit region(s):");
    foreach (var region in render.Regions)
    {
        Console.WriteLine($"[debug]   {region.Bounds} -> {region.Action}");
    }
}

// The reMarkable 1's capacitive touch digitizer (cyttsp5_mt, /dev/input/event2) reports
// ABS_MT_POSITION_X/Y in its own native coordinate space — 0-767 by 0-1023. That range has
// the same aspect ratio as the framebuffer (1404x1872), so the axes are NOT swapped — the
// panel is instead mounted rotated 180 degrees relative to the display, so both axes must be
// inverted (max-minus-value) before rescaling to framebuffer pixels. Confirmed against a
// physical device: raw (554, 401) — a tap on the first reminder row — only lands in that
// row's region (fb Y 1104-1194) when both axes are flipped; an axis swap (the previous,
// unverified assumption) put it three rows further down.
const int TouchMaxX = 767;
const int TouchMaxY = 1023;

static void RunTouchLoop(
    EvdevDevice touch, BlockingCollection<DeviceInput> inputs, int fbWidth, int fbHeight, CancellationToken token)
{
    var detector = new TouchTapDetector();
    while (!token.IsCancellationRequested)
    {
        RawInputEvent evt;
        try
        {
            evt = touch.ReadNext();
        }
        catch (IOException) when (token.IsCancellationRequested)
        {
            return;
        }

        var tap = detector.Feed(evt);
        if (tap is { } t)
        {
            int fbX = (TouchMaxX - t.X) * fbWidth / TouchMaxX;
            int fbY = (TouchMaxY - t.Y) * fbHeight / TouchMaxY;
            Console.WriteLine($"[debug] Raw touch ({t.X}, {t.Y}) of {TouchMaxX}x{TouchMaxY} -> fb ({fbX}, {fbY}).");
            inputs.Add(new DeviceInput(DeviceInputKind.Tap, fbX, fbY));
        }

        if (detector.TakeDrag() is { } drag)
        {
            // Both touch axes are inverted (see the 180-degree-mounting comment above), so a
            // raw DeltaY increase corresponds to a *decrease* in fb-space Y, same as the
            // per-axis rescale taps go through. The pixel delta (not a row count) is passed
            // through — Apply() knows which screen is current and converts to rows using that
            // screen's row height.
            int fbDeltaY = -drag.DeltaY * fbHeight / TouchMaxY;
            Console.WriteLine($"[debug] Raw drag deltaY={drag.DeltaY} -> fb deltaY={fbDeltaY}.");
            if (fbDeltaY != 0)
            {
                inputs.Add(new DeviceInput(DeviceInputKind.Drag, 0, fbDeltaY));
            }
        }
    }
}

static void RunButtonLoop(EvdevDevice button, BlockingCollection<DeviceInput> inputs, CancellationToken token)
{
    while (!token.IsCancellationRequested)
    {
        RawInputEvent evt;
        try
        {
            evt = button.ReadNext();
        }
        catch (IOException) when (token.IsCancellationRequested)
        {
            return;
        }

        // value == 1 is key-down; ignore the key-up (0) and autorepeat (2) events.
        if (evt.Type == EvCodes.EV_KEY && evt.Code == EvCodes.KEY_RIGHT && evt.Value == 1)
        {
            inputs.Add(new DeviceInput(DeviceInputKind.HardwareButton, 0, 0));
        }
    }
}

static BoardState Apply(
    BoardAction action, BoardState state, DeviceStore store, DeviceSettings settings,
    string deviceId, SyncEngine? syncEngine, CaptureUploadEngine? captureEngine, CaptureStore captureStore,
    string? inboxNotebookPath, int fbWidth, int fbHeight)
{
    switch (action)
    {
        case BoardAction.ToggleMode:
            var newMode = state.Mode == SyncContext.Personal ? SyncContext.Work : SyncContext.Personal;
            settings.SetMode(newMode);
            return state.WithMode(newMode) with { Now = DateTimeOffset.Now };

        case BoardAction.NavigatePrevious:
            return state.WithScreen(ScreenSet.Previous(state.Mode, state.CurrentScreen)) with { Now = DateTimeOffset.Now };

        case BoardAction.NavigateNext:
            return state.WithScreen(ScreenSet.Next(state.Mode, state.CurrentScreen)) with { Now = DateTimeOffset.Now };

        case BoardAction.NavigateToScreen(var screen):
            return state.WithScreen(screen) with { Now = DateTimeOffset.Now };

        // No inline sync here: toggles are the highest-value thing to get off the device
        // quickly, but the push must not block the redraw — the main loop enqueues a SyncTick
        // right after this frame renders (see the comment there), so the drain still
        // piggybacks on the same tap, just one loop iteration later.
        case BoardAction.ToggleTodo(var todoId):
            store.ToggleTodoCompleted(todoId, deviceId);
            return state with { Todos = store.Query<Todo>(), Now = DateTimeOffset.Now };

        case BoardAction.ToggleShoppingItem(var itemId):
            store.ToggleShoppingItemChecked(itemId, deviceId);
            return state with { ShoppingItems = store.Query<ShoppingItem>(), Now = DateTimeOffset.Now };

        case BoardAction.OpenRecipe(var recipeId):
            return state.WithOpenRecipe(recipeId) with { Now = DateTimeOffset.Now };

        // Ignored off the Agenda screen — a drag can't currently target any other screen,
        // but this keeps Apply total if that ever changes. Matches BackgroundSync's "same
        // reference = nothing to redraw" convention below when the offset doesn't move
        // (e.g. dragging further at either end of the list).
        case BoardAction.ScrollAgenda(var pixelDeltaY):
            if (state.CurrentScreen == ScreenKind.Agenda)
            {
                int rowDelta = AgendaScreen.RowsForDrag(pixelDeltaY);
                int maxOffset = ListPage.MaxScrollOffset(BoardRenderer.AgendaEntryCount(state));
                int newOffset = Math.Clamp(state.AgendaScrollOffset + rowDelta, 0, maxOffset);
                return newOffset == state.AgendaScrollOffset
                    ? state
                    : state with { AgendaScrollOffset = newOffset, Now = DateTimeOffset.Now };
            }

            if (state.CurrentScreen == ScreenKind.Today)
            {
                int rowDelta = TodayScreen.RowsForDrag(pixelDeltaY);
                int panelHeight = TodayScreen.AgendaPanelHeight(fbHeight);
                int capacity = TodayScreen.AgendaCapacity(panelHeight);
                int maxTodayOffset = Math.Max(0, BoardRenderer.AgendaEntryCount(state) - capacity);
                int newTodayOffset = Math.Clamp(state.TodayAgendaScrollOffset + rowDelta, 0, maxTodayOffset);
                return newTodayOffset == state.TodayAgendaScrollOffset
                    ? state
                    : state with { TodayAgendaScrollOffset = newTodayOffset, Now = DateTimeOffset.Now };
            }

            return state;

        case BoardAction.SyncNow:
            if (syncEngine is null)
            {
                Console.WriteLine("[sync] Manual sync tapped but FULLVIEW_API_BASE_URL not set; ignoring.");
                return state with { Now = DateTimeOffset.Now };
            }

            ScanAndDrainCapture(inboxNotebookPath, captureStore, captureEngine);
            var manualResult = syncEngine.SyncOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"[sync] Manual sync: {manualResult.Outcome}.");
            return state with
            {
                Todos = store.Query<Todo>(),
                AgendaEvents = store.Query<AgendaEvent>(),
                Meals = store.Query<Meal>(),
                ShoppingItems = store.Query<ShoppingItem>(),
                Recipes = store.Query<Recipe>(),
                InboxPages = store.Query<InboxPage>(),
                Now = DateTimeOffset.Now
            };

        // Per B2 ("stale is fine, never hidden"): a failed or no-op background sync just
        // leaves state as-is (same reference — see the main loop's ReferenceEquals check),
        // same as today's startup-sync failure handling. No error surfaced beyond the
        // footer's existing pending/last-synced text.
        case BoardAction.BackgroundSync:
            if (syncEngine is null)
            {
                return state;
            }

            ScanAndDrainCapture(inboxNotebookPath, captureStore, captureEngine);
            var bgResult = syncEngine.SyncOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
            DeviceLog.Debug($"[sync] Background sync: {bgResult.Outcome}, changed={bgResult.Changed}.");
            if (bgResult.Outcome == SyncOutcome.Failed || !bgResult.Changed)
            {
                return state;
            }

            return state with
            {
                Todos = store.Query<Todo>(),
                AgendaEvents = store.Query<AgendaEvent>(),
                Meals = store.Query<Meal>(),
                ShoppingItems = store.Query<ShoppingItem>(),
                Recipes = store.Query<Recipe>(),
                InboxPages = store.Query<InboxPage>(),
                Now = DateTimeOffset.Now
            };

        default:
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown board action.");
    }
}

internal enum DeviceInputKind
{
    Tap,
    HardwareButton,
    SyncTick,
    Drag
}

internal readonly record struct DeviceInput(DeviceInputKind Kind, int X, int Y);
