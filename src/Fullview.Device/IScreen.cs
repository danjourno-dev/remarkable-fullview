using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Device;

/// <summary>
/// Whatever surface the board gets rendered into: either <see cref="FramebufferDevice"/>
/// (direct /dev/fb0 access, used when hand-launched over SSH) or <see cref="QtfbScreen"/>
/// (AppLoad's shared qtfb surface, used when launched from the AppLoad launcher via
/// tools/device/appload/external.manifest.json — see QTFB_KEY in Program.cs).
///
/// Program.cs's render loop only ever talks to this interface, so which one is live is
/// decided once at startup and nothing else in the board/render pipeline needs to know.
/// </summary>
public interface IScreen : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>Blits a grayscale image matching the screen's exact geometry.</summary>
    void WriteImage(Image<L8> image);

    /// <summary>Requests an e-ink redraw of the whole panel.</summary>
    void Refresh(bool fullRefresh = true);

    /// <summary>Requests a partial e-ink redraw of just <paramref name="region"/>.</summary>
    void RefreshRegion(Rectangle region);
}
