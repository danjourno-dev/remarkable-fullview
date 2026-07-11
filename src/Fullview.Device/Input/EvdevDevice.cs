using System.Runtime.InteropServices;
using Fullview.Device.Native;

namespace Fullview.Device.Input;

/// <summary>
/// Opens an evdev device node and yields decoded <see cref="RawInputEvent"/>s, blocking on
/// each read. Generic over which node it's pointed at — a background thread reads the touch
/// device and hands events to <see cref="TouchTapDetector"/>, another reads the gpio-keys
/// button device and watches for <see cref="EvCodes.KEY_RIGHT"/>. Hardware-only — not unit
/// tested beyond what TouchTapDetector covers in isolation.
/// </summary>
public sealed class EvdevDevice : IDisposable
{
    private readonly int _fd;
    private readonly IntPtr _buffer;

    private EvdevDevice(int fd)
    {
        _fd = fd;
        _buffer = Marshal.AllocHGlobal(Evdev.EventSize);
    }

    public static EvdevDevice Open(string devicePath)
    {
        int fd = Evdev.open(devicePath, Evdev.ORdonly);
        if (fd < 0)
        {
            throw new IOException(
                $"Failed to open {devicePath} (errno {Marshal.GetLastWin32Error()}). " +
                "This must run on the reMarkable itself, as a user with access to the input device.");
        }

        return new EvdevDevice(fd);
    }

    /// <summary>Blocks until the next event is available and returns it.</summary>
    public RawInputEvent ReadNext()
    {
        IntPtr read = Evdev.read(_fd, _buffer, (UIntPtr)Evdev.EventSize);
        if ((long)read != Evdev.EventSize)
        {
            throw new IOException($"Short read from input device (errno {Marshal.GetLastWin32Error()}).");
        }

        long tvSec = Marshal.ReadInt32(_buffer, Evdev.TvSecOffset);
        long tvUsec = Marshal.ReadInt32(_buffer, Evdev.TvUsecOffset);
        ushort type = (ushort)Marshal.ReadInt16(_buffer, Evdev.TypeOffset);
        ushort code = (ushort)Marshal.ReadInt16(_buffer, Evdev.CodeOffset);
        int value = Marshal.ReadInt32(_buffer, Evdev.ValueOffset);

        var timestamp = TimeSpan.FromSeconds(tvSec) + TimeSpan.FromTicks(tvUsec * 10);
        return new RawInputEvent(timestamp, type, code, value);
    }

    public void Dispose()
    {
        Marshal.FreeHGlobal(_buffer);
        Evdev.close(_fd);
    }
}
