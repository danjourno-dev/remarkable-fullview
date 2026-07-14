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
public sealed class FramebufferDevice : IScreen
{
    public static string DevicePath => Fb.DevicePath;

    private readonly int _fd;
    private readonly IntPtr _map;
    private readonly int _mapLength;

    public int Width { get; }
    public int Height { get; }
    public int BitsPerPixel { get; }
    public int Stride { get; }

    // Reused across blits so a full-frame write doesn't allocate a new row buffer on every
    // tap (previously each pixel was written with its own Marshal.Write* call — ~2.6M P/Invoke
    // calls per frame; now one row is built in managed memory and Marshal.Copy'd in a single call).
    private readonly byte[] _rowBuffer;

    private FramebufferDevice(int fd, IntPtr map, int mapLength, int width, int height, int bitsPerPixel, int stride)
    {
        _fd = fd;
        _map = map;
        _mapLength = mapLength;
        Width = width;
        Height = height;
        BitsPerPixel = bitsPerPixel;
        Stride = stride;
        _rowBuffer = new byte[stride];
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
    public void WriteImage(Image<L8> image) => WriteImage(image, new Rectangle(0, 0, Width, Height));

    /// <summary>Blits just <paramref name="region"/> of the image — see <see cref="IScreen.WriteImage(Image{L8}, Rectangle)"/>.</summary>
    public void WriteImage(Image<L8> image, Rectangle region)
    {
        if (image.Width != Width || image.Height != Height)
        {
            throw new ArgumentException($"Image is {image.Width}x{image.Height}, framebuffer is {Width}x{Height}.");
        }

        if (region.X < 0 || region.Y < 0 || region.Right > Width || region.Bottom > Height)
        {
            throw new ArgumentException($"Region {region} is outside the {Width}x{Height} framebuffer.");
        }

        switch (BitsPerPixel)
        {
            case 16:
                WriteImageRgb565(image, region);
                break;
            case 8:
                WriteImageGray8(image, region);
                break;
            default:
                throw new NotSupportedException(
                    $"Only 16bpp (RGB565) and 8bpp (grayscale) framebuffers are supported; device reports {BitsPerPixel}bpp.");
        }
    }

    private void WriteImageRgb565(Image<L8> image, Rectangle region)
    {
        // Building each row in managed memory and Marshal.Copy'ing it as a single block avoids
        // one Marshal.Write* P/Invoke per pixel (~2.6M calls for a full rM1 frame) — that
        // per-call overhead, not the actual byte shuffling, was the dominant blit cost.
        image.ProcessPixelRows(accessor =>
        {
            for (int y = region.Y; y < region.Bottom; y++)
            {
                var row = accessor.GetRowSpan(y).Slice(region.X, region.Width);
                Rgb565.ConvertRow(row, _rowBuffer);
                Marshal.Copy(_rowBuffer, 0, _map + y * Stride + region.X * 2, region.Width * 2);
            }
        });
    }

    private void WriteImageGray8(Image<L8> image, Rectangle region)
    {
        image.ProcessPixelRows(accessor =>
        {
            for (int y = region.Y; y < region.Bottom; y++)
            {
                var row = accessor.GetRowSpan(y).Slice(region.X, region.Width);
                for (int x = 0; x < row.Length; x++)
                {
                    _rowBuffer[x] = row[x].PackedValue;
                }

                Marshal.Copy(_rowBuffer, 0, _map + y * Stride + region.X, region.Width);
            }
        });
    }

    /// <summary>
    /// Requests an e-ink redraw of the whole panel via MXCFB_SEND_UPDATE.
    /// Logs (rather than throws) on ioctl failure: the pixels are already
    /// in the mmap'd frame memory regardless, so a bad refresh call
    /// shouldn't be treated as a fatal error for hello-world purposes.
    /// </summary>
    public void Refresh(bool fullRefresh = true) =>
        SendUpdate(0, 0, Width, Height, fullRefresh ? Fb.UpdateModeFull : Fb.UpdateModePartial, Fb.WaveformModeGc16);

    /// <summary>
    /// Requests a partial e-ink redraw of just <paramref name="region"/>, using the fast
    /// monochrome (DU) waveform — for tap-to-complete and mode-toggle updates, where
    /// redrawing the whole panel with GC16 would be needlessly slow.
    /// </summary>
    public void RefreshRegion(Rectangle region) =>
        SendUpdate(region.X, region.Y, region.Width, region.Height, Fb.UpdateModePartial, Fb.WaveformModeDu);

    /// <summary>
    /// Same as <see cref="RefreshRegion"/>, but blocks until this specific update has actually
    /// finished transitioning on the e-ink panel (MXCFB_WAIT_FOR_UPDATE_COMPLETE) before
    /// returning — used to hold a tap's flash feedback on screen for exactly as long as it takes
    /// to become visible, no more and no less, rather than guessing with a fixed Sleep.
    /// </summary>
    public void RefreshRegionAndWait(Rectangle region)
    {
        uint marker = SendUpdate(region.X, region.Y, region.Width, region.Height, Fb.UpdateModePartial, Fb.WaveformModeDu);

        IntPtr buf = Marshal.AllocHGlobal(sizeof(uint));
        try
        {
            Marshal.WriteInt32(buf, unchecked((int)marker));
            if (Fb.ioctl(_fd, Fb.MXCFB_WAIT_FOR_UPDATE_COMPLETE, buf) < 0)
            {
                int errno = Marshal.GetLastWin32Error();
                Console.Error.WriteLine($"MXCFB_WAIT_FOR_UPDATE_COMPLETE failed (errno {errno}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    // Every update needs its own marker: MXCFB_WAIT_FOR_UPDATE_COMPLETE waits for a specific
    // marker value to finish, and the kernel remembers the last-completed marker per value. A
    // single hardcoded marker (this used to always write 1) meant waiting on it could be
    // satisfied instantly by an unrelated update — e.g. the previous loop iteration's full-screen
    // refresh — that happened to finish before this call, rather than the one just issued. That
    // made RefreshRegionAndWait's hold time nondeterministic (worked sometimes, not others).
    // Marker 0 is reserved/ignored by the EPDC driver, so the sequence skips it on wraparound.
    private uint _nextMarker = 1;

    private uint SendUpdate(int x, int y, int width, int height, int updateMode, int waveformMode)
    {
        uint marker = _nextMarker;
        _nextMarker = _nextMarker == uint.MaxValue ? 1 : _nextMarker + 1;

        IntPtr buf = Marshal.AllocHGlobal(Fb.MxcfbUpdateDataSize);
        try
        {
            ZeroMemory(buf, Fb.MxcfbUpdateDataSize);
            Marshal.WriteInt32(buf, Fb.UpdRegionTopOffset, y);
            Marshal.WriteInt32(buf, Fb.UpdRegionLeftOffset, x);
            Marshal.WriteInt32(buf, Fb.UpdRegionWidthOffset, width);
            Marshal.WriteInt32(buf, Fb.UpdRegionHeightOffset, height);
            Marshal.WriteInt32(buf, Fb.WaveformModeOffset, waveformMode);
            Marshal.WriteInt32(buf, Fb.UpdateModeOffset, updateMode);
            Marshal.WriteInt32(buf, Fb.UpdateMarkerOffset, unchecked((int)marker));
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

        return marker;
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
