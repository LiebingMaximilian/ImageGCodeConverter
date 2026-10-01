using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ImageConverter.Core.GCode;
using ImageConverter.Core.Tsp;

namespace ImageConverter.App;

public enum ConversionMode
{
    TspArt = 0,
    Sobel = 1
}

[TypeConverter(typeof(ExpandableObjectConverter))]
public sealed class SobelOptions
{
    [DisplayName("Threshold"), Description("0 = grey-scale gradient, 1–255 = black/white edge map.")]
    public int Threshold { get; set; }

    [DisplayName("Invert"), Description("Black edges on white background.")]
    public bool Invert { get; set; }

    public override string ToString() => Threshold == 0 ? "grey-scale" : $"threshold {Threshold}";
}

/// <summary>All user settings, shown in the PropertyGrid and persisted as JSON
/// in %LOCALAPPDATA%\ImageConverter\settings.json.</summary>
public sealed class AppSettings
{
    [Browsable(false)]
    public ConversionMode Mode { get; set; } = ConversionMode.TspArt;

    [Category("1  TSP art (single line)"), DisplayName("TSP art")]
    public TspArtSettings Tsp { get; set; } = new();

    [Category("2  G-code / machine"), DisplayName("G-code")]
    public GCodeSettings GCode { get; set; } = new();

    [Category("3  Sobel"), DisplayName("Sobel")]
    public SobelOptions Sobel { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageConverter", "settings.json");

    // The file only contains values that differ from the defaults, so changing a default in code
    // (e.g. TspDefaults.AutoPixelsPerPoint) applies to everything the user hasn't changed explicitly.
    private const int FormatVersion = 2;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject json)
            {
                bool oldFormat = json["FormatVersion"]?.GetValue<int>() != FormatVersion;
                if (oldFormat && json["Tsp"] is JsonObject tsp)
                {
                    // v1 stored *all* values: forget the automatic point-count ones so the new defaults apply
                    tsp.Remove(nameof(TspArtSettings.AutoPixelsPerPoint));
                    tsp.Remove(nameof(TspArtSettings.AutoMinPoints));
                    tsp.Remove(nameof(TspArtSettings.AutoMaxPoints));
                }
                json.Remove("FormatVersion");

                var settings = json.Deserialize<AppSettings>(JsonOptions) ?? new();
                if (oldFormat) settings.Save();    // rewrite in the changes-only format
                return settings;
            }
        }
        catch
        {
            // corrupt / incompatible file -> fall back to defaults
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var current = JsonSerializer.SerializeToNode(this, JsonOptions)!.AsObject();
            var defaults = JsonSerializer.SerializeToNode(new AppSettings(), JsonOptions)!.AsObject();
            RemoveDefaults(current, defaults);
            current["FormatVersion"] = FormatVersion;

            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, current.ToJsonString(JsonOptions));
        }
        catch
        {
            // settings are a convenience – never crash because of them
        }
    }

    /// <summary>Removes every property whose value equals the default (recursively).</summary>
    private static void RemoveDefaults(JsonObject current, JsonObject defaults)
    {
        foreach (string key in current.Select(p => p.Key).ToList())
        {
            if (!defaults.TryGetPropertyValue(key, out var def)) continue;
            var cur = current[key];
            if (cur is JsonObject curObj && def is JsonObject defObj)
            {
                RemoveDefaults(curObj, defObj);
                if (curObj.Count == 0) current.Remove(key);
            }
            else if (JsonNode.DeepEquals(cur, def))
            {
                current.Remove(key);
            }
        }
    }

    /// <summary>Deep copy, so a running job isn't affected by edits in the PropertyGrid.</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
