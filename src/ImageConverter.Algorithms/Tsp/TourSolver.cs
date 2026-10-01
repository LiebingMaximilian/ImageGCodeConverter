using System.Numerics;

namespace ImageConverter.Core.Tsp;

/// <summary>
/// Approximate travelling-salesman solver:
/// nearest-neighbour construction + 2-opt with neighbour lists and don't-look bits.
/// 2-opt removes (practically all) self-crossings, which is what makes TSP art look clean.
/// </summary>
public static class TourSolver
{
    /// <summary>Returns the visiting order as an open path (the longest edge of the round trip is dropped).</summary>
    public static int[] Solve(Vector2[] pts, int neighbours = 10,
                              IProgress<string>? progress = null, CancellationToken ct = default)
    {
        int n = pts.Length;
        if (n <= 3) return Enumerable.Range(0, n).ToArray();

        var (minX, minY, maxX, maxY) = Bounds(pts);
        var shifted = pts.Select(p => new Vector2(p.X - minX, p.Y - minY)).ToArray();
        float width = MathF.Max(1, maxX - minX), height = MathF.Max(1, maxY - minY);

        progress?.Report("Tour: nearest neighbour construction…");
        int[] tour = NearestNeighbourTour(shifted, width, height);

        progress?.Report("Tour: building neighbour lists…");
        int k = Math.Clamp(neighbours, 2, Math.Min(32, n - 1));
        int[] neigh = BuildNeighbourLists(shifted, width, height, k);

        progress?.Report("Tour: 2-opt optimisation…");
        TwoOpt(shifted, tour, neigh, k, progress, ct);

        return OpenAtLongestEdge(shifted, tour);
    }

    public static double PathLength(Vector2[] pts, int[] order)
    {
        double len = 0;
        for (int i = 1; i < order.Length; i++) len += Vector2.Distance(pts[order[i - 1]], pts[order[i]]);
        return len;
    }

    // ------------------------------------------------------------------

    private static int[] NearestNeighbourTour(Vector2[] pts, float width, float height)
    {
        int n = pts.Length;
        var grid = new PointGrid(pts, width, height, 2f);
        var tour = new int[n];

        int current = 0;
        // start in the top-left-most point – feels natural for a plotter
        for (int i = 1; i < n; i++)
            if (pts[i].X + pts[i].Y < pts[current].X + pts[current].Y) current = i;

        for (int i = 0; i < n; i++)
        {
            tour[i] = current;
            grid.Remove(current);
            if (i < n - 1) current = grid.Nearest(pts[current].X, pts[current].Y);
        }
        return tour;
    }

    private static int[] BuildNeighbourLists(Vector2[] pts, float width, float height, int k)
    {
        int n = pts.Length;
        var grid = new PointGrid(pts, width, height, 2f);
        var neigh = new int[n * k];
        Parallel.For(0, n, () => (new int[k], new float[k]), (i, _, buf) =>
        {
            int found = grid.KNearest(i, buf.Item1, buf.Item2);
            for (int j = 0; j < k; j++) neigh[i * k + j] = j < found ? buf.Item1[j] : -1;
            return buf;
        }, _ => { });
        return neigh;
    }

    /// <summary>Array-based tour with O(1) next/prev and segment reversal.</summary>
    private sealed class Tour
    {
        public readonly int[] Order, Pos;
        public readonly int N;

        public Tour(int[] order)
        {
            Order = order;
            N = order.Length;
            Pos = new int[N];
            for (int i = 0; i < N; i++) Pos[order[i]] = i;
        }

        public int Next(int c) => Order[Pos[c] + 1 == N ? 0 : Pos[c] + 1];
        public int Prev(int c) => Order[Pos[c] == 0 ? N - 1 : Pos[c] - 1];

