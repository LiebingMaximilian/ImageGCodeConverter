using System.Drawing;
using System.Drawing.Imaging;

namespace ImageConverter.Core;

/// <summary>
/// Loads JPEG/PNG files and normalises them into a 32bpp ARGB <see cref="Bitmap"/>.
/// </summary>
public static class ImageLoader
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Loads the image and copies it into a new, independent 32bpp ARGB bitmap
    /// (so the source file is not kept locked and the pixel format is predictable).
    /// </summary>
    public static Bitmap LoadAsBitmap(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Image not found.", path);
        if (!IsSupported(path))
            throw new NotSupportedException($"Unsupported file type: {Path.GetExtension(path)}");

        using var stream = File.OpenRead(path);
        using var source = Image.FromStream(stream);

        ApplyExifOrientation(source);

        var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        bitmap.SetResolution(source.HorizontalResolution, source.VerticalResolution);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White); // transparent PNG areas become white
            g.DrawImage(source, 0, 0, source.Width, source.Height);
        }
        return bitmap;
    }

    /// <summary>Rotates phone photos so they are upright (EXIF tag 0x0112).</summary>
    private static void ApplyExifOrientation(Image image)
    {
        const int orientationId = 0x0112;
        if (!image.PropertyIdList.Contains(orientationId)) return;

        var value = image.GetPropertyItem(orientationId)?.Value;
        if (value is null || value.Length == 0) return;

        RotateFlipType flip = value[0] switch
        {
            2 => RotateFlipType.RotateNoneFlipX,
            3 => RotateFlipType.Rotate180FlipNone,
            4 => RotateFlipType.Rotate180FlipX,
            5 => RotateFlipType.Rotate90FlipX,
            6 => RotateFlipType.Rotate90FlipNone,
            7 => RotateFlipType.Rotate270FlipX,
            8 => RotateFlipType.Rotate270FlipNone,
            _ => RotateFlipType.RotateNoneFlipNone
        };
        if (flip != RotateFlipType.RotateNoneFlipNone)
            image.RotateFlip(flip);
    }
}
