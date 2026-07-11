using System.Runtime.InteropServices;

namespace Fullview.Device.Native;

/// <summary>
/// P/Invoke surface for talking to the Linux framebuffer (/dev/fb0) and the
/// i.MX EPDC (mxcfb) e-ink controller used by the reMarkable 1.
///
/// Struct field offsets come from the kernel's linux/fb.h, computed for
/// armhf (32-bit `unsigned long`) since that's the only platform this ever
/// runs on. MXCFB_SEND_UPDATE and the mxcfb_update_data layout follow the
/// EPDC v2 struct (adds dither_mode/quant_bit before alt_buffer_data,
/// sizeof == 72 bytes / 0x48) used by the rM1 kernel — this is the same
/// layout libremarkable (github.com/canselcik/libremarkable) targets and
/// the constant 0x4048462e is the same one it (and other rM homebrew:
/// rm2fb, rmkit) use. Checkpoint 3.2 is the real test of these numbers on
/// hardware; if MXCFB_SEND_UPDATE fails, FramebufferDevice.Refresh logs the
/// errno instead of throwing so pixels already written via mmap aren't lost.
/// </summary>
internal static class Fb
{
    public const string DevicePath = "/dev/fb0";

    // Old-style direct ioctl numbers (linux/fb.h) — not _IOC-encoded.
    public const uint FBIOGET_VSCREENINFO = 0x4600;
    public const uint FBIOGET_FSCREENINFO = 0x4602;

    // _IOW('F', 0x2E, struct mxcfb_update_data), EPDC v2 layout, size 72.
    public const uint MXCFB_SEND_UPDATE = 0x4048462e;

    // fb_var_screeninfo byte offsets we actually need.
    public const int VarXResOffset = 0;
    public const int VarYResOffset = 4;
    public const int VarBitsPerPixelOffset = 24;
    public const int VarScreenInfoBufferSize = 160;

    // fb_fix_screeninfo byte offsets (armhf: id[16] + unsigned long smem_start(4)).
    public const int FixSmemLenOffset = 20;
    public const int FixLineLengthOffset = 44;
    public const int FixScreenInfoBufferSize = 80;

    // mxcfb_update_data byte layout (72 bytes total).
    public const int UpdRegionTopOffset = 0;
    public const int UpdRegionLeftOffset = 4;
    public const int UpdRegionWidthOffset = 8;
    public const int UpdRegionHeightOffset = 12;
    public const int WaveformModeOffset = 16;
    public const int UpdateModeOffset = 20;
    public const int UpdateMarkerOffset = 24;
    public const int TempOffset = 28;
    public const int FlagsOffset = 32;
    // dither_mode (36), quant_bit (40), alt_buffer_data (44..72) intentionally left zero.
    public const int MxcfbUpdateDataSize = 72;

    public const int WaveformModeGc16 = 2;
    public const int UpdateModeFull = 1;
    public const int UpdateModePartial = 0;
    public const int TempUseAmbient = 0x1000;

    public const int ORdwr = 0x0002;
    public const int ProtRead = 0x1;
    public const int ProtWrite = 0x2;
    public const int MapShared = 0x01;
    public static readonly IntPtr MapFailed = new(-1);

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr mmap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    public static extern int munmap(IntPtr addr, UIntPtr length);

    [DllImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    public static extern int ioctl(int fd, uint request, IntPtr argp);
}
