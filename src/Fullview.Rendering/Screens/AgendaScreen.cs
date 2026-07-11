using Fullview.Domain.Entities;
using Fullview.Rendering.Layout;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Screens;

/// <summary>
/// Agenda screen (Work mode's native calendar view). Pulled events (Source=GoogleCalendar)
/// render with a subtle marker and no tap-to-edit hit region (B5) — this screen is read-only
/// for both native and pulled events in v1; editing happens on web.
/// </summary>
public static class AgendaScreen
{
    private const int Margin = 24;
    private const int HeaderScale = 4;
    private const int RowScale = 3;
    private const int RowHeight = 70;

    public static ScreenRenderResult Render(int width, int height, IReadOnlyList<AgendaEvent> events)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));

        BitmapFont.DrawText(image, "AGENDA", Margin, Margin, HeaderScale, Canvas.Black);

        int y = Margin + BitmapFont.GlyphHeight * HeaderScale + Margin;
        var ordered = events.OrderBy(e => e.IsAllDay ? 0 : 1).ThenBy(e => e.Start).ToList();
        var (visible, overflow) = ListPage.Paginate(ordered);

        foreach (var ev in visible)
        {
            string time = ev.IsAllDay ? "ALL DAY" : ev.Start.ToLocalTime().ToString("HH:mm");
            string marker = ev.Source == AgendaEventSource.GoogleCalendar ? "*" : "";
            string line = $"{time} {ev.Title}{marker}";
            BitmapFont.DrawText(image, line, Margin, y, RowScale, Canvas.Black);
            y += RowHeight;
        }

        if (overflow > 0)
        {
            BitmapFont.DrawText(image, $"+{overflow} MORE", Margin, y, RowScale, Canvas.Black);
        }

        return new ScreenRenderResult(image, Array.Empty<HitRegion>());
    }
}
