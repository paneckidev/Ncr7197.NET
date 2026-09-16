using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

/// <summary>Loads a picture and scales it to the printer's dot width.</summary>
internal static class ImageLoader{
    /// <summary>
    /// Loads <paramref name="path"/>, scales it to <paramref name="targetWidth"/> dots and
    /// returns one greyscale byte per dot, where 0 is black and 255 is white.
    /// </summary>
    public static (byte[] Grey, int Width, int Height) LoadGrey(string path, int targetWidth)
    {
        using var source = new Bitmap(path);

        var scale = (double)targetWidth / source.Width;
        var targetHeight = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var scaled = new Bitmap(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, targetWidth, targetHeight);
        }

        var grey = new byte[targetWidth * targetHeight];
        var data = scaled.LockBits(
            new Rectangle(0, 0, targetWidth, targetHeight),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            unsafe
            {
                for (var y = 0; y < targetHeight; y++)
                {
                    var row = (byte*)data.Scan0 + (y * data.Stride);

                    for (var x = 0; x < targetWidth; x++)
                    {
                        var b = row[x * 3];
                        var g = row[(x * 3) + 1];
                        var r = row[(x * 3) + 2];
                        grey[(y * targetWidth) + x] = (byte)(((r * 299) + (g * 587) + (b * 114)) / 1000);
                    }
                }
            }
        }
        finally
        {
            scaled.UnlockBits(data);
        }

        return (grey, targetWidth, targetHeight);
    }
}
