using System.Numerics;

namespace ImageConverter.Core;

/// <summary>
/// Ramer–Douglas–Peucker simplification: removes points that lie within <c>tolerance</c> of the
/// straight line through their neighbours. Iterative (explicit stack), so it works for millions of points.
/// </summary>
public static class PathSimplifier
{
    public static Vector2[] Simplify(IReadOnlyList<Vector2> path, float tolerance) =>
        Simplify(path, tolerance, null);

    /// <param name="fixedPoints">Optional: points that must be kept (the path is simplified between them).</param>
    public static Vector2[] Simplify(IReadOnlyList<Vector2> path, float tolerance, bool[]? fixedPoints)
    {
        int n = path.Count;
        if (n <= 2 || tolerance <= 0) return path.ToArray();

        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        float tol2 = tolerance * tolerance;

        var stack = new Stack<(int first, int last)>();
        int start = 0;
        for (int i = 1; i < n; i++)
        {
            if (i == n - 1 || (fixedPoints != null && fixedPoints[i]))
            {
                keep[i] = true;
                stack.Push((start, i));
                start = i;
            }
        }
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            if (last - first < 2) continue;

            Vector2 a = path[first], b = path[last];
            Vector2 ab = b - a;
            float len2 = ab.LengthSquared();

            int index = -1;
            float maxDist2 = tol2;
            for (int i = first + 1; i < last; i++)
            {
                float d2 = DistanceToSegmentSquared(path[i], a, ab, len2);
                if (d2 > maxDist2) { maxDist2 = d2; index = i; }
            }

            if (index >= 0)
            {
                keep[index] = true;
                stack.Push((first, index));
                stack.Push((index, last));
            }
        }

        var result = new List<Vector2>(n / 4);
        for (int i = 0; i < n; i++)
            if (keep[i]) result.Add(path[i]);
        return result.ToArray();
    }

    private static float DistanceToSegmentSquared(Vector2 p, Vector2 a, Vector2 ab, float len2)
    {
        if (len2 <= 1e-12f) return Vector2.DistanceSquared(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.DistanceSquared(p, a + ab * t);
    }

    public static double Length(IReadOnlyList<Vector2> path)
    {
        double len = 0;
        for (int i = 1; i < path.Count; i++) len += Vector2.Distance(path[i - 1], path[i]);
        return len;
    }
}
