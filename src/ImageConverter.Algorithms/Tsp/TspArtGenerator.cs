using System.Numerics;

namespace ImageConverter.Core.Tsp;

/// <param name="Path">Points in drawing order, in working-image pixel coordinates.</param>
/// <param name="Width">Working image width (pixels) – the coordinate space of <see cref="Path"/>.</param>
/// <param name="Height">Working image height (pixels).</param>
public sealed record TspArtResult(Vector2[] Path, int Width, int Height, double LengthPx);

/// <summary>Image -> density map -> stipple points -> TSP tour = one continuous line.</summary>
public static class TspArtGenerator
{
    /// <param name="gray">Luminance 0..255, row-major, width*height values.</param>
    public static TspArtResult Generate(float[] gray, int width, int height, TspArtSettings settings,
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

        return new TspArtResult(path, map.Width, map.Height, TourSolver.PathLength(points, order));
    }
}
