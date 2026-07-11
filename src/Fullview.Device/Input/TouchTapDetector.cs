namespace Fullview.Device.Input;

public readonly record struct TouchTap(int X, int Y);

/// <summary>
/// Turns a raw evdev event stream into tap events: ABS_MT_TRACKING_ID assigned, then cleared
/// (-1) again within
/// <see cref="MaxDuration"/> and without moving more than <see cref="MaxMovement"/> touch
/// units, is a tap at the last known ABS_MT_POSITION_X/Y. Anything longer or that moves
/// further (a drag/scroll) is not a tap. Pure and clock-free (uses each event's own
/// <see cref="RawInputEvent.Timestamp"/>) so it's testable from a canned event sequence
/// without touching real hardware.
/// </summary>
public sealed class TouchTapDetector
{
    private static readonly TimeSpan MaxDuration = TimeSpan.FromMilliseconds(400);
    private const int MaxMovement = 40;

    private int _x;
    private int _y;
    private bool _down;
    private TimeSpan _downAt;
    private int _downX;
    private int _downY;

    /// <summary>Feeds one decoded event; returns the completed tap, if this event was the
    /// touch-up that finished one.</summary>
    public TouchTap? Feed(RawInputEvent evt)
    {
        switch (evt.Type)
        {
            case EvCodes.EV_ABS when evt.Code == EvCodes.ABS_MT_POSITION_X:
                _x = evt.Value;
                return null;

            case EvCodes.EV_ABS when evt.Code == EvCodes.ABS_MT_POSITION_Y:
                _y = evt.Value;
                return null;

            case EvCodes.EV_ABS when evt.Code == EvCodes.ABS_MT_TRACKING_ID:
                return evt.Value >= 0 ? OnTouchDown(evt.Timestamp) : OnTouchUp(evt.Timestamp);

            default:
                return null;
        }
    }

    private TouchTap? OnTouchDown(TimeSpan timestamp)
    {
        _down = true;
        _downAt = timestamp;
        _downX = _x;
        _downY = _y;
        return null;
    }

    private TouchTap? OnTouchUp(TimeSpan timestamp)
    {
        if (!_down)
        {
            return null;
        }

        _down = false;
        var duration = timestamp - _downAt;
        int movement = Math.Max(Math.Abs(_x - _downX), Math.Abs(_y - _downY));

        return duration <= MaxDuration && movement <= MaxMovement ? new TouchTap(_x, _y) : null;
    }
}
