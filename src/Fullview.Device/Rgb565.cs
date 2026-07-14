using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Device;

/// <summary>
/// Shared 8-bit-gray -> RGB565 pixel conversion. Used by both <see cref="FramebufferDevice"/>
/// (writes directly into mmap'd /dev/fb0) and <see cref="QtfbScreen"/> (writes into AppLoad's
/// shared qtfb SHM surface) — both are the same RGB565 layout on the rM1's 16bpp panel mode.
/// </summary>
internal static class Rgb565
{
    // All 256 gray levels fit a lookup table, so conversion is a table read instead of
    // shifting/masking per pixel every frame.
    public static readonly ushort[] FromGray8 = Build();

    private static ushort[] Build()
    {
        var table = new ushort[256];
        for (int gray = 0; gray < table.Length; gray++)
        {
            int r = gray >> 3; // 5 bits
            int g = gray >> 2; // 6 bits
            int b = gray >> 3; // 5 bits
            table[gray] = (ushort)((r << 11) | (g << 5) | b);
        }

        return table;
    }

    /// <summary>
    /// Converts a row (or slice of a row) of gray pixels to little-endian RGB565 bytes.
    /// Shared by FramebufferDevice and QtfbScreen so their blit loops stay identical.
    /// </summary>
    public static void ConvertRow(ReadOnlySpan<L8> gray, Span<byte> rgb565)
    {
        for (int x = 0; x < gray.Length; x++)
        {
            ushort packed = FromGray8[gray[x].PackedValue];
            rgb565[x * 2] = (byte)packed;
            rgb565[x * 2 + 1] = (byte)(packed >> 8);
        }
    }
}
