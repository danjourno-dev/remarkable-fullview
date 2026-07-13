using Fullview.Device.Input;

namespace Fullview.Device.Tests;

public class TouchTapDetectorTests
{
    private static RawInputEvent Abs(ushort code, int value, double atSeconds) =>
        new(TimeSpan.FromSeconds(atSeconds), EvCodes.EV_ABS, code, value);

    private static RawInputEvent TrackingId(int value, double atSeconds) =>
        new(TimeSpan.FromSeconds(atSeconds), EvCodes.EV_ABS, EvCodes.ABS_MT_TRACKING_ID, value);

    private static RawInputEvent Syn(double atSeconds) =>
        new(TimeSpan.FromSeconds(atSeconds), EvCodes.EV_SYN, EvCodes.SYN_REPORT, 0);

    // Event order within each frame matches the rM1's cyttsp5_mt driver: TRACKING_ID first,
    // then POSITION_X/Y, then SYN_REPORT — not the X/Y-then-TRACKING_ID order these tests used
    // to assume, which masked the bug where touch-down state was evaluated before X/Y for that
    // touch had actually been applied.

    [Fact]
    public void QuickTouchWithoutMovement_IsATap()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        Assert.Null(detector.Feed(Syn(0.00)));

        detector.Feed(TrackingId(-1, 0.10));
        var tap = detector.Feed(Syn(0.10));

        Assert.Equal(new TouchTap(100, 200), tap);
    }

    [Fact]
    public void HeldTooLong_IsNotATap()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(Syn(0.00));

        detector.Feed(TrackingId(-1, 0.90));
        var tap = detector.Feed(Syn(0.90));

        Assert.Null(tap);
    }

    [Fact]
    public void MovedTooFarBeforeLifting_IsNotATap()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(Syn(0.00));

        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 300, 0.05));
        detector.Feed(Syn(0.05));

        detector.Feed(TrackingId(-1, 0.10));
        var tap = detector.Feed(Syn(0.10));

        Assert.Null(tap);
    }

    [Fact]
    public void SmallMovementWithinThreshold_IsStillATap()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(Syn(0.00));

        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 110, 0.05));
        detector.Feed(Syn(0.05));

        detector.Feed(TrackingId(-1, 0.10));
        var tap = detector.Feed(Syn(0.10));

        Assert.Equal(new TouchTap(110, 200), tap);
    }

    [Fact]
    public void TouchUpWithoutPriorTouchDown_IsIgnored()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(-1, 0.10));
        var tap = detector.Feed(Syn(0.10));

        Assert.Null(tap);
    }

    [Fact]
    public void TransitionIsOnlyEvaluatedAtSynReport()
    {
        var detector = new TouchTapDetector();

        // TRACKING_ID alone must not fire the down/up transition — only SYN_REPORT does.
        Assert.Null(detector.Feed(TrackingId(0, 0.00)));
        Assert.Null(detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00)));
        Assert.Null(detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00)));
        Assert.Null(detector.Feed(TrackingId(-1, 0.00)));

        // A SYN_REPORT with no pending TRACKING_ID transition (a plain move) returns null.
        detector.Feed(TrackingId(0, 0.01));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.01));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.01));
        detector.Feed(Syn(0.01));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 105, 0.05));
        Assert.Null(detector.Feed(Syn(0.05)));
    }

    [Fact]
    public void TrackingIdBeforePosition_UsesThisTouchsOwnCoordinates_NotThePreviousTouchs()
    {
        // Regression test: the detector used to capture the touch-down position as soon as
        // ABS_MT_TRACKING_ID arrived, which — given the driver's real TRACKING_ID-then-X/Y
        // ordering — meant it captured the *previous* touch's final position. That inflated
        // the measured "movement" for the second tap far past MaxMovement whenever two taps
        // landed in different places, silently dropping the second tap.
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(Syn(0.00));
        detector.Feed(TrackingId(-1, 0.10));
        var firstTap = detector.Feed(Syn(0.10));

        detector.Feed(TrackingId(1, 1.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 900, 1.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 1600, 1.00));
        detector.Feed(Syn(1.00));
        detector.Feed(TrackingId(-1, 1.10));
        var secondTap = detector.Feed(Syn(1.10));

        Assert.Equal(new TouchTap(100, 200), firstTap);
        Assert.Equal(new TouchTap(900, 1600), secondTap);
    }

    [Fact]
    public void MovedTooFarBeforeLifting_ExposesTheVerticalDistanceAsADrag()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(Syn(0.00));

        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 350, 0.05));
        detector.Feed(Syn(0.05));

        detector.Feed(TrackingId(-1, 0.10));
        var tap = detector.Feed(Syn(0.10));

        Assert.Null(tap);
        Assert.Equal(new TouchDrag(150), detector.TakeDrag());
    }

    [Fact]
    public void TakeDrag_ClearsAfterBeingRead_SoItIsNotReportedTwice()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(Syn(0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 350, 0.05));
        detector.Feed(Syn(0.05));
        detector.Feed(TrackingId(-1, 0.10));
        detector.Feed(Syn(0.10));

        Assert.NotNull(detector.TakeDrag());
        Assert.Null(detector.TakeDrag());
    }

    [Fact]
    public void QuickTap_ProducesNoDrag()
    {
        var detector = new TouchTapDetector();

        detector.Feed(TrackingId(0, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_X, 100, 0.00));
        detector.Feed(Abs(EvCodes.ABS_MT_POSITION_Y, 200, 0.00));
        detector.Feed(Syn(0.00));
        detector.Feed(TrackingId(-1, 0.10));
        detector.Feed(Syn(0.10));

        Assert.Null(detector.TakeDrag());
    }
}
