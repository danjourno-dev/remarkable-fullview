namespace Fullview.Rendering;

/// <summary>
/// Lightweight call-count/timing counters for the render hot paths (text drawing, rect fills)
/// — not wired into any product behavior, purely so <c>Fullview.Device</c> can log a
/// breakdown of where render time goes. <see cref="Reset"/> before a render pass, read the
/// totals after.
/// </summary>
public static class RenderDiagnostics
{
    public static int TextDrawCalls;
    public static long TextDrawTicks;
    public static int FillRectCalls;
    public static long FillRectTicks;

    public static void Reset()
    {
        TextDrawCalls = 0;
        TextDrawTicks = 0;
        FillRectCalls = 0;
        FillRectTicks = 0;
    }
}
