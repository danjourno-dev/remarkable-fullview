using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Fullview.Device;

/// <summary>
/// Compares two rendered frames to find what actually changed, so the blit and e-ink refresh
/// can cover just that region instead of the whole panel. Diffing the pixels (rather than
/// asking the renderer what it thinks changed) is ground truth: a todo toggle re-sorts its
/// panel and can shift every row below the tapped one, and the diff picks that up by
/// construction.
/// </summary>
internal static class FrameDiff
{
    /// <summary>
    /// Returns the bounding rectangle of every pixel that differs between
    /// <paramref name="previous"/> and <paramref name="current"/>, or null when the frames are
    /// pixel-identical. Unchanged pixels inside the bounds are included, keeping the region a
    /// single rectangle — but a small change (one checkbox) yields a small rect, so the blit
    /// and DU refresh don't pay for the panel's full width the way a row-band diff would.
    /// </summary>
    public static Rectangle? DirtyRect(Image<L8> previous, Image<L8> current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            throw new ArgumentException(
                $"Cannot diff a {previous.Width}x{previous.Height} frame against a {current.Width}x{current.Height} frame.");
        }

        int top = -1;
        int bottom = -1;
        int left = int.MaxValue;
        int right = -1;

        previous.ProcessPixelRows(current, (prev, cur) =>
        {
            for (int y = 0; y < prev.Height; y++)
            {
                // L8 is one byte per pixel, so a byte index is a column index.
                var a = MemoryMarshal.AsBytes(prev.GetRowSpan(y));
                var b = MemoryMarshal.AsBytes(cur.GetRowSpan(y));

                int first = a.CommonPrefixLength(b);
                if (first == a.Length)
                {
                    continue;
                }

                if (top < 0)
                {
                    top = y;
                }

                bottom = y;

                if (first < left)
                {
                    left = first;
                }

                // Backward scan for this row's last differing column — guaranteed to stop at
                // `first`, and skipped entirely once some earlier row already pushed `right`
                // to the frame's final column.
                if (right < a.Length - 1)
                {
                    int last = a.Length - 1;
                    while (last > right && a[last] == b[last])
                    {
                        last--;
                    }

                    if (last > right)
                    {
                        right = last;
                    }
                }
            }
        });

        return top < 0 ? null : new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }
}
