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
    /// Returns the full-width band of rows that differ between <paramref name="previous"/> and
    /// <paramref name="current"/>, or null when the frames are pixel-identical. The band spans
    /// the first through last differing row; unchanged rows in between are included, keeping
    /// the region a single rectangle whose shape only differs from a full-screen update in top
    /// and height.
    /// </summary>
    public static Rectangle? DirtyRowBand(Image<L8> previous, Image<L8> current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            throw new ArgumentException(
                $"Cannot diff a {previous.Width}x{previous.Height} frame against a {current.Width}x{current.Height} frame.");
        }

        int top = -1;
        int bottom = -1;

        previous.ProcessPixelRows(current, (prev, cur) =>
        {
            for (int y = 0; y < prev.Height; y++)
            {
                if (!RowsEqual(prev.GetRowSpan(y), cur.GetRowSpan(y)))
                {
                    top = y;
                    break;
                }
            }

            if (top < 0)
            {
                return;
            }

            // The top scan already proved row `top` differs, so this loop always terminates
            // with bottom >= top.
            for (int y = prev.Height - 1; y >= top; y--)
            {
                if (!RowsEqual(prev.GetRowSpan(y), cur.GetRowSpan(y)))
                {
                    bottom = y;
                    break;
                }
            }
        });

        return top < 0 ? null : new Rectangle(0, top, previous.Width, bottom - top + 1);
    }

    private static bool RowsEqual(ReadOnlySpan<L8> a, ReadOnlySpan<L8> b) =>
        MemoryMarshal.AsBytes(a).SequenceEqual(MemoryMarshal.AsBytes(b));
}
