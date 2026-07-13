using System.Collections.Concurrent;
using Fullview.Device.Native;

namespace Fullview.Device.Input;

/// <summary>
/// Reads AppLoad's qtfb MESSAGE_USERINPUT stream and turns touch/pen press+release pairs into
/// taps or drags, feeding the same BlockingCollection&lt;DeviceInput&gt; the evdev producers
/// use when the app is hand-launched over SSH (see Program.cs's RunTouchLoop/RunButtonLoop).
/// Unlike the raw cyttsp5 digitizer, qtfb input already arrives in screen pixel coordinates —
/// no 767x1023 rescale and no 180-degree flip needed.
///
/// Runs on its own thread against the same QtfbScreen connection the render loop sends
/// updates over; see QtfbScreen.ReceiveUserInput for why that's safe. Takes the receive
/// operation as a delegate (rather than a QtfbScreen directly) so the press/release ->
/// DeviceInput mapping can be unit tested without a live qtfb socket.
/// </summary>
internal sealed class QtfbInputSource
{
    /// <summary>Pixel-space counterpart of TouchTapDetector's touch-native 40-unit tap
    /// threshold — qtfb input already arrives in fb pixels, so no rescale applies here.</summary>
    private const int MaxTapMovementPx = 40;

    private readonly Func<QtfbUserInput> _receiveNext;

    private bool _pressed;
    private int _pressX;
    private int _pressY;

    public QtfbInputSource(QtfbScreen screen) : this(screen.ReceiveUserInput)
    {
    }

    public QtfbInputSource(Func<QtfbUserInput> receiveNext)
    {
        _receiveNext = receiveNext;
    }

    public void Run(BlockingCollection<DeviceInput> inputs, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            QtfbUserInput input;
            try
            {
                input = _receiveNext();
            }
            catch (IOException) when (token.IsCancellationRequested)
            {
                return;
            }

            switch (input.InputType)
            {
                case Qtfb.INPUT_TOUCH_PRESS:
                case Qtfb.INPUT_PEN_PRESS:
                    _pressed = true;
                    _pressX = input.X;
                    _pressY = input.Y;
                    break;

                case Qtfb.INPUT_TOUCH_RELEASE:
                case Qtfb.INPUT_PEN_RELEASE:
                    HandleRelease(input, inputs);
                    break;

                case Qtfb.INPUT_BTN_RELEASE:
                    // Treats any forwarded hardware button as the mode toggle, same as
                    // RunButtonLoop's single-key check. Whether xochitl forwards physical
                    // buttons to AppLoad's qtfb client at all on rM1 is unverified — see the
                    // hardware-buttons risk note for this feature.
                    inputs.Add(new DeviceInput(DeviceInputKind.HardwareButton, 0, 0));
                    break;
            }
        }
    }

    private void HandleRelease(QtfbUserInput release, BlockingCollection<DeviceInput> inputs)
    {
        // A release with no matching press (e.g. the app started mid-gesture) has nothing to
        // measure movement against — treat it as a tap at its own coordinates, same as before
        // this method existed.
        if (!_pressed)
        {
            inputs.Add(new DeviceInput(DeviceInputKind.Tap, release.X, release.Y));
            return;
        }

        _pressed = false;
        int movement = Math.Max(Math.Abs(release.X - _pressX), Math.Abs(release.Y - _pressY));
        if (movement <= MaxTapMovementPx)
        {
            inputs.Add(new DeviceInput(DeviceInputKind.Tap, release.X, release.Y));
            return;
        }

        // Raw fb-pixel delta, not a row count — Program.cs's Apply() knows which screen is
        // current and converts pixels to rows using that screen's row height.
        int fbDeltaY = release.Y - _pressY;
        if (fbDeltaY != 0)
        {
            inputs.Add(new DeviceInput(DeviceInputKind.Drag, 0, fbDeltaY));
        }
    }
}
