using System.ComponentModel;

namespace ImageConverter.Core.Tsp;

/// <summary>Parameters for the TSP-art (single continuous line) generator.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class TspArtSettings
{
    [Category("Points"), DisplayName("Point count")]
    [Description("Number of stipple points the line passes through. More points = more detail, longer computing and drawing time. Up to several million are possible.")]
    public int PointCount { get; set; } = 8000;

    [Category("Points"), DisplayName("Automatic point count")]
    [Description("If true, the point count is derived from the input image size: pixels ÷ 'Pixels per point', clamped to min/max. 'Point count' is then ignored.")]
    public bool AutoPointCount { get; set; }

    [Category("Points"), DisplayName("Auto: pixels per point")]
    [Description("Automatic mode: one point per this many input pixels (smaller = more points). E.g. 12 MP ÷ 20 = 600 000 points. Default comes from TspDefaults.AutoPixelsPerPoint.")]
    public int AutoPixelsPerPoint { get; set; } = TspDefaults.AutoPixelsPerPoint;

    [Category("Points"), DisplayName("Auto: minimum points")]
    public int AutoMinPoints { get; set; } = TspDefaults.AutoMinPoints;

    [Category("Points"), DisplayName("Auto: maximum points")]
    [Description("Upper limit so huge photos don't produce hour-long drawings.")]
    public int AutoMaxPoints { get; set; } = TspDefaults.AutoMaxPoints;

    [Category("Points"), DisplayName("Relaxation iterations")]
    [Description("Lloyd iterations of weighted Voronoi stippling. 0 = random dots (grainy), 20–50 = evenly spread dots (much nicer line).")]
    public int Iterations { get; set; } = 30;

    [Category("Points"), DisplayName("Random seed")]
    [Description("Same seed + same settings = same result.")]
    public int Seed { get; set; } = 42;

    [Category("Image"), DisplayName("Working size (px)")]
    [Description("Minimum working resolution (longest side). It is raised automatically for high point counts, see 'Min pixels per point'.")]
    public int WorkingSize { get; set; } = 800;

    [Category("Image"), DisplayName("Min pixels per point")]
    [Description("Stippling needs several pixels per point to place them well. The working image is enlarged until it has at least points × this many pixels (16 is good, lower = faster).")]
    public int MinPixelsPerPoint { get; set; } = 16;

    [Category("Image"), DisplayName("Max working pixels (MP)")]
    [Description("Memory/time safety limit for the working image, in megapixels.")]
    public int MaxWorkingMegapixels { get; set; } = 40;

    [Category("Image"), DisplayName("Gamma")]
    [Description(">1 pushes points into dark areas (more contrast), <1 spreads them into mid-tones.")]
    public float Gamma { get; set; } = 2.0f;

    [Category("Image"), DisplayName("White cutoff")]
    [Description("Darkness (0–1) below which an area gets no points at all. Keeps backgrounds clean.")]
    public float WhiteCutoff { get; set; } = 0.08f;

    [Category("Image"), DisplayName("Edge weight")]
    [Description("0 = pure shading. Up to 1 = blend in Sobel edge strength so contours get extra points and stay recognisable.")]
    public float EdgeWeight { get; set; } = 0.15f;

    [Category("Image"), DisplayName("Invert")]
    [Description("Put points in bright areas instead of dark ones (e.g. white pen on black paper).")]
    public bool Invert { get; set; }

    [Category("Tour"), DisplayName("Neighbour candidates")]
    [Description("How many nearest neighbours 2-opt considers per point. 8–12 is a good balance.")]
    public int Neighbours { get; set; } = 10;

    [Category("Preview"), DisplayName("Preview scale")]
    [Description("Preview PNG size relative to the working size.")]
    public float PreviewScale { get; set; } = 2f;

    [Category("Preview"), DisplayName("Preview max size (px)")]
    [Description("Longest side of the preview PNG is limited to this.")]
    public int PreviewMaxSize { get; set; } = 6000;

    [Category("Preview"), DisplayName("Preview line width")]
    public float PreviewLineWidth { get; set; } = 1f;

    /// <summary>The point count actually used for an input image of the given size.</summary>
    public int ResolvePointCount(int imageWidth, int imageHeight)
    {
        if (!AutoPointCount) return Math.Max(2, PointCount);

        long pixels = (long)imageWidth * imageHeight;
        long count = pixels / Math.Max(1, AutoPixelsPerPoint);
        int min = Math.Max(2, AutoMinPoints);
        int max = Math.Max(min, AutoMaxPoints);
        return (int)Math.Clamp(count, min, max);
    }

    public override string ToString() =>
        (AutoPointCount ? "auto points" : $"{PointCount} points") + $", {Iterations} iterations";
}
