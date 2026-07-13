namespace Fullview.Device.Input;

public readonly record struct TouchTap(int X, int Y);

/// <summary>A released touch that moved further than <see cref="TouchTapDetector"/>'s tap
/// threshold — the vertical component of a finger-drag (e.g. scrolling the Agenda screen).
/// Raw touch-native Y units, same axis TouchTapDetector reads off ABS_MT_POSITION_Y; the
/// caller (Program.cs's RunTouchLoop) applies the same axis-inversion/rescale it already
/// uses for taps before turning this into a row count.</summary>
public readonly record struct TouchDrag(int DeltaY);

/// <summary>
/// Turns a raw evdev event stream into tap events: ABS_MT_TRACKING_ID assigned, then cleared
/// (-1) again within <see cref="MaxDuration"/> and without moving more than
/// <see cref="MaxMovement"/> touch units, is a tap at the last known ABS_MT_POSITION_X/Y.
/// Anything longer or that moves further (a drag/scroll) is not a tap.
///
/// Touch-down/up state is only evaluated on SYN_REPORT, not eagerly on ABS_MT_TRACKING_ID.
/// The rM1's cyttsp5_mt driver reports TRACKING_ID before POSITION_X/Y within a frame, so
/// evaluating the transition eagerly would capture the *previous* touch's stale X/Y as this
/// touch's down position — inflating the measured movement (often past
/// <see cref="MaxMovement"/>, silently dropping the tap) or, on release, reporting the wrong
/// coordinate entirely if X/Y hadn't yet been applied. Deferring to SYN_REPORT guarantees every
/// EV_ABS update in the frame has already been applied first, regardless of intra-frame order.
///
/// Pure and clock-free (uses each event's own <see cref="RawInputEvent.Timestamp"/>) so it's
/// testable from a canned event sequence without touching real hardware.
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

    private bool? _pendingTrackingIdIsDown;
    private TouchDrag? _pendingDrag;

    /// <summary>Consumes and clears any drag produced by the most recent <see cref="Feed"/>
    /// call. Separate from Feed's return value (rather than a tuple) so Feed's existing
    /// tap-only contract, and the tests pinned to it, don't change.</summary>
    public TouchDrag? TakeDrag()
    {
        var drag = _pendingDrag;
        _pendingDrag = null;
        return drag;
    }

    /// <summary>Feeds one decoded event; returns the completed tap, if this event was the
    /// SYN_REPORT closing the frame that finished one.</summary>
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
                _pendingTrackingIdIsDown = evt.Value >= 0;
                return null;

            case EvCodes.EV_SYN when evt.Code == EvCodes.SYN_REPORT:
                return FlushFrame(evt.Timestamp);

            default:
                return null;
        }
    }

    private TouchTap? FlushFrame(TimeSpan timestamp)
    {
        if (_pendingTrackingIdIsDown is not { } isDown)
        {
            return null;
        }

        _pendingTrackingIdIsDown = null;
        return isDown ? OnTouchDown(timestamp) : OnTouchUp(timestamp);
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

        if (duration <= MaxDuration && movement <= MaxMovement)
        {
            return new TouchTap(_x, _y);
        }

        if (movement > MaxMovement)
        {
            _pendingDrag = new TouchDrag(_y - _downY);
        }

        return null;
    }
}
