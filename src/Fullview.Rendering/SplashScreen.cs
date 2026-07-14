using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering;

/// <summary>
/// The first thing on the panel at launch: a plain white screen with "FullView"
/// centered in a large bold font. It is drawn and refreshed before the (network-blocking)
/// startup sync and the real board render run, so the user sees the app respond immediately
/// instead of staring at whatever was left on the e-ink panel while everything loads.
/// Deliberately trivial — no board state, no fonts beyond the wordmark — so it can be
/// produced with the least possible work between process start and first blit.
/// </summary>
public static class SplashScreen
{
    private const byte White = 255;
    private const byte Black = 0;
    private const string Wordmark = "FullView";

    public static Image<L8> Render(int width, int height)
    {
        var image = new Image<L8>(width, height, new L8(White));

        float size = Math.Max(48, width / 7f);
        var font = AppFont.Bold(size);
        int textWidth = AppFont.MeasureWidth(Wordmark, font);
        int textHeight = AppFont.LineHeight(font);
        int x = Math.Max(0, (width - textWidth) / 2);
        int y = Math.Max(0, (height - textHeight) / 2);

        AppFont.DrawText(image, Wordmark, x, y, font, Black);

        return image;
    }
}