        /// <summary>Reverses positions i..j (forward, cyclic). Reverses the shorter side
        /// instead when that is cheaper – as an undirected cycle the result is identical.</summary>
        public void Reverse(int i, int j)
        {
            int n = N;
            int len = ((j - i + n) % n) + 1;
            if (len * 2 > n)
            {
                int ni = (j + 1) % n, nj = (i - 1 + n) % n;
                i = ni; j = nj; len = n - len;
            }
            for (int s = 0; s < len / 2; s++)
            {
                int a = Order[i], b = Order[j];
                Order[i] = b; Pos[b] = i;
                Order[j] = a; Pos[a] = j;
                i = i + 1 == n ? 0 : i + 1;
                j = j == 0 ? n - 1 : j - 1;
            }
        }

        /// <summary>2-opt move on edges (a,b) and (c,d), given in any orientation:
        /// replaces them with (a,c) and (b,d).</summary>
        public bool Exchange(int a, int b, int c, int d)
        {
            // orient both edges forward
            if (Next(a) != b) { if (Next(b) != a) return false; (a, b) = (b, a); }
            if (Next(c) != d) { if (Next(d) != c) return false; (c, d) = (d, c); }
            if (a == c || a == d || b == c || b == d) return false;
            Reverse(Pos[b], Pos[c]);           // a b … c d  ->  a c … b d
            return true;
        }
    }

    private static void TwoOpt(Vector2[] p, int[] order, int[] neigh, int k,
                               IProgress<string>? progress, CancellationToken ct)
    {
        var tour = new Tour(order);
        var queue = new Queue<int>(order);
        var queued = new bool[tour.N];
        Array.Fill(queued, true);

        for (int round = 0; ; round++)
        {
            RunQueue(p, tour, neigh, k, queue, queued, progress, ct);

            // Neighbour lists can miss a few crossings involving long edges (sparse areas).
            // Find every remaining crossing and uncross it with an exact 2-opt move.
            var crossings = FindCrossings(p, tour);
            if (crossings.Count == 0 || round > 200) break;
            progress?.Report($"Tour: removing {crossings.Count} crossing(s)…");

            foreach (var (a, b, c, d) in crossings)
            {
                if (Intersect(p[a], p[b], p[c], p[d]) && tour.Exchange(a, b, c, d))
                    foreach (int x in new[] { a, b, c, d })
                        if (!queued[x]) { queued[x] = true; queue.Enqueue(x); }
            }
        }
    }

    private static void RunQueue(Vector2[] p, Tour tour, int[] neigh, int k, Queue<int> queue, bool[] queued,
                                 IProgress<string>? progress, CancellationToken ct)
    {
        const float eps = 1e-5f;
        long improvements = 0, steps = 0;
        float D(int a, int b) => Vector2.Distance(p[a], p[b]);

        void Push(int c)
        {
            if (!queued[c]) { queued[c] = true; queue.Enqueue(c); }
        }

        while (queue.Count > 0)
        {
            int a = queue.Dequeue();
            queued[a] = false;
            if ((++steps & 0xFFF) == 0) ct.ThrowIfCancellationRequested();

            bool improved = false;

            // Direction 1: edge (a, next(a))
            int b = tour.Next(a);
            float dab = D(a, b);
            for (int t = 0; t < k && !improved; t++)
            {
                int c = neigh[a * k + t];
                if (c < 0) break;
                float g1 = dab - D(a, c);
                if (g1 <= eps) break;                // neighbours are sorted: no further gain possible
                int d = tour.Next(c);
                if (c == b || d == a) continue;
                if (g1 + D(c, d) - D(b, d) > eps)
                {
                    tour.Reverse(tour.Pos[b], tour.Pos[c]);   // a b … c d  ->  a c … b d
                    Push(a); Push(b); Push(c); Push(d);
                    improved = true;
                }
            }

            // Direction 2: edge (prev(a), a)
            if (!improved)
            {
                b = tour.Prev(a);
                dab = D(a, b);
                for (int t = 0; t < k && !improved; t++)
                {
                    int c = neigh[a * k + t];
                    if (c < 0) break;
                    float g1 = dab - D(a, c);
                    if (g1 <= eps) break;
                    int d = tour.Prev(c);
                    if (c == b || d == a) continue;
                    if (g1 + D(c, d) - D(b, d) > eps)
                    {
                        tour.Reverse(tour.Pos[a], tour.Pos[d]);   // a … d c … b  ->  d … a c … b
                        Push(a); Push(b); Push(c); Push(d);
                        improved = true;
                    }
                }
            }

            if (improved && (++improvements % 20000) == 0)
                progress?.Report($"Tour: 2-opt, {improvements:N0} improvements…");
        }
    }

