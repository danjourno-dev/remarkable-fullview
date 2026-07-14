using System.Runtime.InteropServices;
using Fullview.Device.Native;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Device;

/// <summary>A qtfb MESSAGE_USERINPUT event, decoded. See Native/Qtfb.cs for the wire format.</summary>
internal readonly record struct QtfbUserInput(int InputType, int DevId, int X, int Y, int D);

/// <summary>
/// Draws into AppLoad's shared qtfb surface instead of /dev/fb0 directly, so the app can be
/// launched from the AppLoad launcher (tools/device/appload/external.manifest.json, qtfb: true)
/// without fighting xochitl for the framebuffer. Speaks the protocol implemented by
/// asivery/rm-appload's src/qtfb/ (AF_UNIX SOCK_SEQPACKET socket at /tmp/qtfb.sock, a
/// shared-memory pixel surface, and touch/pen/button input pushed back over the same socket).
///
/// AppLoad hands us our framebuffer key via the QTFB_KEY env var (see Program.cs); this class
/// registers with that key, gets back a shared-memory key/size, and mmaps
/// /dev/shm/qtfb_&lt;key&gt; to draw into. The surface is RM2FB's fixed rM1/rM2 geometry —
/// 1404x1872 RGB565 — which matches what FramebufferDevice reads back from the real /dev/fb0
/// on this hardware, so the render pipeline needs no branching between the two screens.
/// </summary>
public sealed class QtfbScreen : IScreen
{
    public const int Rm2fbWidth = 1404;
    public const int Rm2fbHeight = 1872;

    public int Width => Rm2fbWidth;
    public int Height => Rm2fbHeight;
    public int Stride { get; } = Rm2fbWidth * 2; // RGB565

    private readonly int _socketFd;
    private readonly IntPtr _shm;
    private readonly int _shmSize;

    private QtfbScreen(int socketFd, IntPtr shm, int shmSize)
    {
        _socketFd = socketFd;
        _shm = shm;
        _shmSize = shmSize;
    }

    /// <summary>
    /// Performs the qtfb handshake: connects to AppLoad's socket, registers the framebuffer
    /// key AppLoad handed us via QTFB_KEY, and mmaps the shared-memory surface it responds
    /// with.
    /// </summary>
    public static QtfbScreen Connect(int fbKey)
    {
        int fd = Qtfb.socket(Qtfb.AF_UNIX, Qtfb.SOCK_SEQPACKET, 0);
        if (fd < 0)
        {
            throw new IOException($"Failed to create the qtfb AF_UNIX socket (errno {Marshal.GetLastWin32Error()}).");
        }

        try
        {
            byte[] addr = Qtfb.BuildSockaddrUn(Qtfb.SocketPath);
            if (Qtfb.connect(fd, addr, addr.Length) < 0)
            {
                throw new IOException(
                    $"Failed to connect to {Qtfb.SocketPath} (errno {Marshal.GetLastWin32Error()}). " +
                    "QtfbScreen must run under AppLoad with qtfb: true in external.manifest.json.");
            }

            SendMessage(fd, BuildInitializeMessage(fbKey));
            byte[] reply = ReceiveMessage(fd);
            if (reply[Qtfb.TypeOffset] != Qtfb.MESSAGE_INITIALIZE)
            {
                throw new IOException($"Expected an INITIALIZE reply from qtfb, got message type {reply[Qtfb.TypeOffset]}.");
            }

            int shmKey = Qtfb.ReadInt32(reply, Qtfb.PayloadOffset + Qtfb.InitShmKeyOffset);
            int shmSize = Qtfb.ReadInt32(reply, Qtfb.PayloadOffset + Qtfb.InitShmSizeOffset);

            // shm_open("qtfb_<key>", ...) on the server side names the backing file
            // /dev/shm/qtfb_<key> — plain open()+mmap() on that path works without any
            // POSIX shm P/Invoke of our own.
            string shmPath = $"/dev/shm/qtfb_{shmKey}";
            int shmFd = Qtfb.open(shmPath, Qtfb.ORdwr);
            if (shmFd < 0)
            {
                throw new IOException($"Failed to open {shmPath} (errno {Marshal.GetLastWin32Error()}).");
            }

            IntPtr map;
            try
            {
                map = Qtfb.mmap(IntPtr.Zero, (UIntPtr)shmSize, Qtfb.ProtRead | Qtfb.ProtWrite, Qtfb.MapShared, shmFd, IntPtr.Zero);
                if (map == Qtfb.MapFailed)
                {
                    throw new IOException($"mmap of {shmPath} ({shmSize} bytes) failed (errno {Marshal.GetLastWin32Error()}).");
                }
            }
            finally
            {
                // The mapping stays valid after the fd is closed; matches the pattern
                // FramebufferDevice.Open uses for the mxcfb /dev/fb0 mapping.
                Qtfb.close(shmFd);
            }

            return new QtfbScreen(fd, map, shmSize);
        }
        catch
        {
            Qtfb.close(fd);
            throw;
        }
    }

