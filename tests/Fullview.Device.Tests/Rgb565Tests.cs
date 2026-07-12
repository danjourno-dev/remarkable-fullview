namespace Fullview.Device.Tests;

/// <summary>
/// Verifies the shared gray8 -> RGB565 lookup table (Rgb565.cs) used by both
/// FramebufferDevice and QtfbScreen for their pixel blits.
/// </summary>
public class Rgb565Tests
{
    [Fact]
    public void Table_HasOneEntryPerGrayLevel()
    {
        Assert.Equal(256, Rgb565.FromGray8.Length);
    }

    [Fact]
    public void Black_MapsToZero()
    {
        Assert.Equal(0, Rgb565.FromGray8[0]);
    }

    [Fact]
    public void White_MapsToAllOnes()
    {
        // 5 bits red + 6 bits green + 5 bits blue, all set.
        ushort expected = (ushort)((0b11111 << 11) | (0b111111 << 5) | 0b11111);
        Assert.Equal(expected, Rgb565.FromGray8[255]);
    }

    [Fact]
    public void MidGray_PacksIntoDistinctRedGreenBlueFields()
    {
        ushort packed = Rgb565.FromGray8[128];
        int r = (packed >> 11) & 0b11111;
        int g = (packed >> 5) & 0b111111;
        int b = packed & 0b11111;

        Assert.Equal(128 >> 3, r);
        Assert.Equal(128 >> 2, g);
        Assert.Equal(128 >> 3, b);
    }
}
