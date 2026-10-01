using System.Drawing;
using System.Drawing.Drawing2D;

namespace ImageConverter.Core.Tsp;

/// <summary>System.Drawing-dependent TSP-art helpers (Bitmap in, preview Bitmap out).</summary>
public static class TspArtBitmap
{
    public static TspArtResult Generate(Bitmap bitmap, TspArtSettings settings,
                                        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        float[] gray = BitmapPixels.ToGrayscale(bitmap);
        return TspArtGenerator.Generate(gray, bitmap.Width, bitmap.Height, settings, progress, ct);
    }

    /// <summary>Draws the single line black on white, so you can check it before plotting.</summary>
    public static Bitmap RenderPreview(TspArtResult result, float scale = 2f, float lineWidth = 1f, int maxSize = 6000)
    {
        scale = Math.Max(0.01f, scale);
        float longSide = Math.Max(result.Width, result.Height) * scale;
        if (longSide > maxSize) scale *= Math.Max(64, maxSize) / longSide;
        int w = Math.Max(1, (int)Math.Ceiling(result.Width * scale));
        int h = Math.Max(1, (int)Math.Ceiling(result.Height * scale));

        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (result.Path.Length >= 2)
        {
            var pts = result.Path.Select(p => new PointF(p.X * scale, p.Y * scale)).ToArray();
            using var pen = new Pen(Color.Black, lineWidth) { LineJoin = LineJoin.Round };
            g.DrawLines(pen, pts);

            // start (green) / end (red) markers
            float r = Math.Max(3f, 3f * lineWidth);
            g.FillEllipse(Brushes.LimeGreen, pts[0].X - r, pts[0].Y - r, 2 * r, 2 * r);
            g.FillEllipse(Brushes.Red, pts[^1].X - r, pts[^1].Y - r, 2 * r, 2 * r);
        }
        return bmp;
    }
}
