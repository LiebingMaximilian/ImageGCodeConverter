using System.ComponentModel;
using System.Reflection;
using ImageConverter.Core.GCode;
using ImageConverter.Core.Spiral;
using ImageConverter.Core.Tsp;

namespace ImageConverter.Web;

/// <summary>Settings sent by the browser: { "mode": "tsp"|"spiral", "tsp": {...}, "spiral": {...}, "gcode": {...} }
/// (property names as in the C# classes).</summary>
public sealed class JobSettings
{
    public string? Mode { get; set; }
    public TspArtSettings? Tsp { get; set; }
    public SpiralSettings? Spiral { get; set; }
    public GCodeSettings? GCode { get; set; }
}

public sealed record SettingField(string Section, string Name, string Label, string Category,
                                  string Description, string Type, object? Value);

/// <summary>
/// Describes every setting (name, label, category, description, type, default) via reflection,
/// so the web form always shows all parameters of <see cref="TspArtSettings"/> and <see cref="GCodeSettings"/>.
/// </summary>
public static class SettingsSchema
{
    public static IReadOnlyList<SettingField> Describe() =>
        [.. Describe("tsp", new TspArtSettings()), .. Describe("spiral", new SpiralSettings()),
         .. Describe("gcode", new GCodeSettings())];

    private static IEnumerable<SettingField> Describe(string section, object defaults)
    {
        foreach (var p in defaults.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || !p.CanWrite) continue;
            if (p.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false) continue;

            string type = p.PropertyType switch
            {
                var t when t == typeof(int) => "int",
                var t when t == typeof(float) || t == typeof(double) => "number",
                var t when t == typeof(bool) => "bool",
                var t when t == typeof(string) => "string",
                var t when t == typeof(string[]) => "lines",
                _ => "unsupported"
            };
            if (type == "unsupported") continue;

            yield return new SettingField(
                section,
                p.Name,
                p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? p.Name,
                p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "Other",
                p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                type,
                p.GetValue(defaults));
        }
    }
}
