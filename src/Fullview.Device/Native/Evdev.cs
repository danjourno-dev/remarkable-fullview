using System.Runtime.InteropServices;

namespace Fullview.Device.Native;

/// <summary>
/// P/Invoke surface for reading raw `struct input_event` records off an evdev device node.
///
/// Layout is armhf (32-bit) `struct input_event { struct timeval time; __u16 type; __u16 code;
/// __s32 value; }`: an 8-byte timeval (two 4-byte longs) + 2 + 2 + 4 bytes = 16 bytes total,
/// no padding. The reMarkable 1's capacitive touchscreen is the "cyttsp5_mt" device — confirmed
/// via `cat /proc/bus/input/devices` on hardware to be /dev/input/event2 (event0 is the Wacom
/// pen digitizer, event1 is gpio-keys for the physical buttons), same caveat as the mxcfb ioctl
/// numbers in Fb.cs still applying to those.
/// </summary>
internal static class Evdev
{
    public const string DefaultTouchDevicePath = "/dev/input/event2";

    /// <summary>gpio-keys, the reMarkable 1's three physical buttons below the screen —
    /// unverified beyond `cat /proc/bus/input/devices` naming it event1 until Checkpoint 4.1
    /// confirms the right-hand button's key code on hardware.</summary>
    public const string DefaultButtonDevicePath = "/dev/input/event1";

    public const int EventSize = 16;
    public const int TvSecOffset = 0;
    public const int TvUsecOffset = 4;
    public const int TypeOffset = 8;
    public const int CodeOffset = 10;
    public const int ValueOffset = 12;

    public const int ORdonly = 0x0000;

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr read(int fd, IntPtr buf, UIntPtr count);
}
