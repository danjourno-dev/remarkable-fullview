using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Tests;

public class BitmapFontTests
{
    [Fact]
    public void MeasureWidth_ScalesWithGlyphCountAndScale()
    {
        int width = BitmapFont.MeasureWidth("HI", scale: 2);

        Assert.Equal(2 * (BitmapFont.GlyphWidth + 1) * 2, width);
    }

    [Fact]
    public void DrawText_UnknownCharacterFallsBackToBlankGlyphWithoutThrowing()
    {
        using var image = new Image<L8>(50, 20, new L8(255));

        var exception = Record.Exception(() => BitmapFont.DrawText(image, "H!N", 0, 0, scale: 2, color: 0));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawText_ClipsBlocksThatFallOutsideTheImage()
    {
        using var image = new Image<L8>(3, 3, new L8(255));

        var exception = Record.Exception(() => BitmapFont.DrawText(image, "H", 0, 0, scale: 10, color: 0));

        Assert.Null(exception);
        Assert.Equal(0, image[0, 0].PackedValue);
    }
}
