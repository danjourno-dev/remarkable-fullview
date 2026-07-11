namespace Fullview.Device.Tests;

/// <summary>
/// Verifies the MXCFB_SEND_UPDATE constant in Native/Fb.cs against the
/// standard Linux _IOW macro, so the derivation is checked by something
/// other than a comment. This is pure arithmetic — it doesn't touch
/// /dev/fb0 and runs on any OS.
/// </summary>
public class MxcfbIoctlNumberTests
{
    [Fact]
    public void MxcfbSendUpdate_MatchesIowMacroForEpdcV2StructSize()
    {
        const uint direction = 1; // _IOC_WRITE
        const uint type = 'F';
        const uint number = 0x2E;
        const uint size = 72; // sizeof(struct mxcfb_update_data), EPDC v2 layout

        uint expected = (direction << 30) | (size << 16) | (type << 8) | number;

        Assert.Equal(0x4048462eu, expected);
        Assert.Equal(Native.Fb.MXCFB_SEND_UPDATE, expected);
        Assert.Equal(Native.Fb.MxcfbUpdateDataSize, (int)size);
    }
}
