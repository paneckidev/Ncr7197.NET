/// <summary>Turns greyscale into printer dots.</summary>
internal static class Dither
{
    /// <summary>
    /// Floyd-Steinberg error diffusion. Returns one flag per dot, true for a printed dot.
    /// Error diffusion keeps photographs readable on a 1-bit thermal head, where a plain
    /// threshold turns mid-grey into mud.
    /// </summary>
    /// <param name="grey">Row-major greyscale, 0 is black and 255 is white.</param>
    public static bool[] FloydSteinberg(byte[] grey, int width, int height, int threshold)
    {
        var work = new double[grey.Length];
        for (var i = 0; i < grey.Length; i++) work[i] = grey[i];

        var result = new bool[grey.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * width) + x;
                var old = work[index];
                var black = old < threshold;
                result[index] = black;

                var error = black ? old : old - 255;

                Spread(work, width, height, x + 1, y, error * 7 / 16);
                Spread(work, width, height, x - 1, y + 1, error * 3 / 16);
                Spread(work, width, height, x, y + 1, error * 5 / 16);
                Spread(work, width, height, x + 1, y + 1, error * 1 / 16);
            }
        }

        return result;
    }

    private static void Spread(double[] work, int width, int height, int x, int y, double error)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        work[(y * width) + x] += error;
    }
}
