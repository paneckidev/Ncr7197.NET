namespace Ncr7197;

/// <summary>
/// Monochrome image data, one bit per dot, for the raster (<c>DC1</c>) and bit-image
/// (<c>ESC *</c>) graphics paths.
/// </summary>
public sealed class RasterImage
{
    private readonly byte[] _data;

    public RasterImage(int width, int height, ReadOnlySpan<byte> data)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (width % 8 != 0) throw new ArgumentException("Raster width must be a multiple of 8 pixels.", nameof(width));

        var bytesPerRow = width / 8;
        if (data.Length != bytesPerRow * height)
            throw new ArgumentException("Raster data length does not match width and height.", nameof(data));

        Width = width;
        Height = height;
        _data = data.ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public ReadOnlyMemory<byte> Data => _data;

    /// <summary>
    /// Builds an image from a 1-bit-per-dot buffer whose rows are <paramref name="sourceWidth"/>
    /// dots wide, padding each row on the right so the result is a whole number of bytes.
    /// Use this instead of the constructor when the source width is not a multiple of 8.
    /// </summary>
    /// <param name="sourceWidth">Width of the source rows, in dots.</param>
    /// <param name="height">Number of rows in <paramref name="source"/>.</param>
    /// <param name="source">Rows packed 1 bit per dot, bit 7 is the leftmost dot of a row.</param>
    public static RasterImage FromBits(int sourceWidth, int height, ReadOnlySpan<byte> source)
    {
        if (sourceWidth <= 0) throw new ArgumentOutOfRangeException(nameof(sourceWidth));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        var sourceBytesPerRow = (sourceWidth + 7) / 8;
        if (source.Length < sourceBytesPerRow * height)
        {
            throw new ArgumentException(
                $"Source needs at least {sourceBytesPerRow * height} bytes for {sourceWidth}x{height}.",
                nameof(source));
        }

        var paddedWidth = sourceBytesPerRow * 8;
        var padded = new byte[sourceBytesPerRow * height];

        for (var row = 0; row < height; row++)
        {
            var sourceRow = source.Slice(row * sourceBytesPerRow, sourceBytesPerRow);
            var destination = padded.AsSpan(row * sourceBytesPerRow, sourceBytesPerRow);
            sourceRow.CopyTo(destination);

            // Blank the padding dots so they never print.
            var usedDotsInLastByte = sourceWidth - ((sourceBytesPerRow - 1) * 8);
            if (usedDotsInLastByte < 8)
                destination[^1] &= (byte)(0xFF << (8 - usedDotsInLastByte));
        }

        return new RasterImage(paddedWidth, height, padded);
    }

    /// <summary>Bytes per horizontal line (width / 8).</summary>
    public int BytesPerLine => Width / 8;

    /// <summary>Number of <c>ESC *</c> bands needed to print this image (height rounded up to 8 dots).</summary>
    public int BitImageBandCount => (Height + 7) / 8;

    /// <summary>Returns one 8-dot band using <see cref="BitImageOrientation.RowMajorBit7Left"/>.</summary>
    public ReadOnlyMemory<byte> GetBitImageBand(int band)
        => GetBitImageBand(band, BitImageOrientation.RowMajorBit7Left);

    /// <summary>
    /// Returns one 8-dot band for <c>ESC *</c>, using <paramref name="orientation"/>.
    /// </summary>
    /// <param name="band">Band index, 0 for the top 8 dot rows of the image.</param>
    /// <param name="orientation">
    /// Row and bit mapping. The right value is firmware dependent; see
    /// <see cref="BitImageOrientation"/>.
    /// </param>
    public ReadOnlyMemory<byte> GetBitImageBand(int band, BitImageOrientation orientation)
    {
        if ((uint)band >= (uint)BitImageBandCount)
            throw new ArgumentOutOfRangeException(nameof(band));

        var firstRow = band * 8;
        var rows = Math.Min(8, Height - firstRow);

        if (orientation == BitImageOrientation.ColumnMajorBit7Top)
            return GetColumnMajorBand(firstRow, rows);

        var reverseRows = orientation is BitImageOrientation.RowMajorReversedRowsBit7Left
                                      or BitImageOrientation.RowMajorReversedRowsBit0Left;
        var reverseBits = orientation is BitImageOrientation.RowMajorBit0Left
                                      or BitImageOrientation.RowMajorReversedRowsBit0Left;

        var buffer = new byte[BytesPerLine * 8];

        for (var row = 0; row < rows; row++)
        {
            var source = GetRow(firstRow + row).Span;
            var destination = buffer.AsSpan((reverseRows ? 7 - row : row) * BytesPerLine);

            for (var b = 0; b < BytesPerLine; b++)
                destination[b] = reverseBits ? ReverseBits(source[b]) : source[b];
        }

        return buffer;
    }

    /// <summary>
    /// Builds the band in the layout the printer documents and the hardware confirmed: one
    /// byte per dot column, bit 7 the top dot of that column, columns left to right.
    /// </summary>
    private ReadOnlyMemory<byte> GetColumnMajorBand(int firstRow, int rows)
    {
        var buffer = new byte[BytesPerLine * 8];
        var columns = Width;
        var bytesPerRow = BytesPerLine;

        for (var row = 0; row < rows; row++)
        {
            var source = GetRow(firstRow + row).Span;
            var bit = 7 - row;
            var mask = 1 << bit;

            for (var x = 0; x < columns; x++)
            {
                // Bit 7 of a source byte is the leftmost dot of that row.
                if ((source[x / 8] & (0x80 >> (x % 8))) != 0)
                    buffer[x] |= (byte)mask;
            }
        }

        return buffer;
    }

    /// <summary>Reverses the bit order of a byte (bit 0 &lt;-&gt; bit 7).</summary>
    internal static byte ReverseBitOrder(byte value) => (byte)(
        ((value & 0x01) << 7) |
        ((value & 0x02) << 5) |
        ((value & 0x04) << 3) |
        ((value & 0x08) << 1) |
        ((value & 0x10) >> 1) |
        ((value & 0x20) >> 3) |
        ((value & 0x40) >> 5) |
        ((value & 0x80) >> 7));

    private static byte ReverseBits(byte value) => ReverseBitOrder(value);

    public ReadOnlyMemory<byte> GetRow(int row)
    {
        if ((uint)row >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(row));

        return _data.AsMemory(row * (Width / 8), Width / 8);
    }
}
