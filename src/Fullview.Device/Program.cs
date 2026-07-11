using System.Collections.Concurrent;
using Fullview.Device;
using Fullview.Device.Input;
using Fullview.Device.Storage;
using Fullview.Domain;
using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;

const string DeviceId = "device";

string dbPath = Environment.GetEnvironmentVariable("FULLVIEW_DB_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "fullview.db");
string touchDevicePath = Environment.GetEnvironmentVariable("FULLVIEW_TOUCH_DEVICE")
    ?? Fullview.Device.Native.Evdev.DefaultTouchDevicePath;
string buttonDevicePath = Environment.GetEnvironmentVariable("FULLVIEW_BUTTON_DEVICE")
    ?? Fullview.Device.Native.Evdev.DefaultButtonDevicePath;

Console.WriteLine($"Fullview.Device starting (pid {Environment.ProcessId}), db={dbPath}.");

using var database = DeviceDatabase.Open(dbPath);
var store = new DeviceStore(database);
var settings = new DeviceSettings(database);

SeedData.ApplyIfEmpty(store);

using var fb = FramebufferDevice.Open();
Console.WriteLine(
    $"Opened {FramebufferDevice.DevicePath} — {fb.Width}x{fb.Height}, {fb.BitsPerPixel}bpp, stride {fb.Stride} bytes.");

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
    Now: DateTimeOffset.Now);

var lastRender = BoardRenderer.Render(fb.Width, fb.Height, state);
fb.WriteImage(lastRender.Image);
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

using var touch = EvdevDevice.Open(touchDevicePath);
Console.WriteLine($"Opened touch device {touchDevicePath}. Listening for taps (Ctrl+C to exit).");
var touchThread = new Thread(() => RunTouchLoop(touch, inputs, cts.Token)) { IsBackground = true };
touchThread.Start();

using var button = EvdevDevice.Open(buttonDevicePath);
Console.WriteLine($"Opened hardware button device {buttonDevicePath}. Right button switches mode.");
var buttonThread = new Thread(() => RunButtonLoop(button, inputs, cts.Token)) { IsBackground = true };
buttonThread.Start();

foreach (var input in inputs.GetConsumingEnumerable(cts.Token))
{
    BoardAction? action = null;
    Rectangle? hitBounds = null;

    if (input.Kind == DeviceInputKind.HardwareButton)
    {
        action = new BoardAction.ToggleMode();
    }
    else
    {
        Console.WriteLine($"[debug] Tap at ({input.X}, {input.Y}) — fb is {fb.Width}x{fb.Height}.");
        var hit = lastRender.Regions.FirstOrDefault(r => r.Contains(input.X, input.Y));
        if (hit is null)
        {
            Console.WriteLine("[debug] No region matched that tap.");
            continue;
        }

        action = hit.Action;
        hitBounds = hit.Bounds;
    }

    state = Apply(action, state, store, settings);
    lastRender = BoardRenderer.Render(fb.Width, fb.Height, state);
    fb.WriteImage(lastRender.Image);

    var refreshRegion = action is BoardAction.ToggleTodo or BoardAction.ToggleShoppingItem
        ? hitBounds!.Value
        : new Rectangle(0, 0, fb.Width, fb.Height);
    fb.RefreshRegion(refreshRegion);
}

Console.WriteLine("Exiting.");

static void RunTouchLoop(EvdevDevice touch, BlockingCollection<DeviceInput> inputs, CancellationToken token)
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
            inputs.Add(new DeviceInput(DeviceInputKind.Tap, t.X, t.Y));
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

static BoardState Apply(BoardAction action, BoardState state, DeviceStore store, DeviceSettings settings)
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
            store.ToggleTodoCompleted(todoId, DeviceId);
            return state with { Todos = store.Query<Todo>(), Now = DateTimeOffset.Now };

        case BoardAction.ToggleShoppingItem(var itemId):
            store.ToggleShoppingItemChecked(itemId, DeviceId);
            return state with { ShoppingItems = store.Query<ShoppingItem>(), Now = DateTimeOffset.Now };

        case BoardAction.OpenRecipe(var recipeId):
            return state.WithOpenRecipe(recipeId) with { Now = DateTimeOffset.Now };

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
