using System.Globalization;
using System.Numerics;
using System.Text;

namespace ImageConverter.Core.GCode;

public sealed record GCodeResult(string Code, double DrawLengthMm, double WidthMm, double HeightMm,
                                 int Moves, TimeSpan EstimatedTime);

/// <summary>
/// Turns one continuous polyline into plotter G-code:
/// pen up → travel to start → pen down (once!) → G1 through every point → pen up.
/// </summary>
public static class GCodeWriter
{
    /// <param name="path">Points in drawing order (image pixel coordinates, Y down).</param>
    /// <param name="sourceWidth">Width of the coordinate space of <paramref name="path"/>.</param>
    /// <param name="sourceHeight">Height of the coordinate space of <paramref name="path"/>.</param>
    public static GCodeResult Write(IReadOnlyList<Vector2> path, double sourceWidth, double sourceHeight,
                                    GCodeSettings s, string? title = null)
    {
        if (path.Count == 0) throw new ArgumentException("Path is empty.", nameof(path));
        if (string.IsNullOrWhiteSpace(s.PenDownCommand))
            throw new ArgumentException("The 'pen down' command must not be empty – otherwise nothing would be drawn.");
        if (string.IsNullOrWhiteSpace(s.PenUpCommand))
            throw new ArgumentException("The 'pen up' command must not be empty.");
        if (s.WidthMm <= 0 || s.HeightMm <= 0)
            throw new ArgumentException("Drawing width and height must be greater than 0 mm.");

        // Fit the bounding box of the path into the machine area.
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in path)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        double bw = Math.Max(1e-9, maxX - minX), bh = Math.Max(1e-9, maxY - minY);
        double scaleX = s.WidthMm / bw, scaleY = s.HeightMm / bh;
        if (s.KeepAspectRatio) scaleX = scaleY = Math.Min(scaleX, scaleY);
        double outW = bw * scaleX, outH = bh * scaleY;
        double padX = s.Center ? (s.WidthMm - outW) / 2 : 0;
        double padY = s.Center ? (s.HeightMm - outH) / 2 : 0;

        (double x, double y) ToMm(Vector2 p)
        {
            double x = (p.X - minX) * scaleX;
            double y = s.FlipY ? (maxY - p.Y) * scaleY : (p.Y - minY) * scaleY;
            return (x + padX + s.OffsetXMm, y + padY + s.OffsetYMm);
        }

        string fmt = "0." + new string('#', Math.Clamp(s.Decimals, 0, 6));
        string F(double v) => v.ToString(fmt, CultureInfo.InvariantCulture);   // always '.' as decimal separator

        var sb = new StringBuilder(path.Count * 24);
        void Line(string l) => sb.Append(l).Append('\n');
        void PenDelay() { if (s.PenDelayMs > 0) Line($"G4 P{F(s.PenDelayMs / 1000.0)}"); }

        Line($"; {Ascii(title ?? "ImageConverter")} - single line, {path.Count} points");
        Line($"; size {F(outW)} x {F(outH)} mm, generated {DateTime.Now:yyyy-MM-dd HH:mm}");
        foreach (var h in s.HeaderLines ?? []) if (!string.IsNullOrWhiteSpace(h)) Line(h.Trim());

        Line("; pen up");
        Line(s.PenUpCommand.Trim());
        PenDelay();

        var (sx, sy) = ToMm(path[0]);
        Line("; travel to start point");
        Line($"G1 X{F(sx)} Y{F(sy)} F{F(s.TravelFeedRate)}");
        Line("; pen down - the pen stays down for the whole line");
        Line(s.PenDownCommand.Trim());
        PenDelay();
        Line("; draw");

        double length = 0;
        int moves = 0;
        double lx = sx, ly = sy;
        bool first = true;
        double minSeg = Math.Max(0, s.MinSegmentMm);

        for (int i = 1; i < path.Count; i++)
        {
            var (x, y) = ToMm(path[i]);
            double seg = Math.Sqrt((x - lx) * (x - lx) + (y - ly) * (y - ly));
            if (seg < minSeg && i < path.Count - 1) continue;

            Line(first ? $"G1 X{F(x)} Y{F(y)} F{F(s.FeedRate)}" : $"G1 X{F(x)} Y{F(y)}");
            first = false;
            length += seg;
            moves++;
            lx = x; ly = y;
        }

        Line("; pen up - done");
        Line(s.PenUpCommand.Trim());
        PenDelay();
        foreach (var f in s.FooterLines ?? []) if (!string.IsNullOrWhiteSpace(f)) Line(f.Trim());

        var time = TimeSpan.FromMinutes(length / Math.Max(1, s.FeedRate));
        return new GCodeResult(sb.ToString(), length, outW, outH, moves, time);
    }

    /// <summary>G-code comments are kept plain ASCII – some senders/controllers choke on anything else.</summary>
    private static string Ascii(string text) =>
        new(text.Select(c => c is >= ' ' and <= '~' && c != '(' && c != ')' ? c : '_').ToArray());
}
