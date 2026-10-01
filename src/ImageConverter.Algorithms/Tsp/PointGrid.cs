using System.Numerics;

namespace ImageConverter.Core.Tsp;

/// <summary>
/// Uniform grid spatial index for fast nearest-neighbour queries.
/// Supports removing points (used by the nearest-neighbour tour construction).
/// </summary>
internal sealed class PointGrid
{
    private readonly Vector2[] _pts;
    private readonly float _cell;
    private readonly int _cols, _rows;
    private readonly int[] _cellStart;   // CSR: items of cell c are _items[_cellStart[c] .. _cellStart[c] + _cellCount[c])
    private readonly int[] _cellCount;
    private readonly int[] _items;
    private readonly int[] _slot;        // index of point i inside _items (for O(1) removal)

    public PointGrid(Vector2[] pts, float width, float height, float pointsPerCell = 2f)
    {
        _pts = pts;
        int n = Math.Max(1, pts.Length);
        _cell = MathF.Max(1e-3f, MathF.Sqrt(width * height * pointsPerCell / n));
        _cols = Math.Max(1, (int)MathF.Ceiling(width / _cell));
        _rows = Math.Max(1, (int)MathF.Ceiling(height / _cell));

        int cells = _cols * _rows;
        _cellStart = new int[cells + 1];
        _cellCount = new int[cells];
        _items = new int[pts.Length];
        _slot = new int[pts.Length];

        var cellOf = new int[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            cellOf[i] = CellIndex(pts[i].X, pts[i].Y);
            _cellCount[cellOf[i]]++;
        }
        for (int c = 0; c < cells; c++) _cellStart[c + 1] = _cellStart[c] + _cellCount[c];

        var fill = new int[cells];
        for (int i = 0; i < pts.Length; i++)
        {
            int c = cellOf[i];
            int s = _cellStart[c] + fill[c]++;
            _items[s] = i;
            _slot[i] = s;
        }
    }

    private int ClampX(float x) => Math.Clamp((int)(x / _cell), 0, _cols - 1);
    private int ClampY(float y) => Math.Clamp((int)(y / _cell), 0, _rows - 1);
    private int CellIndex(float x, float y) => ClampY(y) * _cols + ClampX(x);

    /// <summary>Removes point i from the index (it will not be returned by queries anymore).</summary>
    public void Remove(int i)
    {
        int c = CellIndex(_pts[i].X, _pts[i].Y);
        int last = _cellStart[c] + _cellCount[c] - 1;
        int s = _slot[i];
        int moved = _items[last];
        _items[s] = moved;
        _slot[moved] = s;
        _items[last] = i;
        _slot[i] = last;
        _cellCount[c]--;
    }

    /// <summary>Nearest remaining point to (x, y), or -1 if the grid is empty.</summary>
    public int Nearest(float x, float y)
    {
        int cx = ClampX(x), cy = ClampY(y);
        int best = -1;
        float bestD = float.MaxValue;
        int maxR = Math.Max(_cols, _rows);

        for (int r = 0; r <= maxR; r++)
        {
            // Everything in ring r is at least (r - 1) * cell away.
            if (r > 0 && best >= 0)
            {
                float bound = (r - 1) * _cell;
                if (bestD <= bound * bound) break;
            }
            ScanRing(cx, cy, r, x, y, ref best, ref bestD, -1);
        }
        return best;
    }

    /// <summary>Fills <paramref name="result"/> with up to k nearest neighbours of point idx (sorted, excluding idx).</summary>
    public int KNearest(int idx, Span<int> result, Span<float> dist)
    {
        int k = result.Length;
        int found = 0;
        float x = _pts[idx].X, y = _pts[idx].Y;
        int cx = ClampX(x), cy = ClampY(y);
        int maxR = Math.Max(_cols, _rows);

        for (int r = 0; r <= maxR; r++)
        {
            if (r > 0 && found == k)
            {
                float bound = (r - 1) * _cell;
                if (dist[k - 1] <= bound * bound) break;
            }

            int x0 = cx - r, x1 = cx + r, y0 = cy - r, y1 = cy + r;
            for (int gy = y0; gy <= y1; gy++)
            {
                if (gy < 0 || gy >= _rows) continue;
                bool edgeRow = gy == y0 || gy == y1;
                int step = edgeRow ? 1 : Math.Max(1, x1 - x0);
                for (int gx = x0; gx <= x1; gx += step)
                {
                    if (gx < 0 || gx >= _cols) continue;
                    int c = gy * _cols + gx;
                    int start = _cellStart[c], end = start + _cellCount[c];
                    for (int s = start; s < end; s++)
                    {
                        int j = _items[s];
                        if (j == idx) continue;
                        float d = Vector2.DistanceSquared(_pts[j], _pts[idx]);
                        if (found < k)
                        {
                            Insert(result, dist, found, j, d);
                            found++;
                        }
                        else if (d < dist[k - 1])
                        {
                            Insert(result, dist, k - 1, j, d);
                        }
                    }
                }
            }
        }
        return found;

        static void Insert(Span<int> res, Span<float> dst, int count, int j, float d)
        {
            int p = count;
            while (p > 0 && dst[p - 1] > d)
            {
                if (p < res.Length) { res[p] = res[p - 1]; dst[p] = dst[p - 1]; }
                p--;
            }
            res[p] = j;
            dst[p] = d;
        }
    }

    private void ScanRing(int cx, int cy, int r, float x, float y, ref int best, ref float bestD, int exclude)
    {
        int x0 = cx - r, x1 = cx + r, y0 = cy - r, y1 = cy + r;
        for (int gy = y0; gy <= y1; gy++)
        {
            if (gy < 0 || gy >= _rows) continue;
            bool edgeRow = gy == y0 || gy == y1;
            int step = edgeRow ? 1 : Math.Max(1, x1 - x0);   // inner rows: only left & right cell
            for (int gx = x0; gx <= x1; gx += step)
            {
                if (gx < 0 || gx >= _cols) continue;
                int c = gy * _cols + gx;
                int start = _cellStart[c], end = start + _cellCount[c];
                for (int s = start; s < end; s++)
                {
                    int j = _items[s];
                    if (j == exclude) continue;
                    float dx = _pts[j].X - x, dy = _pts[j].Y - y;
                    float d = dx * dx + dy * dy;
                    if (d < bestD) { bestD = d; best = j; }
                }
            }
        }
    }
}
