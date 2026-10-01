using System.Numerics;

namespace ImageConverter.Core.Tsp;

/// <summary>Image -> density map -> stipple points -> TSP tour = one continuous line.</summary>
public static class TspArtGenerator
{
    /// <param name="gray">Luminance 0..255, row-major, width*height values.</param>
    public static LineArtResult Generate(float[] gray, int width, int height, TspArtSettings settings,
                                        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        int count = settings.ResolvePointCount(width, height);
        progress?.Report($"Building density map for {count:N0} points…");
        var map = DensityMap.FromGray(gray, width, height, settings, count);

        var points = Stippler.Generate(map, count, Math.Max(0, settings.Iterations),
                                       settings.Seed, progress, ct);

        int[] order = TourSolver.Solve(points, settings.Neighbours, progress, ct);

        var path = new Vector2[order.Length];
        for (int i = 0; i < order.Length; i++) path[i] = points[order[i]];

        return new LineArtResult(path, map.Width, map.Height, TourSolver.PathLength(points, order));
    }
}
