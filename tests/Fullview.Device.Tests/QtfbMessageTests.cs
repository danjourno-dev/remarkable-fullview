using Fullview.Device.Native;

namespace Fullview.Device.Tests;

/// <summary>
/// Verifies the 24-byte qtfb wire encoding (Native/Qtfb.cs, QtfbScreen's message builders)
/// against the offsets documented from asivery/rm-appload's src/qtfb/common.h. Pure byte-array
/// arithmetic — doesn't touch a socket or run under AppLoad.
/// </summary>
public class QtfbMessageTests
{
    [Fact]
    public void InitializeMessage_IsExactly24BytesWithKeyAndFormatAtDocumentedOffsets()
    {
        byte[] msg = QtfbScreen.BuildInitializeMessage(fbKey: 0x1234);

        Assert.Equal(Qtfb.MessageSize, msg.Length);
        Assert.Equal(Qtfb.MESSAGE_INITIALIZE, msg[Qtfb.TypeOffset]);
        Assert.Equal(0x1234, Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.InitFbKeyOffset));
        Assert.Equal(Qtfb.FBFMT_RM2FB, msg[Qtfb.PayloadOffset + Qtfb.InitFbTypeOffset]);
    }

    [Fact]
    public void UpdateMessage_RoundTripsKindAndRegion()
    {
        byte[] msg = QtfbScreen.BuildUpdateMessage(Qtfb.UPDATE_PARTIAL, x: 10, y: 20, width: 300, height: 400);

        Assert.Equal(Qtfb.MessageSize, msg.Length);
        Assert.Equal(Qtfb.MESSAGE_UPDATE, msg[Qtfb.TypeOffset]);
        Assert.Equal(Qtfb.UPDATE_PARTIAL, Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateKindOffset));
        Assert.Equal(10, Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateXOffset));
        Assert.Equal(20, Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateYOffset));
        Assert.Equal(300, Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateWidthOffset));
        Assert.Equal(400, Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.UpdateHeightOffset));
    }

    [Fact]
    public void RefreshModeMessage_RoundTripsMode()
    {
        byte[] msg = QtfbScreen.BuildRefreshModeMessage(Qtfb.REFRESH_MODE_UI);

        Assert.Equal(Qtfb.MESSAGE_SET_REFRESH_MODE, msg[Qtfb.TypeOffset]);
        Assert.Equal(Qtfb.REFRESH_MODE_UI, Qtfb.ReadInt32(msg, Qtfb.PayloadOffset + Qtfb.RefreshModeOffset));
    }

    [Fact]
    public void TerminateMessage_IsJustTheTypeByte()
    {
        byte[] msg = QtfbScreen.BuildTerminateMessage();

        Assert.Equal(Qtfb.MessageSize, msg.Length);
        Assert.Equal(Qtfb.MESSAGE_TERMINATE, msg[Qtfb.TypeOffset]);
        for (int i = 1; i < msg.Length; i++)
        {
            Assert.Equal(0, msg[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void WriteThenReadInt32_RoundTrips(int value)
    {
        var buf = new byte[8];
        Qtfb.WriteInt32(buf, 2, value);

        Assert.Equal(value, Qtfb.ReadInt32(buf, 2));
    }
}
