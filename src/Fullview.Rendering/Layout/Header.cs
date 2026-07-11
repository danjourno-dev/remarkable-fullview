using Fullview.Domain;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>
/// Top title bar (mockup v4): board name for the current mode ("LIFE OPS"/"WORK OPS") plus a
/// date/inbox subtitle. Sits above <see cref="NowNextStrip"/> on every screen.
/// </summary>
public static class Header
{
    public const int Height = 130;

    private const int Margin = 20;
    private const int TitleScale = 5;
    private const int SubtitleScale = 2;
    private const byte Black = Canvas.Black;

    public static void Draw(Image<L8> image, SyncContext mode, DateOnly date, string inboxStatus)
    {
        Canvas.DrawFrame(image, 0, 0, image.Width, Height);

        int innerX = Margin + 14;
        int titleY = Margin + 10;
        string title = mode == SyncContext.Work ? "WORK OPS" : "LIFE OPS";
        BitmapFont.DrawText(image, title, innerX, titleY, TitleScale, Black);

        string subtitle = $"{DayName(date.DayOfWeek)} {date.Day:00} {MonthName(date.Month)}  //  INBOX {inboxStatus}";
        int subtitleY = Height - Margin - BitmapFont.GlyphHeight * SubtitleScale - 8;
        BitmapFont.DrawText(image, subtitle, innerX, subtitleY, SubtitleScale, Black);
    }

    private static string DayName(DayOfWeek day) => day.ToString()[..3].ToUpperInvariant();

    private static string MonthName(int month) => new DateOnly(2000, month, 1).ToString("MMM").ToUpperInvariant();
}
