using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Fullview.Device;
using Fullview.Device.Input;
using Fullview.Device.Storage;
using Fullview.Device.Sync;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;

string deviceId = Environment.GetEnvironmentVariable("FULLVIEW_DEVICE_ID") ?? "device";
string? apiBaseUrl = Environment.GetEnvironmentVariable("FULLVIEW_API_BASE_URL");
string runMode = Environment.GetEnvironmentVariable("FULLVIEW_MODE") ?? "app";

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

using var database = DeviceDatabase.Open(dbPath);
var store = new DeviceStore(database);
var settings = new DeviceSettings(database);

SeedData.ApplyIfEmpty(store);

// Headless mode for the systemd RTC-wake timer (tools/device/systemd/fullview-sync.timer):
// no fb0/qtfb/evdev is touched here, so it can never contend with the AppLoad-launched
// foreground app for the framebuffer. Skips the network call entirely when there's nothing
// queued to push, to avoid waking the radio every 30 minutes for no reason.
if (string.Equals(runMode, "sync-once", StringComparison.OrdinalIgnoreCase))
{
    if (store.OutboxCount() == 0)
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
    var syncOnceEngine = new SyncEngine(store, settings, new SyncClient(syncOnceHttp), deviceId);
    var syncOnceOutcome = syncOnceEngine.SyncOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
    Console.WriteLine($"[sync] sync-once: {syncOnceOutcome}.");
    Environment.Exit(syncOnceOutcome == SyncOutcome.Succeeded ? 0 : 1);
}

// A fresh read on every open (Stage 5): catches up on anything that changed via web/other
// devices while this one was closed. Short timeout so a slow/absent network doesn't stall
// startup for long — a failed attempt just leaves the local cache as-is (B2: stale is fine,
// never hidden) and the footer will read "NOT SYNCED" or the last-known time.
SyncEngine? syncEngine = null;
if (apiBaseUrl is not null)
{
    var syncHttp = new HttpClient(CreateHttpHandler()) { BaseAddress = new Uri(apiBaseUrl), Timeout = TimeSpan.FromSeconds(8) };
    syncEngine = new SyncEngine(store, settings, new SyncClient(syncHttp), deviceId);
    var startupOutcome = syncEngine.SyncOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
    Console.WriteLine($"[sync] Startup sync: {startupOutcome}.");
}
else
{
    Console.WriteLine("[sync] FULLVIEW_API_BASE_URL not set; sync disabled.");
}

// AppLoad (tools/device/appload/external.manifest.json, qtfb: true) hands us a framebuffer
// key via QTFB_KEY when it launches us from the launcher; its absence means we were
// hand-launched over SSH and should fall back to driving /dev/fb0 and evdev directly.
string? qtfbKeyRaw = Environment.GetEnvironmentVariable("QTFB_KEY");
using IScreen fb = qtfbKeyRaw is { } raw && int.TryParse(raw, out int qtfbKey)
    ? QtfbScreen.Connect(qtfbKey)
    : FramebufferDevice.Open();
Console.WriteLine(fb is QtfbScreen
    ? $"Connected to qtfb (QTFB_KEY={qtfbKeyRaw}) — {fb.Width}x{fb.Height} RGB565."
    : $"Opened {FramebufferDevice.DevicePath} — {fb.Width}x{fb.Height}.");

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

foreach (var input in inputs.GetConsumingEnumerable(cts.Token))
{
    BoardAction? action = null;

    if (input.Kind == DeviceInputKind.HardwareButton)
    {
        action = new BoardAction.ToggleMode();
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
    }

    var swTotal = Stopwatch.StartNew();

    var swApply = Stopwatch.StartNew();
    state = Apply(action, state, store, settings, deviceId, syncEngine);
    state = state with { LastSyncedAt = settings.GetLastSyncedAt(), PendingSyncCount = store.OutboxCount() };
    swApply.Stop();

    RenderDiagnostics.Reset();
    var swRender = Stopwatch.StartNew();
    lastRender = BoardRenderer.Render(fb.Width, fb.Height, state, version);
    swRender.Stop();
    double textMs = TimeSpan.FromTicks(RenderDiagnostics.TextDrawTicks).TotalMilliseconds;
    double fillRectMs = TimeSpan.FromTicks(RenderDiagnostics.FillRectTicks).TotalMilliseconds;
    double otherRenderMs = swRender.Elapsed.TotalMilliseconds - textMs - fillRectMs;
    Console.WriteLine(
        $"[debug] Render breakdown: text={RenderDiagnostics.TextDrawCalls} calls/{textMs:F1}ms, " +
        $"fillRect={RenderDiagnostics.FillRectCalls} calls/{fillRectMs:F1}ms, other={otherRenderMs:F1}ms.");

    var swBlit = Stopwatch.StartNew();
    fb.WriteImage(lastRender.Image);
    swBlit.Stop();

    LogRegions(lastRender);

    // Toggling completion re-sorts the panel (completed items sink to the bottom), which can
    // shift every row below the tapped one — not just hitBounds — so a full refresh is needed
    // to avoid leaving stale/duplicate-looking rows from the old sort order on screen.
    var swRefresh = Stopwatch.StartNew();
    fb.RefreshRegion(new Rectangle(0, 0, fb.Width, fb.Height));
    swRefresh.Stop();

    swTotal.Stop();
    Console.WriteLine(
        $"[debug] Timing: db/apply={swApply.ElapsedMilliseconds}ms render={swRender.ElapsedMilliseconds}ms " +
        $"blit={swBlit.ElapsedMilliseconds}ms refresh-ioctl={swRefresh.ElapsedMilliseconds}ms " +
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

// Toggles are the highest-value thing to get off the device quickly (they're what a
// caregiver checking the web view actually cares about), so we piggyback a drain attempt
// on the same request/render cycle instead of waiting for the next app-open or timer tick.
// Same fire-now-fail-silent contract as every other SyncOnceAsync call site: a failure just
// leaves the outbox for the next trigger to retry, never blocks or surfaces an error to the
// user beyond the footer's existing pending-count/last-synced display.
static void TryImmediateSync(SyncEngine? syncEngine)
{
    if (syncEngine is null)
    {
        return;
    }

    var outcome = syncEngine.SyncOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
    Console.WriteLine($"[sync] Post-toggle sync: {outcome}.");
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
    string deviceId, SyncEngine? syncEngine)
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

        case BoardAction.ToggleTodo(var todoId):
            store.ToggleTodoCompleted(todoId, deviceId);
            TryImmediateSync(syncEngine);
            return state with { Todos = store.Query<Todo>(), Now = DateTimeOffset.Now };

        case BoardAction.ToggleShoppingItem(var itemId):
            store.ToggleShoppingItemChecked(itemId, deviceId);
            TryImmediateSync(syncEngine);
            return state with { ShoppingItems = store.Query<ShoppingItem>(), Now = DateTimeOffset.Now };

        case BoardAction.OpenRecipe(var recipeId):
            return state.WithOpenRecipe(recipeId) with { Now = DateTimeOffset.Now };

        case BoardAction.SyncNow:
            if (syncEngine is null)
            {
                Console.WriteLine("[sync] Manual sync tapped but FULLVIEW_API_BASE_URL not set; ignoring.");
                return state with { Now = DateTimeOffset.Now };
            }

            var outcome = syncEngine.SyncOnceAsync(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine($"[sync] Manual sync: {outcome}.");
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
    HardwareButton
}

internal readonly record struct DeviceInput(DeviceInputKind Kind, int X, int Y);
