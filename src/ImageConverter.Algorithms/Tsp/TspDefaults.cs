namespace ImageConverter.Core.Tsp;

/// <summary>
/// Central defaults for the automatic point count – change them HERE.
///
/// Used by both apps (WinForms and web). The web app can additionally override them in
/// src/ImageConverter.Web/appsettings.json (section "TspDefaults"), which is picked up
/// without a restart – just reload the page.
///
/// Settings saved by a user only override these values if the user changed them explicitly.
/// </summary>
public static class TspDefaults
{
    /// <summary>Automatic mode: one point per this many input pixels (smaller = more points).</summary>
    public static int AutoPixelsPerPoint { get; set; } = 20;

    /// <summary>Automatic mode: lower limit for the point count.</summary>
    public static int AutoMinPoints { get; set; } = 1_000;

    /// <summary>Automatic mode: upper limit for the point count.</summary>
    public static int AutoMaxPoints { get; set; } = 1_000_000;
}
