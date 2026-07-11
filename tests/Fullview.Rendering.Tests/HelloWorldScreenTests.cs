using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Tests;

public class HelloWorldScreenTests
{
    [Fact]
    public void Render_ReturnsImageMatchingRequestedGeometry()
    {
        using var image = HelloWorldScreen.Render(1404, 1872);

        Assert.Equal(1404, image.Width);
        Assert.Equal(1872, image.Height);
    }

    [Fact]
    public void Render_DrawsSomeBlackPixels()
    {
        using var image = HelloWorldScreen.Render(1404, 1872);

        bool hasBlackPixel = false;
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height && !hasBlackPixel; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++)
                {
                    if (row[x].PackedValue == 0)
                    {
                        hasBlackPixel = true;
                        break;
                    }
                }
            }
        });

        Assert.True(hasBlackPixel, "Expected the hello-world screen to draw at least one black pixel (text or border).");
    }

    [Fact]
    public void Render_TopLeftCornerIsBorderBlack()
    {
        using var image = HelloWorldScreen.Render(1404, 1872);

        Assert.Equal(0, image[0, 0].PackedValue);
    }
}
