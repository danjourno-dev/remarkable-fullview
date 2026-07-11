using System.Runtime.InteropServices;
using Fullview.Device.Native;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Device;

/// <summary>
/// Owns /dev/fb0: queries real geometry via ioctl (rather than hardcoding
/// the rM1's known 1404x1872), mmaps the frame memory, and drives the
/// mxcfb e-ink controller to redraw after a write.
/// </summary>
public sealed class FramebufferDevice : IDisposable
{
    public static string DevicePath => Fb.DevicePath;

    private readonly int _fd;
    private readonly IntPtr _map;
    private readonly int _mapLength;

    public int Width { get; }
    public int Height { get; }
    public int BitsPerPixel { get; }
    public int Stride { get; }

    private FramebufferDevice(int fd, IntPtr map, int mapLength, int width, int height, int bitsPerPixel, int stride)
    {
        _fd = fd;
        _map = map;
        _mapLength = mapLength;
        Width = width;
        Height = height;
        BitsPerPixel = bitsPerPixel;
        Stride = stride;
    }

    public static FramebufferDevice Open()
    {
        int fd = Fb.open(Fb.DevicePath, Fb.ORdwr);
        if (fd < 0)
        {
            throw new IOException(
                $"Failed to open {Fb.DevicePath} (errno {Marshal.GetLastWin32Error()}). " +
                "This must run on the reMarkable itself, as a user with access to the framebuffer device.");
        }

        try
        {
            var varBuf = Marshal.AllocHGlobal(Fb.VarScreenInfoBufferSize);
            var fixBuf = Marshal.AllocHGlobal(Fb.FixScreenInfoBufferSize);
            try
            {
                ZeroMemory(varBuf, Fb.VarScreenInfoBufferSize);
                ZeroMemory(fixBuf, Fb.FixScreenInfoBufferSize);

                if (Fb.ioctl(fd, Fb.FBIOGET_VSCREENINFO, varBuf) < 0)
                {
                    throw new IOException($"FBIOGET_VSCREENINFO failed (errno {Marshal.GetLastWin32Error()}).");
                }

                if (Fb.ioctl(fd, Fb.FBIOGET_FSCREENINFO, fixBuf) < 0)
                {
                    throw new IOException($"FBIOGET_FSCREENINFO failed (errno {Marshal.GetLastWin32Error()}).");
                }

                int width = Marshal.ReadInt32(varBuf, Fb.VarXResOffset);
                int height = Marshal.ReadInt32(varBuf, Fb.VarYResOffset);
                int bitsPerPixel = Marshal.ReadInt32(varBuf, Fb.VarBitsPerPixelOffset);
                int smemLen = Marshal.ReadInt32(fixBuf, Fb.FixSmemLenOffset);
                int lineLength = Marshal.ReadInt32(fixBuf, Fb.FixLineLengthOffset);

                if (width <= 0 || height <= 0 || smemLen <= 0 || lineLength <= 0)
                {
                    throw new IOException(
                        $"{Fb.DevicePath} reported an invalid geometry (width={width}, height={height}, " +
                        $"smemLen={smemLen}, lineLength={lineLength}) — is this really the mxcfb device?");
                }

                IntPtr map = Fb.mmap(IntPtr.Zero, (UIntPtr)smemLen, Fb.ProtRead | Fb.ProtWrite, Fb.MapShared, fd, IntPtr.Zero);
                if (map == Fb.MapFailed)
                {
                    throw new IOException($"mmap of {smemLen} bytes failed (errno {Marshal.GetLastWin32Error()}).");
                }

                return new FramebufferDevice(fd, map, smemLen, width, height, bitsPerPixel, lineLength);
            }
            finally
            {
                Marshal.FreeHGlobal(varBuf);
                Marshal.FreeHGlobal(fixBuf);
            }
        }
        catch
        {
            Fb.close(fd);
            throw;
        }
    }

