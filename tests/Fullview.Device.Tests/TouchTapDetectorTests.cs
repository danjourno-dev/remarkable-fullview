using Fullview.Device.Input;

namespace Fullview.Device.Tests;

public class TouchTapDetectorTests
{
    private static RawInputEvent Abs(ushort code, int value, double atSeconds) =>
        new(TimeSpan.FromSeconds(atSeconds), EvCodes.EV_ABS, code, value);

    private static RawInputEvent TrackingId(int value, double atSeconds) =>
        new(TimeSpan.FromSeconds(atSeconds), EvCodes.EV_ABS, EvCodes.ABS_MT_TRACKING_ID, value);

    [Fact]
    public void QuickTouchWithoutMovement_IsATap()
    {
        var detector = new TouchTapDetector();

        Assert.Null(detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00)));
        Assert.Null(detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00)));
        Assert.Null(detector.Feed(TrackingId(0, 0.00)));
        var tap = detector.Feed(TrackingId(-1, 0.10));

        Assert.Equal(new TouchTap(100, 200), tap);
    }

    [Fact]
    public void HeldTooLong_IsNotATap()
    {
        var detector = new TouchTapDetector();

        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(TrackingId(0, 0.00));
        var tap = detector.Feed(TrackingId(-1, 0.90));

        Assert.Null(tap);
    }

    [Fact]
    public void MovedTooFarBeforeLifting_IsNotATap()
    {
        var detector = new TouchTapDetector();

        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 300, 0.05));
        var tap = detector.Feed(TrackingId(-1, 0.10));

        Assert.Null(tap);
    }

    [Fact]
    public void SmallMovementWithinThreshold_IsStillATap()
    {
        var detector = new TouchTapDetector();

        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 110, 0.05));
        var tap = detector.Feed(TrackingId(-1, 0.10));

        Assert.Equal(new TouchTap(110, 200), tap);
    }

    [Fact]
    public void TouchUpWithoutPriorTouchDown_IsIgnored()
    {
        var detector = new TouchTapDetector();

        var tap = detector.Feed(TrackingId(-1, 0.10));

        Assert.Null(tap);
    }

    [Fact]
    public void SynEvents_AreIgnored()
    {
        var detector = new TouchTapDetector();

        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(TrackingId(0, 0.00));
        var syn = detector.Feed(new RawInputEvent(TimeSpan.FromSeconds(0.05), EvCodes.EV_SYN, 0, 0));
        var tap = detector.Feed(TrackingId(-1, 0.10));

        Assert.Null(syn);
        Assert.Equal(new TouchTap(100, 200), tap);
    }
}
