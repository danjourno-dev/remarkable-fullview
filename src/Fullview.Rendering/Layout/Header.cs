using Fullview.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Top title bar (mockup v4): board name for the current mode ("LIFE OPS"/"WORK OPS") plus a
/// date/inbox subtitle. Sits above <see cref="NowNextStrip"/> on every screen. The subtitle's
/// "SWIPE FROM TOP TO WRITE" segment is a static hint, not a tap target — AppLoad has no
/// programmatic way to switch a running external app to xochitl (confirmed by reading its
/// source), so capturing to the Inbox notebook is a manual drag-from-top-center-to-center
/// gesture that only AppLoad's own window chrome recognises. See PROGRESS.md's Stage 7
/// amendment decision.
/// </summary>
public static class Header
{
    public const int Height = 130;

    private const int Margin = 20;
    private const int TitleSize = 40;
    private const int SubtitleSize = 18;
    private const byte Black = Canvas.Black;

    // Header content only depends on (mode, date, inboxStatus), all of which stay constant across
    // most taps (e.g. toggling a todo) — cache the rendered bitmap and skip re-rasterizing text
    // when nothing here actually changed.
    private static (SyncContext Mode, DateOnly Date, string InboxStatus)? _cacheKey;
    private static Image<L8>? _cache;

    /// <summary>Renders the header as its own <see cref="Height"/>-tall bitmap, reusing the
    /// cached bitmap when (mode, date, inboxStatus) are unchanged since the last render.</summary>
    public static Image<L8> Render(int width, SyncContext mode, DateOnly date, string inboxStatus)
    {
        var key = (mode, date, inboxStatus);
        if (_cache is { } cached && cached.Width == width && _cacheKey == key)
        {
            return cached;
        }

        var image = new Image<L8>(width, Height, new L8(Canvas.White));
        Canvas.DrawFrame(image, 0, 0, width, Height);

        int innerX = Margin + 14;
        int titleY = Margin + 10;
        string title = mode == SyncContext.Work ? "WORK OPS" : "LIFE OPS";
        var titleFont = AppFont.Bold(TitleSize);
        AppFont.DrawText(image, title, innerX, titleY, titleFont, Black);

        string subtitle = $"{DayName(date.DayOfWeek)} {date.Day:00} {MonthName(date.Month)}  //  INBOX {inboxStatus}  //  SWIPE FROM TOP TO WRITE";
        var subtitleFont = AppFont.Regular(SubtitleSize);
        int subtitleY = Height - Margin - AppFont.LineHeight(subtitleFont) - 8;
        AppFont.DrawText(image, subtitle, innerX, subtitleY, subtitleFont, Black);

        _cache?.Dispose();
        _cache = image;
        _cacheKey = key;
        return image;
    }

    private static string DayName(DayOfWeek day) => day.ToString()[..3].ToUpperInvariant();

    private static string MonthName(int month) => new DateOnly(2000, month, 1).ToString("MMM").ToUpperInvariant();
}
