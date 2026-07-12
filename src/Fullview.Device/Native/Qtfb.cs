using System.Runtime.InteropServices;

namespace Fullview.Device.Native;

/// <summary>
/// P/Invoke surface and wire-format constants for AppLoad's qtfb protocol
/// (asivery/rm-appload, src/qtfb/). When launched by AppLoad with
/// <c>qtfb: true</c>, the app is handed a framebuffer key in the
/// <c>QTFB_KEY</c> env var and talks to AppLoad over a Unix
/// <see cref="SockSeqpacket"/> socket at <see cref="SocketPath"/> to obtain a
/// shared-memory surface to draw into, and to receive touch/pen/button input
/// pushed back over the same socket.
///
/// Both message structs are exactly 24 bytes on the armhf (32-bit `int` /
/// `size_t`) runtime this always publishes for (see tools/device/publish-arm.sh,
/// `-r linux-arm`): byte 0 is the message type, bytes 1-3 are padding, and the
/// union payload starts at offset 4. Offsets below are read directly off
/// AppLoad's src/qtfb/common.h rather than relying on P/Invoke struct
/// marshalling, matching the style Fb.cs and Evdev.cs already use for their
/// own kernel/ioctl struct layouts.
/// </summary>
internal static class Qtfb
{
    public const string SocketPath = "/tmp/qtfb.sock";

    // sys/socket.h
    public const int AF_UNIX = 1;
    public const int SOCK_SEQPACKET = 5;

    // Client -> server message types (qtfb::MessageType in common.h).
    public const int MESSAGE_INITIALIZE = 0;
    public const int MESSAGE_UPDATE = 1;
    public const int MESSAGE_CUSTOM_INITIALIZE = 2;
    public const int MESSAGE_TERMINATE = 3;
    public const int MESSAGE_USERINPUT = 4; // server -> client only
    public const int MESSAGE_SET_REFRESH_MODE = 5;
    public const int MESSAGE_REQUEST_FULL_REFRESH = 6;

    // qtfb::FBFormat
    public const int FBFMT_RM2FB = 0;

    // Update kind carried in a MESSAGE_UPDATE payload.
    public const int UPDATE_ALL = 0;
    public const int UPDATE_PARTIAL = 1;

    // qtfb::RefreshMode
    public const int REFRESH_MODE_UFAST = 0;
    public const int REFRESH_MODE_FAST = 1;
    public const int REFRESH_MODE_ANIMATE = 2;
    public const int REFRESH_MODE_CONTENT = 3;
    public const int REFRESH_MODE_UI = 4;

    // qtfb::UserInputContents.inputType
    public const int INPUT_TOUCH_PRESS = 0x10;
    public const int INPUT_TOUCH_RELEASE = 0x11;
    public const int INPUT_TOUCH_UPDATE = 0x12;
    public const int INPUT_PEN_PRESS = 0x20;
    public const int INPUT_PEN_RELEASE = 0x21;
    public const int INPUT_PEN_UPDATE = 0x22;
    public const int INPUT_BTN_PRESS = 0x30;
    public const int INPUT_BTN_RELEASE = 0x31;
    public const int INPUT_VKB_PRESS = 0x40;
    public const int INPUT_VKB_RELEASE = 0x41;

    // Every message is 24 bytes: 1 byte type + 3 padding + a 20-byte union.
    public const int MessageSize = 24;
    public const int TypeOffset = 0;
    public const int PayloadOffset = 4;

    // ClientMessage(INITIALIZE) payload, from PayloadOffset.
    public const int InitFbKeyOffset = 0;
    public const int InitFbTypeOffset = 4; // 1 byte

    // ClientMessage(UPDATE) payload, from PayloadOffset.
    public const int UpdateKindOffset = 0;
    public const int UpdateXOffset = 4;
    public const int UpdateYOffset = 8;
    public const int UpdateWidthOffset = 12;
    public const int UpdateHeightOffset = 16;

    // ClientMessage(SET_REFRESH_MODE) payload, from PayloadOffset.
    public const int RefreshModeOffset = 0;

    // ServerMessage(INITIALIZE) payload, from PayloadOffset.
    public const int InitShmKeyOffset = 0;
    public const int InitShmSizeOffset = 4;

    // ServerMessage(USERINPUT) payload, from PayloadOffset.
    public const int InputTypeOffset = 0;
    public const int InputDevIdOffset = 4;
    public const int InputXOffset = 8;
    public const int InputYOffset = 12;
    public const int InputDOffset = 16;

    [DllImport("libc", SetLastError = true)]
    public static extern int socket(int domain, int type, int protocol);

    [DllImport("libc", SetLastError = true)]
    public static extern int connect(int sockfd, byte[] addr, int addrlen);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr send(int sockfd, byte[] buf, UIntPtr len, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr recv(int sockfd, byte[] buf, UIntPtr len, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern IntPtr mmap(IntPtr addr, UIntPtr length, int prot, int flags, int fd, IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    public static extern int munmap(IntPtr addr, UIntPtr length);

    public const int ORdonly = 0x0000;
    public const int ORdwr = 0x0002;
    public const int ProtRead = 0x1;
    public const int ProtWrite = 0x2;
    public const int MapShared = 0x01;
    public static readonly IntPtr MapFailed = new(-1);

    /// <summary>
    /// Builds a `struct sockaddr_un` (`sun_family` u16 + a 108-byte path buffer, the standard
    /// Linux UNIX_PATH_MAX) for <see cref="SocketPath"/>, as `connect()` expects it.
    /// </summary>
    public static byte[] BuildSockaddrUn(string path)
    {
        const int pathMax = 108;
        var addr = new byte[2 + pathMax];
        addr[0] = (byte)AF_UNIX;
        addr[1] = 0;
        var pathBytes = System.Text.Encoding.ASCII.GetBytes(path);
        if (pathBytes.Length >= pathMax)
        {
            throw new ArgumentException($"Socket path '{path}' is too long for sockaddr_un.");
        }

        Array.Copy(pathBytes, 0, addr, 2, pathBytes.Length);
        return addr;
    }

    /// <summary>Writes a little-endian int32 into a message buffer at the given byte offset.</summary>
    public static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    /// <summary>Reads a little-endian int32 from a message buffer at the given byte offset.</summary>
    public static int ReadInt32(byte[] buffer, int offset)
    {
        return buffer[offset]
            | (buffer[offset + 1] << 8)
            | (buffer[offset + 2] << 16)
            | (buffer[offset + 3] << 24);
    }
}