    /// <summary>
    /// Blits a grayscale image matching the framebuffer's exact geometry.
    /// Supports 16bpp (RGB565, the rM1's normal mode) and 8bpp (packed
    /// grayscale) — anything else means Checkpoint 3.2 found a different
    /// panel mode than expected, and this should fail loudly rather than
    /// silently mis-render.
    /// </summary>
    public void WriteImage(Image<L8> image)
    {
        if (image.Width != Width || image.Height != Height)
        {
            throw new ArgumentException($"Image is {image.Width}x{image.Height}, framebuffer is {Width}x{Height}.");
        }

        switch (BitsPerPixel)
        {
            case 16:
                WriteImageRgb565(image);
                break;
            case 8:
                WriteImageGray8(image);
                break;
            default:
                throw new NotSupportedException(
                    $"Only 16bpp (RGB565) and 8bpp (grayscale) framebuffers are supported; device reports {BitsPerPixel}bpp.");
        }
    }

    private void WriteImageRgb565(Image<L8> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                IntPtr rowPtr = _map + y * Stride;
                for (int x = 0; x < row.Length; x++)
                {
                    ushort rgb565 = GrayToRgb565(row[x].PackedValue);
                    Marshal.WriteInt16(rowPtr + x * 2, unchecked((short)rgb565));
                }
            }
        });
    }

    private void WriteImageGray8(Image<L8> image)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                IntPtr rowPtr = _map + y * Stride;
                for (int x = 0; x < row.Length; x++)
                {
                    Marshal.WriteByte(rowPtr + x, row[x].PackedValue);
                }
            }
        });
    }

    private static ushort GrayToRgb565(byte gray)
    {
        int r = gray >> 3; // 5 bits
        int g = gray >> 2; // 6 bits
        int b = gray >> 3; // 5 bits
        return (ushort)((r << 11) | (g << 5) | b);
    }

    /// <summary>
    /// Requests an e-ink redraw of the whole panel via MXCFB_SEND_UPDATE.
    /// Logs (rather than throws) on ioctl failure: the pixels are already
    /// in the mmap'd frame memory regardless, so a bad refresh call
    /// shouldn't be treated as a fatal error for hello-world purposes.
    /// </summary>
    public void Refresh(bool fullRefresh = true)
    {
        IntPtr buf = Marshal.AllocHGlobal(Fb.MxcfbUpdateDataSize);
        try
        {
            ZeroMemory(buf, Fb.MxcfbUpdateDataSize);
            Marshal.WriteInt32(buf, Fb.UpdRegionTopOffset, 0);
            Marshal.WriteInt32(buf, Fb.UpdRegionLeftOffset, 0);
            Marshal.WriteInt32(buf, Fb.UpdRegionWidthOffset, Width);
            Marshal.WriteInt32(buf, Fb.UpdRegionHeightOffset, Height);
            Marshal.WriteInt32(buf, Fb.WaveformModeOffset, Fb.WaveformModeGc16);
            Marshal.WriteInt32(buf, Fb.UpdateModeOffset, fullRefresh ? Fb.UpdateModeFull : Fb.UpdateModePartial);
            Marshal.WriteInt32(buf, Fb.UpdateMarkerOffset, 1);
            Marshal.WriteInt32(buf, Fb.TempOffset, Fb.TempUseAmbient);
            Marshal.WriteInt32(buf, Fb.FlagsOffset, 0);

            if (Fb.ioctl(_fd, Fb.MXCFB_SEND_UPDATE, buf) < 0)
            {
                int errno = Marshal.GetLastWin32Error();
                Console.Error.WriteLine(
                    $"MXCFB_SEND_UPDATE failed (errno {errno}) — pixels were written to {Fb.DevicePath} but the " +
                    "e-ink panel may not have redrawn. Known risk area for Checkpoint 3.2 (see docs/plans/implementation.md, Stage 3).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    private static void ZeroMemory(IntPtr ptr, int length)
    {
        for (int i = 0; i < length; i++)
        {
            Marshal.WriteByte(ptr, i, 0);
        }
    }

    public void Dispose()
    {
        Fb.munmap(_map, (UIntPtr)_mapLength);
        Fb.close(_fd);
    }
}
