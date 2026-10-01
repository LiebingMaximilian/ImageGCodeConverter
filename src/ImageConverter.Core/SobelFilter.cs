using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageConverter.Core;

/// <summary>
/// Sobel edge detection on a <see cref="Bitmap"/>.
///
///      Gx = [-1 0 +1]      Gy = [-1 -2 -1]
///           [-2 0 +2]           [ 0  0  0]
///           [-1 0 +1]           [+1 +2 +1]
///
/// magnitude = sqrt(Gx² + Gy²), normalised to 0..255.
/// </summary>
public static class SobelFilter
{
    /// <param name="source">Input bitmap (any format GDI+ can read).</param>
    /// <param name="threshold">
    /// 0 = keep the full grey-scale gradient. 1..255 = binarise: pixels with a
    /// normalised magnitude >= threshold become white, everything else black.
    /// </param>
    /// <param name="invert">If true, edges are black on a white background.</param>
    public static Bitmap Apply(Bitmap source, int threshold = 0, bool invert = false)
    {
        int width = source.Width;
        int height = source.Height;

        float[] gray = BitmapPixels.ToGrayscale(source);
        float[] magnitude = new float[width * height];
        float max = 0f;

        // Border pixels are left at 0 (no full 3x3 neighbourhood).
        Parallel.For(1, height - 1, () => 0f, (y, _, localMax) =>
        {
            int row = y * width;
            int up = row - width;
            int down = row + width;

            for (int x = 1; x < width - 1; x++)
            {
                float tl = gray[up + x - 1],   t = gray[up + x],   tr = gray[up + x + 1];
                float l  = gray[row + x - 1],                       r  = gray[row + x + 1];
                float bl = gray[down + x - 1], b = gray[down + x], br = gray[down + x + 1];

                float gx = (tr + 2f * r + br) - (tl + 2f * l + bl);
                float gy = (bl + 2f * b + br) - (tl + 2f * t + tr);

                float m = MathF.Sqrt(gx * gx + gy * gy);
                magnitude[row + x] = m;
                if (m > localMax) localMax = m;
            }
            return localMax;
        },
        localMax =>
        {
            lock (magnitude) { if (localMax > max) max = localMax; }
        });

        return ToBitmap(magnitude, width, height, max, threshold, invert);
    }

    private static Bitmap ToBitmap(float[] magnitude, int width, int height, float max,
                                   int threshold, bool invert)
    {
        var result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = result.LockBits(new Rectangle(0, 0, width, height),
                                   ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] pixels = new byte[stride * height];
            float scale = max > 0 ? 255f / max : 0f;

            Parallel.For(0, height, y =>
            {
                int dst = y * stride;
                int src = y * width;
                for (int x = 0; x < width; x++, dst += 4)
                {
                    int v = (int)(magnitude[src + x] * scale);
                    if (threshold > 0) v = v >= threshold ? 255 : 0;
                    if (invert) v = 255 - v;

                    byte b = (byte)Math.Clamp(v, 0, 255);
                    pixels[dst] = b;
                    pixels[dst + 1] = b;
                    pixels[dst + 2] = b;
                    pixels[dst + 3] = 255;
                }
            });

            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        }
        finally
        {
            result.UnlockBits(data);
        }
        return result;
    }
}
