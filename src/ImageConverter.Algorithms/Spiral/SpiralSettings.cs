using System.ComponentModel;

namespace ImageConverter.Core.Spiral;

/// <summary>Parameters for the spiral mode: one spiral from the centre outwards, shading by a sideways wobble.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class SpiralSettings
{
    [Category("Spiral"), DisplayName("Rings")]
    [Description("Number of turns from the centre to the edge of the circle that fits inside the image. More rings = more detail, thinner spacing.")]
    public int Rings { get; set; } = 90;

    [Category("Spiral"), DisplayName("Fill rectangle")]
    [Description("Off: round picture (the circle inside the image). On: the spiral continues until the whole rectangle is covered; outside the image the line runs along the border, which gives a thin frame.")]
    public bool FillRectangle { get; set; }

    [Category("Spiral"), DisplayName("Amplitude")]
    [Description("Maximum sideways wobble in dark areas, as a fraction of half the ring spacing (0–1). Below 1 the rings never touch.")]
    public float Amplitude { get; set; } = 0.9f;

    [Category("Spiral"), DisplayName("Wavelength")]
    [Description("Length of one wobble relative to the ring spacing. Smaller = finer texture (and more points).")]
    public float Wavelength { get; set; } = 0.6f;

    [Category("Spiral"), DisplayName("Frequency follows darkness")]
    [Description("Dark areas also wobble faster, which makes the darkest tones denser (more contrast).")]
    public bool FrequencyFollowsDarkness { get; set; } = true;

    [Category("Spiral"), DisplayName("Simplify tolerance")]
    [Description("Points that deviate less than this fraction of the ring spacing from a straight line are removed (smaller G-code, same drawing). 0 = keep all points.")]
    public float SimplifyTolerance { get; set; } = 0.02f;

    [Category("Image"), DisplayName("Working size (px)")]
    [Description("The image is resized so its longest side has this many pixels before it is sampled.")]
    public int WorkingSize { get; set; } = 1200;

    [Category("Image"), DisplayName("Gamma")]
    [Description(">1 makes mid-tones lighter (more contrast), <1 makes them darker.")]
    public float Gamma { get; set; } = 1.6f;

    [Category("Image"), DisplayName("White cutoff")]
    [Description("Darkness (0–1) below which the line stays straight.")]
    public float WhiteCutoff { get; set; } = 0.03f;

    [Category("Image"), DisplayName("Edge weight")]
    [Description("0 = pure shading. Up to 1 = blend in Sobel edge strength to emphasise contours.")]
    public float EdgeWeight { get; set; }

    [Category("Image"), DisplayName("Invert")]
    [Description("Wobble in bright areas instead of dark ones (e.g. white pen on black paper).")]
    public bool Invert { get; set; }

    public override string ToString() => $"{Rings} rings" + (FillRectangle ? ", rectangle" : ", circle");
}
