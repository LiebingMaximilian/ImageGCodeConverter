using System.Numerics;
using ImageConverter.Core.Tsp;

namespace ImageConverter.Core.Spiral;

/// <summary>
/// One Archimedean spiral (r = b·θ) from the image centre outwards. The line wobbles sideways
/// (radially) with an amplitude proportional to the darkness under it, so dark areas get more ink.
/// The wobble stays below half the ring spacing, so neighbouring rings never touch – the result
/// is free of self-crossings by construction.
/// </summary>
public static class SpiralGenerator
{
    private const int PointsPerWave = 12;

    /// <param name="gray">Luminance 0..255, row-major, width*height values.</param>
    public static LineArtResult Generate(float[] gray, int width, int height, SpiralSettings s,
                                         IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Spiral: preparing image…");
        var map = DensityMap.FromGray(gray, width, height, s.WorkingSize,
                                      s.Gamma, s.WhiteCutoff, s.EdgeWeight, s.Invert);
        int w = map.Width, h = map.Height;

        float cx = w / 2f, cy = h / 2f;
        float innerRadius = MathF.Min(w, h) / 2f;
        int rings = Math.Clamp(s.Rings, 1, 5000);
        float spacing = innerRadius / rings;                       // distance between neighbouring rings
        float b = spacing / (2 * MathF.PI);                        // r = b·θ
        float maxRadius = s.FillRectangle
            ? MathF.Sqrt(cx * cx + cy * cy) + spacing              // until the corners are covered
            : innerRadius;

        float maxAmp = Math.Clamp(s.Amplitude, 0f, 0.98f) * spacing / 2f;
        float wavelength = MathF.Max(0.05f, s.Wavelength) * spacing;
        float ds = wavelength / PointsPerWave;                      // constant arc-length step

        // rough size estimate for the list: length of the spiral ≈ π·R²/spacing
        int estimate = (int)Math.Min(50_000_000, Math.PI * maxRadius * maxRadius / spacing / ds + 16);
        var path = new List<Vector2>(estimate);
        path.Add(new Vector2(cx, cy));                              // always start exactly in the middle

        float theta = 0f, phase = 0f;
        int step = 0;
        while (true)
        {
            float r = b * theta;
            if (r > maxRadius) break;

            float cos = MathF.Cos(theta), sin = MathF.Sin(theta);
            float x = cx + r * cos, y = cy + r * sin;

            float d = map.Sample(x, y);
            float freq = s.FrequencyFollowsDarkness ? 0.5f + d : 1f;
            phase += 2 * MathF.PI * freq * ds / wavelength;

            // near the centre the wobble is limited so the first loops don't tangle
            float amp = MathF.Min(maxAmp, r * 0.5f) * d;
            float rr = r + amp * MathF.Sin(phase);

            // Fill rectangle: outside the image the line follows the border (the pen never lifts).
            // Projecting along the ray from the centre maps every ring at the same angle onto the
            // same border point, so the ring order is kept and no crossings can appear.
            var p = new Vector2(cx + rr * cos, cy + rr * sin);
            if (s.FillRectangle)
            {
                float toSide = MathF.Abs(cos) > 1e-6f ? cx / MathF.Abs(cos) : float.MaxValue;   // left/right edge
                float toTopBottom = MathF.Abs(sin) > 1e-6f ? cy / MathF.Abs(sin) : float.MaxValue;
                float border = MathF.Min(toSide, toTopBottom);
                if (rr >= border)
                {
                    // exactly on the border line (no rounding noise), so overlapping border runs stay collinear
                    p = toSide <= toTopBottom
                        ? new Vector2(cos > 0 ? w : 0f, Math.Clamp(cy + border * sin, 0f, h))
                        : new Vector2(Math.Clamp(cx + border * cos, 0f, w), sin > 0 ? h : 0f);
                }
            }

            if (theta > 0) path.Add(p);

            // dθ for a constant arc length: ds = sqrt(r² + b²)·dθ
            theta += ds / MathF.Sqrt(r * r + b * b);

            if ((++step & 0xFFFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Spiral: ring {(int)(r / spacing) + 1} of {(int)Math.Ceiling(maxRadius / spacing)}…");
            }
        }

        progress?.Report($"Spiral: simplifying {path.Count:N0} points…");
        var simplified = s.SimplifyTolerance > 0
            ? Simplify(path, s.SimplifyTolerance * spacing, s.FillRectangle ? spacing : 0f, w, h)
            : path.ToArray();

        return new LineArtResult(simplified, w, h, PathSimplifier.Length(simplified));
    }

    /// <summary>
    /// Douglas–Peucker simplification. In "fill rectangle" mode the rings are squeezed together
    /// where they meet the border, so points within <paramref name="borderBand"/> of the border are
    /// kept exactly (simplifying them could make neighbouring rings touch). Points lying exactly on a
    /// border edge between two other points on the same edge are dropped – that changes nothing.
    /// </summary>
    private static Vector2[] Simplify(List<Vector2> path, float tolerance, float borderBand, float w, float h)
    {
        if (borderBand <= 0) return PathSimplifier.Simplify(path, tolerance);

        static int Edge(Vector2 p, float w, float h) =>
            p.Y == 0 ? 1 : p.Y == h ? 2 : p.X == 0 ? 3 : p.X == w ? 4 : 0;

        // 1. drop collinear points in the middle of a run along one border edge
        var compact = new List<Vector2>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            int e = Edge(path[i], w, h);
            bool middleOfEdgeRun = e != 0 && i > 0 && i < path.Count - 1
                                   && Edge(path[i - 1], w, h) == e && Edge(path[i + 1], w, h) == e;
            if (!middleOfEdgeRun) compact.Add(path[i]);
        }

        // 2. keep every point near the border, simplify the rest
        var fixedPoints = new bool[compact.Count];
        for (int i = 0; i < compact.Count; i++)
        {
            var p = compact[i];
            fixedPoints[i] = MathF.Min(MathF.Min(p.X, w - p.X), MathF.Min(p.Y, h - p.Y)) <= borderBand;
        }
        return PathSimplifier.Simplify(compact, tolerance, fixedPoints);
    }

    /// <summary>Ring spacing on paper in mm for a drawing of the given size (for UI hints).</summary>
    public static double RingSpacingMm(SpiralSettings s, double drawingWidthMm, double drawingHeightMm) =>
        Math.Min(drawingWidthMm, drawingHeightMm) / 2.0 / Math.Max(1, s.Rings);
}
