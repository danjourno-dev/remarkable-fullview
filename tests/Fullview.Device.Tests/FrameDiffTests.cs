using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Device.Tests;

/// <summary>
/// Verifies the frame diff (FrameDiff.cs) that decides which rectangle gets blitted and
/// e-ink-refreshed after each render. A false-negative here would leave stale pixels visible
/// on the panel, so the edge rows/columns and multi-change spans matter.
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

        Assert.Null(FrameDiff.DirtyRect(previous, current));
    }

    [Fact]
    public void SinglePixelChange_ReturnsOnePixelRect()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[3, 7] = new L8(0);

        Assert.Equal(new Rectangle(3, 7, 1, 1), FrameDiff.DirtyRect(previous, current));
    }

    [Fact]
    public void ChangeInTopLeftCorner_IsDetected()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[0, 0] = new L8(0);

        Assert.Equal(new Rectangle(0, 0, 1, 1), FrameDiff.DirtyRect(previous, current));
    }

    [Fact]
    public void ChangeInBottomRightCorner_IsDetected()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[9, 19] = new L8(0);

        Assert.Equal(new Rectangle(9, 19, 1, 1), FrameDiff.DirtyRect(previous, current));
    }

    [Fact]
    public void DisjointChanges_ReturnOneSpanningRect()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        current[5, 4] = new L8(0);
        current[2, 15] = new L8(0);

        // Rows 5..14 and columns 3..4 are unchanged, but the result must still be a single
        // rectangle spanning both changes.
        Assert.Equal(new Rectangle(2, 4, 4, 12), FrameDiff.DirtyRect(previous, current));
    }

    [Fact]
    public void ColumnBounds_ComeFromDifferentRows()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 20);
        // Leftmost change on one row, rightmost on another — the rect must take the min/max
        // column across all differing rows, not just the first differing row's own span.
        current[1, 6] = new L8(0);
        current[8, 9] = new L8(0);

        Assert.Equal(new Rectangle(1, 6, 8, 4), FrameDiff.DirtyRect(previous, current));
    }

    [Fact]
    public void EveryPixelChanged_ReturnsFullFrame()
    {
        using var previous = SolidFrame(10, 20, 255);
        using var current = SolidFrame(10, 20, 0);

        Assert.Equal(new Rectangle(0, 0, 10, 20), FrameDiff.DirtyRect(previous, current));
    }

    [Fact]
    public void MismatchedDimensions_Throw()
    {
        using var previous = SolidFrame(10, 20);
        using var current = SolidFrame(10, 21);

        Assert.Throws<ArgumentException>(() => FrameDiff.DirtyRect(previous, current));
    }
}
