using Fullview.Domain;
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
    private const int HeaderSize = 32;
    private const int RowSize = 33;

    /// <summary>Public so device input code can convert a drag's pixel distance into a
    /// number of rows to scroll (see Program.cs's RunTouchLoop and QtfbInputSource).</summary>
    public const int RowHeight = 105;

    private const byte PastEventColor = 160;

    /// <summary>Converts a vertical drag distance (framebuffer pixels, positive = finger
    /// moved down the screen) into a row count for BoardAction.ScrollAgenda. Dragging up
    /// (negative fbDeltaY) should reveal later entries, hence the sign flip. Shared by both
    /// device input paths (Program.cs's RunTouchLoop for evdev, QtfbInputSource for AppLoad)
    /// so a swipe feels the same regardless of how the app was launched.</summary>
    public static int RowsForDrag(int fbDeltaY) => (int)Math.Round(-fbDeltaY / (double)RowHeight);

    public static ScreenRenderResult Render(
        int width, int height, IReadOnlyList<AgendaEvent> events, DateTimeOffset now, int scrollOffset = 0)
    {
        var image = new Image<L8>(width, height, new L8(Canvas.White));

        var headerFont = AppFont.Bold(HeaderSize);
        AppFont.DrawText(image, "AGENDA", Margin, Margin, headerFont, Canvas.Black);

        int y = Margin + AppFont.LineHeight(headerFont) + Margin;
        var ordered = events.OrderBy(e => e.IsAllDay ? 0 : 1).ThenBy(e => e.Start).ToList();

        // No more "+N more" truncation (B2's old rule) — the caller-supplied offset pages
        // through the full list instead, driven by a finger-drag on the device or the
        // clamped scroll state Program.cs keeps in BoardState.AgendaScrollOffset.
        int offset = Math.Clamp(scrollOffset, 0, ListPage.MaxScrollOffset(ordered.Count));
        var visible = ordered.Skip(offset).Take(ListPage.MaxVisibleItems).ToList();

        var rowFont = AppFont.Regular(RowSize);
        int rowWidth = width - 2 * Margin;

        if (visible.Count > 0)
        {
            Canvas.DrawDivider(image, Margin, y - 8, rowWidth);
        }

        foreach (var ev in visible)
        {
            string time = ev.IsAllDay ? "ALL DAY" : ev.Start.ToLocal().ToString("HH:mm");
            string marker = ev.Source == AgendaEventSource.GoogleCalendar ? "*" : "";
            string line = $"{time} {ev.Title}{marker}";
            byte color = !ev.IsAllDay && ev.End <= now ? PastEventColor : Canvas.Black;

            AppFont.DrawText(image, line, Margin, y, rowFont, color);
            y += RowHeight;
            Canvas.DrawDivider(image, Margin, y - 8, rowWidth);
        }

        return new ScreenRenderResult(image, Array.Empty<HitRegion>());
    }
}
