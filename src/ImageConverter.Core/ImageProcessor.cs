using System.Drawing.Imaging;
using ImageConverter.Core.GCode;
using ImageConverter.Core.Tsp;

namespace ImageConverter.Core;

/// <param name="OutputPath">The image written (Sobel result or single-line preview).</param>
/// <param name="GCodePath">G-code file, if one was written.</param>
/// <param name="Info">Short human-readable summary for the log.</param>
public sealed record ProcessResult(string InputPath, string OutputPath, int Width, int Height, TimeSpan Duration,
                                   string? GCodePath = null, string? Info = null);

/// <summary>
/// Pipelines:
///   Sobel:   file (jpg/png) -> Bitmap -> Sobel -> PNG
///   TSP art: file (jpg/png) -> Bitmap -> stipple points -> TSP tour -> preview PNG + G-code
/// Everything is written to the output folder.
/// </summary>
public sealed class ImageProcessor
{
    public string OutputDirectory { get; }

    public ImageProcessor(string? outputDirectory = null)
    {
        OutputDirectory = outputDirectory ?? DefaultOutputDirectory;
        Directory.CreateDirectory(OutputDirectory);
    }

    /// <summary>%USERPROFILE%\Desktop\ImageConverter Output</summary>
    public static string DefaultOutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     "ImageConverter Output");

    public ProcessResult Process(string inputPath, int threshold = 0, bool invert = false)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var bitmap = ImageLoader.LoadAsBitmap(inputPath);
        using var edges = SobelFilter.Apply(bitmap, threshold, invert);

        string outputPath = GetUniqueBasePath(inputPath, "_sobel", ".png") + ".png";
        edges.Save(outputPath, ImageFormat.Png);

        sw.Stop();
        return new ProcessResult(inputPath, outputPath, bitmap.Width, bitmap.Height, sw.Elapsed);
    }

    /// <summary>Converts the image into one continuous line (TSP art) and writes preview PNG + G-code.</summary>
    public ProcessResult ProcessTspArt(string inputPath, TspArtSettings tsp, GCodeSettings gcode,
                                       IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        int width, height;
        TspArtResult art;
        using (var bitmap = ImageLoader.LoadAsBitmap(inputPath))
        {
            width = bitmap.Width;
            height = bitmap.Height;
            art = TspArtBitmap.Generate(bitmap, tsp, progress, ct);
        }

        progress?.Report("Writing preview and G-code…");
        string basePath = GetUniqueBasePath(inputPath, "_tsp", ".png", ".gcode");
        string pngPath = basePath + ".png";
        string gcodePath = basePath + ".gcode";

        using (var preview = TspArtBitmap.RenderPreview(art, tsp.PreviewScale, tsp.PreviewLineWidth, tsp.PreviewMaxSize))
            preview.Save(pngPath, ImageFormat.Png);

        var g = GCodeWriter.Write(art.Path, art.Width, art.Height, gcode, Path.GetFileName(inputPath));
        File.WriteAllText(gcodePath, g.Code);

        sw.Stop();
        string info = $"{art.Path.Length:N0} points, {g.WidthMm:F0}×{g.HeightMm:F0} mm, " +
                      $"line {g.DrawLengthMm / 1000:F1} m, ~{g.EstimatedTime:h\\:mm\\:ss} @ F{gcode.FeedRate:0}";
        return new ProcessResult(inputPath, pngPath, width, height, sw.Elapsed, gcodePath, info);
    }

    /// <summary>Output path without extension, unique for all given extensions.</summary>
    private string GetUniqueBasePath(string inputPath, string suffix, params string[] extensions)
    {
        string baseName = Path.GetFileNameWithoutExtension(inputPath) + suffix;
        string candidate = Path.Combine(OutputDirectory, baseName);
        for (int i = 2; extensions.Any(e => File.Exists(candidate + e)); i++)
            candidate = Path.Combine(OutputDirectory, $"{baseName}_{i}");
        return candidate;
    }
}