    /// <summary>All pairs of tour edges that properly intersect (segment grid, ~O(n)).</summary>
    private static List<(int a, int b, int c, int d)> FindCrossings(Vector2[] p, Tour tour)
    {
        int n = tour.N;
        var (minX, minY, maxX, maxY) = Bounds(p);
        float cell = MathF.Max(1e-3f, MathF.Sqrt((maxX - minX + 1) * (maxY - minY + 1) / n) * 2f);
        int cols = (int)((maxX - minX) / cell) + 1, rows = (int)((maxY - minY) / cell) + 1;

        // edge e = (Order[e], Order[e+1])
        var buckets = new Dictionary<int, List<int>>();
        for (int e = 0; e < n; e++)
        {
            Vector2 u = p[tour.Order[e]], v = p[tour.Order[(e + 1) % n]];
            int x0 = (int)((MathF.Min(u.X, v.X) - minX) / cell), x1 = (int)((MathF.Max(u.X, v.X) - minX) / cell);
            int y0 = (int)((MathF.Min(u.Y, v.Y) - minY) / cell), y1 = (int)((MathF.Max(u.Y, v.Y) - minY) / cell);
            for (int gy = y0; gy <= y1; gy++)
                for (int gx = x0; gx <= x1; gx++)
                {
                    int key = gy * cols + gx;
                    if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>(4);
                    list.Add(e);
                }
        }

        var seen = new HashSet<long>();
        var result = new List<(int, int, int, int)>();
        foreach (var list in buckets.Values)
        {
            for (int i = 0; i < list.Count; i++)
                for (int j = i + 1; j < list.Count; j++)
                {
                    int e1 = Math.Min(list[i], list[j]), e2 = Math.Max(list[i], list[j]);
                    int a = tour.Order[e1], b = tour.Order[(e1 + 1) % n];
                    int c = tour.Order[e2], d = tour.Order[(e2 + 1) % n];
                    if (a == c || a == d || b == c || b == d) continue;   // adjacent edges
                    if (!Intersect(p[a], p[b], p[c], p[d])) continue;
                    if (seen.Add(((long)e1 << 32) | (uint)e2)) result.Add((a, b, c, d));
                }
        }
        return result;
    }

    /// <summary>True if segments ab and cd properly cross (touching endpoints don't count).</summary>
    private static bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        static float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        float o1 = Cross(a, b, c), o2 = Cross(a, b, d), o3 = Cross(c, d, a), o4 = Cross(c, d, b);
        return ((o1 > 0 && o2 < 0) || (o1 < 0 && o2 > 0)) && ((o3 > 0 && o4 < 0) || (o3 < 0 && o4 > 0));
    }

    private static int[] OpenAtLongestEdge(Vector2[] p, int[] tour)
    {
        int n = tour.Length;
        int cut = 0;
        float longest = -1;
        for (int i = 0; i < n; i++)
        {
            float d = Vector2.DistanceSquared(p[tour[i]], p[tour[(i + 1) % n]]);
            if (d > longest) { longest = d; cut = i; }
        }
        var path = new int[n];
        for (int i = 0; i < n; i++) path[i] = tour[(cut + 1 + i) % n];
        return path;
    }

    private static (float, float, float, float) Bounds(Vector2[] pts)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var q in pts)
        {
            minX = MathF.Min(minX, q.X); minY = MathF.Min(minY, q.Y);
            maxX = MathF.Max(maxX, q.X); maxY = MathF.Max(maxY, q.Y);
        }
        return (minX, minY, maxX, maxY);
    }
}
