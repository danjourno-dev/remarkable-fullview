using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Rendering.Layout;

/// <summary>A rendered frame plus the hit regions that apply to it, in the same pixel space.</summary>
public sealed record ScreenRenderResult(Image<L8> Image, IReadOnlyList<HitRegion> Regions);
