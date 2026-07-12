using System.Collections.Concurrent;
using Fullview.Device.Native;

namespace Fullview.Device.Input;

/// <summary>
/// Reads AppLoad's qtfb MESSAGE_USERINPUT stream and turns touch/pen release events into taps,
/// feeding the same BlockingCollection&lt;DeviceInput&gt; the evdev producers use when the app
/// is hand-launched over SSH (see Program.cs's RunTouchLoop/RunButtonLoop). Unlike the raw
/// cyttsp5 digitizer, qtfb input already arrives in screen pixel coordinates — no 767x1023
/// rescale and no 180-degree flip needed.
///
/// Runs on its own thread against the same QtfbScreen connection the render loop sends
/// updates over; see QtfbScreen.ReceiveUserInput for why that's safe. Takes the receive
/// operation as a delegate (rather than a QtfbScreen directly) so the press/release ->
/// DeviceInput mapping can be unit tested without a live qtfb socket.
/// </summary>
internal sealed class QtfbInputSource
{
    private readonly Func<QtfbUserInput> _receiveNext;

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
                case Qtfb.INPUT_TOUCH_RELEASE:
                case Qtfb.INPUT_PEN_RELEASE:
                    inputs.Add(new DeviceInput(DeviceInputKind.Tap, input.X, input.Y));
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
}