    /// <summary>
    /// Blits a grayscale image matching the qtfb surface's exact geometry (RM2FB is fixed at
    /// 1404x1872 RGB565, unlike FramebufferDevice which queries /dev/fb0's real geometry).
    /// </summary>
    public void WriteImage(Image<L8> image) => WriteImage(image, new Rectangle(0, 0, Width, Height));

    /// <summary>Blits just <paramref name="region"/> of the image — see <see cref="IScreen.WriteImage(Image{L8}, Rectangle)"/>.</summary>
    public void WriteImage(Image<L8> image, Rectangle region)
    {
        if (image.Width != Width || image.Height != Height)
        {
            throw new ArgumentException($"Image is {image.Width}x{image.Height}, qtfb surface is {Width}x{Height}.");
        }

        if (region.X < 0 || region.Y < 0 || region.Right > Width || region.Bottom > Height)
        {
            throw new ArgumentException($"Region {region} is outside the {Width}x{Height} qtfb surface.");
        }

        // Same convert-directly-into-the-mapping approach as FramebufferDevice.WriteImageRgb565
        // — each pixel is touched once, with no intermediate row buffer or per-row P/Invoke.
        unsafe
        {
            image.ProcessPixelRows(accessor =>
            {
                for (int y = region.Y; y < region.Bottom; y++)
                {
                    var row = accessor.GetRowSpan(y).Slice(region.X, region.Width);
                    var dest = new Span<byte>((byte*)_shm + y * Stride + region.X * 2, region.Width * 2);
                    Rgb565.ConvertRow(row, dest);
                }
            });
        }
    }

    /// <summary>
    /// Requests an e-ink redraw of the whole panel. The refresh-mode -> waveform-quality
    /// mapping (UI for full refreshes, FAST for partial ones) mirrors the intent of
    /// FramebufferDevice's GC16-vs-DU choice but hasn't been validated against how AppLoad's
    /// qtfb server actually renders each RefreshMode on real hardware — treat this as a
    /// starting point to confirm during device verification, not a settled value.
    /// </summary>
    public void Refresh(bool fullRefresh = true)
    {
        SendMessage(_socketFd, BuildRefreshModeMessage(fullRefresh ? Qtfb.REFRESH_MODE_UI : Qtfb.REFRESH_MODE_FAST));
        SendMessage(_socketFd, BuildUpdateMessage(Qtfb.UPDATE_ALL, 0, 0, Width, Height));
    }

    /// <summary>
    /// De-ghost flash: fill the whole panel solid black, then solid white, each a full refresh,
    /// to erase whatever faint image (e.g. the splash wordmark) a normal refresh leaves behind.
    /// Unlike <see cref="FramebufferDevice"/> there is no update-completion message to wait on
    /// (see <see cref="WaitForRefresh"/>), so a fixed settle delay stands in for the real signal
    /// — long enough that the qtfb server has read the shared surface and driven the full
    /// waveform before the next frame overwrites it. Tune <see cref="FullRefreshSettleMs"/>
    /// against real hardware if the flash looks partial or the two transitions run together.
    /// </summary>
    public void Flash()
    {
        FlashSolid(0);
        FlashSolid(255);
    }

    // The qtfb protocol acknowledges nothing, so this is a deliberate guess at how long a full
    // GC16 panel transition takes on the rM1 — confirm on device (see Flash's remarks).
    private const int FullRefreshSettleMs = 700;

    private void FlashSolid(byte gray)
    {
        using var solid = new Image<L8>(Width, Height, new L8(gray));
        WriteImage(solid);
        Refresh(fullRefresh: true);
        Thread.Sleep(FullRefreshSettleMs);
    }

    /// <summary>Requests a partial e-ink redraw of just <paramref name="region"/>.</summary>
    public void RefreshRegion(Rectangle region)
    {
        SendMessage(_socketFd, BuildRefreshModeMessage(Qtfb.REFRESH_MODE_FAST));
        SendMessage(_socketFd, BuildUpdateMessage(Qtfb.UPDATE_PARTIAL, region.X, region.Y, region.Width, region.Height));
    }

