using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ImageConverter.Core;

/// <summary>Fast pixel access helpers for <see cref="Bitmap"/>.</summary>
public static class BitmapPixels
{
    /// <summary>Luminance (ITU-R BT.601) per pixel, 0..255, row-major.</summary>
    public static float[] ToGrayscale(Bitmap source)
    {
        int width = source.Width;
        int height = source.Height;
        var data = source.LockBits(new Rectangle(0, 0, width, height),
                                   ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = data.Stride;
            byte[] pixels = new byte[Math.Abs(stride) * height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            float[] gray = new float[width * height];
            Parallel.For(0, height, y =>
            {
                int src = y * stride;
                int dst = y * width;
                for (int x = 0; x < width; x++, src += 4)
                {
                    // memory layout for 32bppArgb is B, G, R, A
                    gray[dst + x] = 0.114f * pixels[src] + 0.587f * pixels[src + 1] + 0.299f * pixels[src + 2];
                }
            });
            return gray;
        }
        finally
        {
            source.UnlockBits(data);
        }
    }
}
