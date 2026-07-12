using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering;

/// <summary>
/// Stage 3's derisking render: white background, black border, "HELLO DAN"
/// centered. Deliberately dumb — it exists to prove the fb0/mxcfb pipeline
/// works, not to look like the final product (Stage 4 owns real screens).
/// </summary>
public static class HelloWorldScreen
{
    private const byte White = 255;
    private const byte Black = 0;

    public static Image<L8> Render(int width, int height)
    {
        var image = new Image<L8>(width, height, new L8(White));

        float size = Math.Max(28, width / 20f);
        const string text = "HELLO DAN";
        var font = AppFont.Bold(size);
        int textWidth = AppFont.MeasureWidth(text, font);
        int textHeight = AppFont.LineHeight(font);
        int x = Math.Max(0, (width - textWidth) / 2);
        int y = Math.Max(0, (height - textHeight) / 2);

        AppFont.DrawText(image, text, x, y, font, Black);
        DrawBorder(image, thickness: 8, color: Black);

        return image;
    }

    private static void DrawBorder(Image<L8> image, int thickness, byte color)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int py = 0; py < accessor.Height; py++)
            {
                var row = accessor.GetRowSpan(py);
                bool edgeRow = py < thickness || py >= accessor.Height - thickness;
                for (int px = 0; px < row.Length; px++)
                {
                    if (edgeRow || px < thickness || px >= row.Length - thickness)
                    {
                        row[px] = new L8(color);
                    }
                }
            }
        });
    }
}