    /// <summary>Same as <see cref="RefreshRegion"/>; the returned marker is meaningless (0)
    /// because AppLoad's qtfb protocol has no update-completion message, unlike /dev/fb0's
    /// MXCFB_WAIT_FOR_UPDATE_COMPLETE ioctl (see FramebufferDevice).</summary>
    public uint BeginRefreshRegion(Rectangle region)
    {
        RefreshRegion(region);
        return 0;
    }

    /// <summary>No-op: qtfb offers no way to wait for a specific update, so tap-flash
    /// feedback doesn't get the same guaranteed hold time it has on /dev/fb0.</summary>
    public void WaitForRefresh(uint marker)
    {
    }

    /// <summary>
    /// Blocks for the next message qtfb pushes over the connection — only MESSAGE_USERINPUT is
    /// ever sent unsolicited after the initial handshake. Called from
    /// <see cref="Input.QtfbInputSource"/>'s own thread; safe to call concurrently with
    /// <see cref="WriteImage"/>/<see cref="Refresh"/> on the render thread, since a
    /// SOCK_SEQPACKET Unix socket is full-duplex.
    /// </summary>
    internal QtfbUserInput ReceiveUserInput()
    {
        byte[] msg = ReceiveMessage(_socketFd);
        if (msg[Qtfb.TypeOffset] != Qtfb.MESSAGE_USERINPUT)
        {
            throw new IOException($"Expected a USERINPUT message from qtfb, got message type {msg[Qtfb.TypeOffset]}.");
        }

        return new QtfbUserInput(
            Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.InputTypeOffset),
            Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.InputDevIdOffset),
            Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.InputXOffset),
            Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.InputYOffset),
            Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.InputDOffset));
    }

    internal static byte[] BuildInitializeMessage(int fbKey)
    {
        var msg = new byte[Qtfb.MessageSize];
        msg[Qtfb.TypeOffset] = (byte)Qtfb.MESSAGE_INITIALIZE;
        Qtfb.WriteInt32(msg, Qtfb.PayloadOffset + Qtfb.InitFbKeyOffset, fbKey);
        msg[Qtfb.PayloadOffset + Qtfb.InitFbTypeOffset] = (byte)Qtfb.FBFMT_RM2FB;
        return msg;
    }

    internal static byte[] BuildUpdateMessage(int kind, int x, int y, int width, int height)
    {
        var msg = new byte[Qtfb.MessageSize];
        msg[Qtfb.TypeOffset] = (byte)Qtfb.MESSAGE_UPDATE;
        Qtfb.WriteInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateKindOffset, kind);
        Qtfb.WriteInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateXOffset, x);
        Qtfb.WriteInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateYOffset, y);
        Qtfb.WriteInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateWidthOffset, width);
        Qtfb.WriteInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateHeightOffset, height);
        return msg;
    }

    internal static byte[] BuildRefreshModeMessage(int mode)
    {
        var msg = new byte[Qtfb.MessageSize];
        msg[Qtfb.TypeOffset] = (byte)Qtfb.MESSAGE_SET_REFRESH_MODE;
        Qtfb.WriteInt32(msg, Qtfb.PayloadOffset + Qtfb.RefreshModeOffset, mode);
        return msg;
    }

    internal static byte[] BuildTerminateMessage()
    {
        var msg = new byte[Qtfb.MessageSize];
        msg[Qtfb.TypeOffset] = (byte)Qtfb.MESSAGE_TERMINATE;
        return msg;
    }

    private static void SendMessage(int fd, byte[] message)
    {
        IntPtr sent = Qtfb.send(fd, message, (UIntPtr)message.Length, 0);
        if (sent.ToInt64() != message.Length)
        {
            throw new IOException($"qtfb send() wrote {sent} of {message.Length} bytes (errno {Marshal.GetLastWin32Error()}).");
        }
    }

    private static byte[] ReceiveMessage(int fd)
    {
        var buf = new byte[Qtfb.MessageSize];
        IntPtr received = Qtfb.recv(fd, buf, (UIntPtr)buf.Length, 0);
        if (received.ToInt64() != buf.Length)
        {
            throw new IOException($"qtfb recv() returned {received} bytes, expected {buf.Length} (errno {Marshal.GetLastWin32Error()}).");
        }

        return buf;
    }

    public void Dispose()
    {
        try
        {
            SendMessage(_socketFd, BuildTerminateMessage());
        }
        catch (IOException)
        {
            // Best-effort — if the connection is already gone there's nothing further to clean up.
        }

        Qtfb.munmap(_shm, (UIntPtr)_shmSize);
        Qtfb.close(_socketFd);
    }
}
