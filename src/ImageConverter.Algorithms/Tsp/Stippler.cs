using System.Numerics;

namespace ImageConverter.Core.Tsp;

/// <summary>
/// Weighted Voronoi stippling (Secord 2002):
/// 1. scatter points with probability proportional to the density map,
/// 2. repeatedly move every point to the density-weighted centroid of its Voronoi cell (Lloyd relaxation).
/// The result is an evenly spaced dot pattern whose local density follows the image darkness.
/// </summary>
public static class Stippler
{
    public static Vector2[] Generate(DensityMap map, int count, int iterations, int seed,
                                     IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (count < 2) throw new ArgumentOutOfRangeException(nameof(count), "Need at least 2 points.");

        int w = map.Width, h = map.Height;
        float[] v = map.Values;
        float maxV = 0;
        for (int i = 0; i < v.Length; i++) if (v[i] > maxV) maxV = v[i];
        if (maxV <= 0) throw new InvalidOperationException("The image has no dark areas to draw (try lowering 'White cutoff').");

        var rng = new Random(seed);
        var pts = new Vector2[count];
        for (int i = 0; i < count; i++) pts[i] = SamplePoint(v, w, h, maxV, rng);

        // Pixels that can receive ink, and which point (Voronoi cell) each one belongs to.
        int[] inked = Enumerable.Range(0, v.Length).Where(i => v[i] > 0).ToArray();
        var owner = new int[inked.Length];
        var total = new double[count * 3];

        for (int it = 0; it < iterations; it++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Stippling {count:N0} points ({w}×{h} px): iteration {it + 1}/{iterations}");

            var grid = new PointGrid(pts, w, h, 1f);

            // 1. Voronoi assignment: nearest point for every inked pixel (parallel, read-only grid).
            Parallel.For(0, (inked.Length + 4095) / 4096, chunk =>
            {
                int end = Math.Min(inked.Length, (chunk + 1) * 4096);
                for (int j = chunk * 4096; j < end; j++)
                {
                    int i = inked[j];
                    owner[j] = grid.Nearest(i % w + 0.5f, i / w + 0.5f);
                }
            });

            // 2. Density-weighted centroid per cell (sequential, cheap; no per-thread buffers -> low memory).
            Array.Clear(total);
            for (int j = 0; j < inked.Length; j++)
            {
                int i = inked[j];
                float d = v[i];
                int k = owner[j] * 3;
                total[k] += d;
                total[k + 1] += d * (i % w + 0.5f);
                total[k + 2] += d * (i / w + 0.5f);
            }

            for (int i = 0; i < count; i++)
            {
                double m = total[i * 3];
                pts[i] = m > 1e-9
                    ? new Vector2((float)(total[i * 3 + 1] / m), (float)(total[i * 3 + 2] / m))
                    : SamplePoint(v, w, h, maxV, rng);   // stranded in a blank area -> re-seed
            }
        }
        return pts;
    }

    /// <summary>Rejection sampling: a random pixel is accepted with probability density / maxDensity.</summary>
    private static Vector2 SamplePoint(float[] v, int w, int h, float maxV, Random rng)
    {
        while (true)
        {
            int x = rng.Next(w), y = rng.Next(h);
            if (rng.NextSingle() * maxV < v[y * w + x])
                return new Vector2(x + rng.NextSingle(), y + rng.NextSingle());
        }
    }
}
