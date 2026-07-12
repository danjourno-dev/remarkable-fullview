namespace Fullview.Device.Input;

/// <summary>
/// One decoded Linux `struct input_event` (see linux/input.h). <see cref="Timestamp"/> is the
/// kernel-reported capture time (tv_sec + tv_usec), not wall-clock-at-read-time — using it
/// lets <see cref="TouchTapDetector"/> measure tap duration without any clock dependency,
/// so it's fully unit-testable from a canned event sequence.
/// </summary>
public readonly record struct RawInputEvent(TimeSpan Timestamp, ushort Type, ushort Code, int Value);

/// <summary>Event type/code constants this app cares about (linux/input-event-codes.h).
/// The rM1's capacitive touch controller (cyttsp5_mt) is a slot-based Multitouch Protocol B
/// device that reports no EV_KEY codes at all (confirmed via `cat /proc/bus/input/devices`
/// showing KEY=0), so BTN_TOUCH never fires — touch down/up is derived from
/// ABS_MT_TRACKING_ID transitioning to/from -1 instead.</summary>
public static class EvCodes
{
    public const ushort EV_SYN = 0x00;
    public const ushort EV_KEY = 0x01;
    public const ushort EV_ABS = 0x03;

    /// <summary>Terminates one batch of EV_ABS updates. The touch controller reports
    /// ABS_MT_TRACKING_ID before ABS_MT_POSITION_X/Y within a frame, so touch-down/up state
    /// must not be evaluated until SYN_REPORT — evaluating it eagerly on TRACKING_ID reads
    /// stale X/Y left over from the previous touch.</summary>
    public const ushort SYN_REPORT = 0x00;

    public const ushort ABS_MT_POSITION_X = 0x35;
    public const ushort ABS_MT_POSITION_Y = 0x36;
    public const ushort ABS_MT_TRACKING_ID = 0x39;

    /// <summary>The rM1's gpio-keys device reports its three physical buttons (below the
    /// screen: left/home/right) as standard linux/input-event-codes.h EV_KEY codes. Only the
    /// right-hand button is wired up (mode switch); unverified on real hardware until
    /// Checkpoint 4.1.</summary>
    public const ushort KEY_RIGHT = 106;
}
