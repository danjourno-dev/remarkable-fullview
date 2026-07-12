using System.Collections.Concurrent;
using Fullview.Device.Input;
using Fullview.Device.Native;

namespace Fullview.Device.Tests;

/// <summary>
/// Verifies QtfbInputSource's press/release -> DeviceInput mapping using a scripted feed
/// instead of a live qtfb socket (see QtfbInputSource's Func&lt;QtfbUserInput&gt; constructor).
/// </summary>
public class QtfbInputSourceTests
{
    [Fact]
    public void TouchRelease_ProducesTapAtItsCoordinates()
    {
        var script = new Queue<QtfbUserInput>(new[]
        {
            new QtfbUserInput(Qtfb.INPUT_TOUCH_PRESS, DevId: 0, X: 100, Y: 200, D: 50),
            new QtfbUserInput(Qtfb.INPUT_TOUCH_RELEASE, DevId: 0, X: 100, Y: 200, D: 0),
        });
        var inputs = new BlockingCollection<DeviceInput>();

        RunUntilExhausted(script, inputs);

        var tap = Assert.Single(inputs);
        Assert.Equal(DeviceInputKind.Tap, tap.Kind);
        Assert.Equal(100, tap.X);
        Assert.Equal(200, tap.Y);
    }

    [Fact]
    public void PenPressAlone_ProducesNoInput()
    {
        var script = new Queue<QtfbUserInput>(new[]
        {
            new QtfbUserInput(Qtfb.INPUT_PEN_PRESS, DevId: 0, X: 5, Y: 5, D: 80),
        });
        var inputs = new BlockingCollection<DeviceInput>();

        RunUntilExhausted(script, inputs);

        Assert.Empty(inputs);
    }

    [Fact]
    public void HardwareButtonRelease_ProducesHardwareButtonInput()
    {
        var script = new Queue<QtfbUserInput>(new[]
        {
            new QtfbUserInput(Qtfb.INPUT_BTN_PRESS, DevId: 0, X: 2, Y: 0, D: 0),
            new QtfbUserInput(Qtfb.INPUT_BTN_RELEASE, DevId: 0, X: 2, Y: 0, D: 0),
        });
        var inputs = new BlockingCollection<DeviceInput>();

        RunUntilExhausted(script, inputs);

        var press = Assert.Single(inputs);
        Assert.Equal(DeviceInputKind.HardwareButton, press.Kind);
    }

    private static void RunUntilExhausted(Queue<QtfbUserInput> script, BlockingCollection<DeviceInput> inputs)
    {
        using var cts = new CancellationTokenSource();
        var source = new QtfbInputSource(() =>
        {
            if (script.Count == 0)
            {
                cts.Cancel();
                throw new IOException("Script exhausted.");
            }

            return script.Dequeue();
        });

        // QtfbInputSource.Run itself swallows the IOException once the token is already
        // cancelled (matching how it tolerates a real socket erroring out on Dispose), so the
        // scripted feed cancelling then throwing on its last call is enough to unwind the loop.
        source.Run(inputs, cts.Token);
    }
}
