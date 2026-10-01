using System.Globalization;
using System.Numerics;
using System.Text;

namespace ImageConverter.Core.Export;

/// <summary>Writes the single line as an SVG polyline, sized in millimetres.</summary>
public static class SvgWriter
{
    public static string Write(IReadOnlyList<Vector2> path, double widthMm, double heightMm,
                               bool keepAspectRatio = true, double strokeMm = 0.3)
    {
        if (path.Count == 0) throw new ArgumentException("Path is empty.", nameof(path));

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in path)
        {
            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
            minY = MathF.Min(minY, p.Y); maxY = MathF.Max(maxY, p.Y);
        }
        float bw = MathF.Max(1e-3f, maxX - minX), bh = MathF.Max(1e-3f, maxY - minY);

        // stroke width in path units so it is ~strokeMm on paper
        double unitsPerMm = Math.Max(bw / widthMm, bh / heightMm);
        var ci = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.##", ci);

        var sb = new StringBuilder(path.Count * 16 + 512);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(widthMm)}mm\" height=\"{F(heightMm)}mm\" ");
        sb.Append($"viewBox=\"{F(minX)} {F(minY)} {F(bw)} {F(bh)}\" ");
        sb.Append(keepAspectRatio ? "preserveAspectRatio=\"xMidYMid meet\">\n" : "preserveAspectRatio=\"none\">\n");
        // stretched drawings use a non-scaling stroke, whose width is in CSS px (96 per inch)
        double strokeWidth = keepAspectRatio ? strokeMm * unitsPerMm : strokeMm * 96 / 25.4;
        sb.Append($"<polyline fill=\"none\" stroke=\"#000\" stroke-width=\"{strokeWidth.ToString("0.####", ci)}\" ");
        sb.Append("stroke-linejoin=\"round\" stroke-linecap=\"round\"");
        if (!keepAspectRatio) sb.Append(" vector-effect=\"non-scaling-stroke\"");
        sb.Append(" points=\"");
        for (int i = 0; i < path.Count; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(F(path[i].X)).Append(',').Append(F(path[i].Y));
        }
        sb.Append("\"/>\n</svg>\n");
        return sb.ToString();
    }
}
