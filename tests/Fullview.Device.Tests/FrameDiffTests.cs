using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Device.Tests;

/// <summary>
/// Verifies the frame diff (FrameDiff.cs) that decides which row band gets blitted and
/// e-ink-refreshed after each render. A false-negative here would leave stale rows visible
/// on the panel, so the edge rows and multi-change spans matter.
/// </summary>
public class FrameDiffTests
{
    private static Image<L8> SolidFrame(int width, int height, byte gray = 255)
    {
        var image = new Image<L8>(width, height);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                accessor.GetRowSpan(y).Fill(new L8(gray));
            }
        });
        return image;
    }

    [Fact]
    public void IdenticalFrames_ReturnNull()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);

        Assert.Null(FrameDiff.DirtyRowBand(previous, current));
    }

    [Fact]
    public void SinglePixelChange_ReturnsFullWidthOneRowBand()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[3, 7] = new L8(0);

        var dirty = FrameDiff.DirtyRowBand(previous, current);

        Assert.Equal(new Rectangle(0, 7, 10, 1), dirty);
    }

    [Fact]
    public void ChangeInFirstRow_IsDetected()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[0, 0] = new L8(0);

        Assert.Equal(new Rectangle(0, 0, 10, 1), FrameDiff.DirtyRowBand(previous, current));
    }

    [Fact]
    public void ChangeInLastRow_IsDetected()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[9, 19] = new L8(0);

        Assert.Equal(new Rectangle(0, 19, 10, 1), FrameDiff.DirtyRowBand(previous, current));
    }

    [Fact]
    public void DisjointChanges_ReturnOneSpanningBand()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[5, 4] = new L8(0);
        current[2, 15] = new L8(0);

        // Rows 5..14 are unchanged but the band must still be a single rectangle.
        Assert.Equal(new Rectangle(0, 4, 10, 12), FrameDiff.DirtyRowBand(previous, current));
    }

    [Fact]
    public void EveryRowChanged_ReturnsFullFrame()
    {
        using var previous = SolidFrame(10, 20, 255);
        using var current = SolidFrame(10, 20, 0);

        Assert.Equal(new Rectangle(0, 0, 10, 20), FrameDiff.DirtyRowBand(previous, current));
    }

    [Fact]
    public void MismatchedDimensions_Throw()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 21);

        Assert.Throws<ArgumentException>(() => FrameDiff.DirtyRowBand(previous, current));
    }
}
