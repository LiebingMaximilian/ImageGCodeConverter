namespace ImageConverter.Core.Tsp;

/// <summary>
/// A downscaled "where should ink go" map. Values are 0 (no points) .. 1 (densest).
/// Pure arrays, no System.Drawing.
/// </summary>
public sealed class DensityMap
{
    public int Width { get; }
    public int Height { get; }
    public float[] Values { get; }

    public DensityMap(int width, int height, float[] values)
    {
        if (values.Length != width * height) throw new ArgumentException("Size mismatch.", nameof(values));
        Width = width;
        Height = height;
        Values = values;
    }

    /// <param name="gray">Luminance 0..255, row-major.</param>
    public static DensityMap FromGray(float[] gray, int width, int height, TspArtSettings s, int pointCount)
    {
        var (small, w, h) = Resample(gray, width, height, WorkingLongSide(width, height, s, pointCount));

        float[] edges = s.EdgeWeight > 0 ? SobelMagnitude(small, w, h) : new float[w * h];
        float edgeWeight = Math.Clamp(s.EdgeWeight, 0f, 1f);
        float gamma = Math.Max(0.05f, s.Gamma);
        float cutoff = Math.Clamp(s.WhiteCutoff, 0f, 0.99f);

        var values = new float[w * h];
        for (int i = 0; i < values.Length; i++)
        {
            float dark = 1f - small[i] / 255f;
            if (s.Invert) dark = 1f - dark;
            dark = MathF.Pow(Math.Clamp(dark, 0f, 1f), gamma);

            float d = (1f - edgeWeight) * dark + edgeWeight * edges[i];
            values[i] = d < cutoff ? 0f : d;
        }
        return new DensityMap(w, h, values);
    }

    /// <summary>
    /// Longest side of the working image: at least WorkingSize, and big enough that every point
    /// gets ~MinPixelsPerPoint pixels (may be larger than the input -> upscaling), capped by MaxWorkingMegapixels.
    /// </summary>
    public static int WorkingLongSide(int width, int height, TspArtSettings s, int pointCount)
    {
        double ratio = (double)Math.Max(width, height) / Math.Max(1, Math.Min(width, height));   // >= 1
        double neededPixels = (double)pointCount * Math.Max(1, s.MinPixelsPerPoint);
        double maxPixels = Math.Max(1, s.MaxWorkingMegapixels) * 1_000_000.0;
        double pixels = Math.Min(Math.Max(neededPixels, Math.Pow(Math.Max(16, s.WorkingSize), 2) / ratio), maxPixels);
        return Math.Max(16, (int)Math.Round(Math.Sqrt(pixels * ratio)));   // long * (long / ratio) = pixels
    }

    /// <summary>Resizes so the longest side is <paramref name="longSide"/>: box filter when shrinking, bilinear when enlarging.</summary>
    private static (float[] data, int w, int h) Resample(float[] src, int sw, int sh, int longSide)
    {
        double scale = (double)longSide / Math.Max(sw, sh);
        if (Math.Abs(scale - 1.0) < 1e-6) return ((float[])src.Clone(), sw, sh);
        if (scale > 1.0) return Upscale(src, sw, sh, scale);

        int w = Math.Max(1, (int)Math.Round(sw * scale));
        int h = Math.Max(1, (int)Math.Round(sh * scale));
        var dst = new float[w * h];

        Parallel.For(0, h, ty =>
        {
            int y0 = (int)(ty / scale), y1 = Math.Min(sh, Math.Max(y0 + 1, (int)((ty + 1) / scale)));
            for (int tx = 0; tx < w; tx++)
            {
                int x0 = (int)(tx / scale), x1 = Math.Min(sw, Math.Max(x0 + 1, (int)((tx + 1) / scale)));
                double sum = 0;
                for (int y = y0; y < y1; y++)
                {
                    int row = y * sw;
                    for (int x = x0; x < x1; x++) sum += src[row + x];
                }
                dst[ty * w + tx] = (float)(sum / ((y1 - y0) * (x1 - x0)));
            }
        });
        return (dst, w, h);
    }

    private static (float[] data, int w, int h) Upscale(float[] src, int sw, int sh, double scale)
    {
        int w = Math.Max(1, (int)Math.Round(sw * scale));
        int h = Math.Max(1, (int)Math.Round(sh * scale));
        var dst = new float[w * h];
        double fx = (double)sw / w, fy = (double)sh / h;

        Parallel.For(0, h, ty =>
        {
            double sy = Math.Clamp((ty + 0.5) * fy - 0.5, 0, sh - 1);
            int y0 = (int)sy, y1 = Math.Min(y0 + 1, sh - 1);
            float wy = (float)(sy - y0);
            for (int tx = 0; tx < w; tx++)
            {
                double sx = Math.Clamp((tx + 0.5) * fx - 0.5, 0, sw - 1);
                int x0 = (int)sx, x1 = Math.Min(x0 + 1, sw - 1);
                float wx = (float)(sx - x0);
                float top = src[y0 * sw + x0] * (1 - wx) + src[y0 * sw + x1] * wx;
                float bot = src[y1 * sw + x0] * (1 - wx) + src[y1 * sw + x1] * wx;
                dst[ty * w + tx] = top * (1 - wy) + bot * wy;
            }
        });
        return (dst, w, h);
    }

    /// <summary>Normalised (0..1) Sobel gradient magnitude.</summary>
    private static float[] SobelMagnitude(float[] g, int w, int h)
    {
        var m = new float[w * h];
        float max = 0;
        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                int i = y * w + x;
                float gx = (g[i - w + 1] + 2 * g[i + 1] + g[i + w + 1]) - (g[i - w - 1] + 2 * g[i - 1] + g[i + w - 1]);
                float gy = (g[i + w - 1] + 2 * g[i + w] + g[i + w + 1]) - (g[i - w - 1] + 2 * g[i - w] + g[i - w + 1]);
                float v = MathF.Sqrt(gx * gx + gy * gy);
                m[i] = v;
                if (v > max) max = v;
            }
        }
        if (max > 0)
            for (int i = 0; i < m.Length; i++) m[i] /= max;
        return m;
    }
}
