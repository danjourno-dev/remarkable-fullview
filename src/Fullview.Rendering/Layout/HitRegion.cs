using SixLabors.ImageSharp;

namespace Fullview.Rendering.Layout;

/// <summary>One tappable rectangle on the rendered board, in framebuffer pixel space.</summary>
public sealed record HitRegion(Rectangle Bounds, BoardAction Action)
{
    public bool Contains(int x, int y) => Bounds.Contains(x, y);
}
