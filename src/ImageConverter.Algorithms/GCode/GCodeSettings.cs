using System.ComponentModel;

namespace ImageConverter.Core.GCode;

/// <summary>Machine settings for the pen-plotter G-code output.</summary>
[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class GCodeSettings
{
    [Category("Drawing area"), DisplayName("Width (mm)")]
    [Description("Width of the drawing area. With 'Keep aspect ratio' the drawing fits inside width × height.")]
    public double WidthMm { get; set; } = 200;

    [Category("Drawing area"), DisplayName("Height (mm)")]
    public double HeightMm { get; set; } = 200;

    [Category("Drawing area"), DisplayName("Keep aspect ratio")]
    [Description("true = scale uniformly to fit inside width × height (no distortion). false = stretch to exactly width × height.")]
    public bool KeepAspectRatio { get; set; } = true;

    [Category("Drawing area"), DisplayName("Offset X (mm)")]
    [Description("Position of the drawing's lower-left corner on the machine.")]
    public double OffsetXMm { get; set; } = 0;

    [Category("Drawing area"), DisplayName("Offset Y (mm)")]
    public double OffsetYMm { get; set; } = 0;

    [Category("Drawing area"), DisplayName("Center in area")]
    [Description("Center the drawing inside the width × height box (otherwise it sits in the lower-left corner).")]
    public bool Center { get; set; } = true;

    [Category("Drawing area"), DisplayName("Flip Y")]
    [Description("Images have Y pointing down, most machines have Y pointing up. Keep true unless the drawing comes out mirrored.")]
    public bool FlipY { get; set; } = true;

    [Category("Speed"), DisplayName("Draw feed rate (mm/min)")]
    public double FeedRate { get; set; } = 3000;

    [Category("Speed"), DisplayName("Travel feed rate (mm/min)")]
    [Description("Used for the single move to the start point (written as G1 so it also works on firmware where G0 ignores F).")]
    public double TravelFeedRate { get; set; } = 6000;

    [Category("Pen"), DisplayName("Pen up command")]
    [Description("e.g. 'G0 Z5' for a Z-axis pen, or 'M5' / 'M3 S0' for a servo pen (GRBL).")]
    public string PenUpCommand { get; set; } = "G0 Z5";

    [Category("Pen"), DisplayName("Pen down command")]
    [Description("Lowers the pen onto the paper – written once, right before the line starts. e.g. 'G1 Z0 F1000' for a Z-axis pen, or 'M3 S90' for a servo pen (GRBL). Must not be empty.")]
    public string PenDownCommand { get; set; } = "G1 Z0 F1000";

    [Category("Pen"), DisplayName("Pen delay (ms)")]
    [Description("Pause after pen up/down (G4) so a servo has time to move. 0 = none.")]
    public int PenDelayMs { get; set; } = 0;

    [Category("Program"), DisplayName("Header lines")]
    [Description("Written at the start. Default: millimetres, absolute positioning.")]
    public string[] HeaderLines { get; set; } = ["G21", "G90"];

    [Category("Program"), DisplayName("Footer lines")]
    [Description("Written at the end, after the pen is lifted.")]
    public string[] FooterLines { get; set; } = ["G0 X0 Y0"];

    [Category("Program"), DisplayName("Min segment (mm)")]
    [Description("Points closer than this to the previous one are skipped (fewer tiny moves).")]
    public double MinSegmentMm { get; set; } = 0.05;

    [Category("Program"), DisplayName("Decimals")]
    public int Decimals { get; set; } = 3;

    public override string ToString() => $"{WidthMm}×{HeightMm} mm, F{FeedRate}";
}
