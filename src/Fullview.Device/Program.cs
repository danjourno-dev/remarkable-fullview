using Fullview.Device;
using Fullview.Rendering;

Console.WriteLine($"Fullview.Device hello-world starting (pid {Environment.ProcessId}).");

using var fb = FramebufferDevice.Open();
Console.WriteLine(
    $"Opened {FramebufferDevice.DevicePath} — {fb.Width}x{fb.Height}, {fb.BitsPerPixel}bpp, stride {fb.Stride} bytes.");

using (var image = HelloWorldScreen.Render(fb.Width, fb.Height))
{
    fb.WriteImage(image);
}

fb.Refresh(fullRefresh: true);
Console.WriteLine("Rendered hello-world and requested a full e-ink refresh.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("Staying alive for 60s so RSS/free can be inspected (Checkpoint 3.2) — Ctrl+C to exit early.");
try
{
    await Task.Delay(TimeSpan.FromSeconds(60), cts.Token);
}
catch (TaskCanceledException)
{
    // Ctrl+C — fall through to normal exit.
}

Console.WriteLine("Exiting.");
