namespace Ncr7197;

/// <summary>Raw NCR 7197 native commands documented by NCR.</summary>
public static class Ncr7197Commands
{
    public static NcrCommand Initialize() => NcrCommand.From(0x10);

    /// <summary>
    /// <c>ESC @</c> - full reset that clears the input <b>and</b> print buffers, unlike
    /// <see cref="Initialize"/> which resets settings only.
    /// <para>
    /// Without it, residue from earlier commands stays in the printer and later graphics
    /// output can appear corrupted or delayed. Emit it before a document that follows other
    /// output.
    /// </para>
    /// </summary>
    public static NcrCommand Reset() => NcrCommand.From(0x1B, 0x40);

    /// <summary>
    /// Cuts at the cutting position (native command). The printer feeds the paper to the
    /// cutter itself, so no host-side margin is needed.
    /// </summary>
    public static NcrCommand FullCut() => NcrCommand.From(0x19);

    /// <summary>
    /// Cuts at the cutting position (native command). NCR documents this as performing the
    /// same 5 mm partial cut as <see cref="FullCut"/>.
    /// </summary>
    public static NcrCommand PartialCut() => NcrCommand.From(0x1B, 0x6D);

    /// <summary>
    /// <c>GS V 66 n</c> - feeds paper to the cutting position plus n dot rows, then cuts.
    /// The printer performs the alignment, so the host does not have to feed the whole
    /// print-head-to-cutter distance.
    /// </summary>
    public static NcrCommand FeedToCuttingPositionAndCut(byte extraDots = 0)
        => NcrCommand.From(0x1D, 0x56, 0x42, extraDots);

    /// <summary>
    /// <c>GS V 65 n</c> - same as <see cref="FeedToCuttingPositionAndCut"/>; Epson documents
    /// m=65 as the full-cut form of the same function.
    /// </summary>
    public static NcrCommand FeedToCuttingPositionAndFullCut(byte extraDots = 0)
        => NcrCommand.From(0x1D, 0x56, 0x41, extraDots);

    /// <summary>
    /// <c>GS V 1</c> - cuts immediately without feeding to the cutting position, which severs
    /// paper through the printed text.
    /// </summary>
    public static NcrCommand CutWithoutFeed() => NcrCommand.From(0x1D, 0x56, 0x01);

    public static NcrCommand Tone() => NcrCommand.From(0x1B, 0x07);
    public static NcrCommand FeedLines(byte count) => NcrCommand.From(0x14, count);
    public static NcrCommand FeedDots(byte count) => NcrCommand.From(0x15, count);
    public static NcrCommand DoubleWidth(bool enabled) => NcrCommand.From(enabled ? (byte)0x12 : (byte)0x13);
    public static NcrCommand Align(Alignment alignment) => NcrCommand.From(0x1B, 0x61, (byte)alignment);
    public static NcrCommand Bold(bool enabled) => NcrCommand.From(0x1B, 0x45, enabled ? (byte)1 : (byte)0);
    public static NcrCommand Underline(UnderlineMode mode) => NcrCommand.From(0x1B, 0x2D, (byte)mode);
    public static NcrCommand UpsideDown(bool enabled) => NcrCommand.From(0x1B, 0x7B, enabled ? (byte)1 : (byte)0);
    public static NcrCommand Rotate(bool enabled) => NcrCommand.From(0x1B, 0x56, enabled ? (byte)1 : (byte)0);
    public static NcrCommand Reverse(bool enabled) => NcrCommand.From(0x1D, 0x42, enabled ? (byte)1 : (byte)0);
    public static NcrCommand OpenDrawer(byte drawer = 0, byte onTime = 25, byte offTime = 250)
    {
        if (drawer is > 1) throw new ArgumentOutOfRangeException(nameof(drawer));
        return NcrCommand.From(0x1B, 0x70, drawer, onTime, offTime);
    }

    public static NcrCommand DrawerStatus() => NcrCommand.From(0x1B, 0x75, 0x00);
    public static NcrCommand PrinterStatus() => NcrCommand.From(0x1B, 0x76);
    public static NcrCommand TransmitStatus(byte n) => NcrCommand.From(0x1D, 0x72, n);
    public static NcrCommand SoftwareVersion() => NcrCommand.From(0x1F, 0x56);

    /// <summary>Prints the buffered line and feeds paper by n lines (ESC d).</summary>
    public static NcrCommand PrintAndFeedLines(byte count) => NcrCommand.From(0x1B, 0x64, count);

    /// <summary>Prints the buffered line and feeds paper by n dot rows (ESC J).</summary>
    public static NcrCommand PrintAndFeedPaper(byte dots) => NcrCommand.From(0x1B, 0x4A, dots);

    /// <summary>Prints the buffered line and feeds paper by one line (LF).</summary>
    public static NcrCommand PrintAndFeedLine() => NcrCommand.From(0x0A);

    /// <summary>Prints the buffered line and performs a carriage return (CR LF).</summary>
    public static NcrCommand PrintAndCarriageReturnLineFeed() => NcrCommand.From(0x0D, 0x0A);

    /// <summary>
    /// Prints one raster row: <c>DC1</c> (<c>0x11</c>) followed immediately by the row bytes.
    /// <para>
    /// A row is <b>72 bytes = 576 dots</b> for 80 mm paper and 53 bytes for 58 mm. There is
    /// <b>no length byte</b>. One row prints immediately and advances the paper exactly one dot
    /// row, so successive rows stack with no feed command between them; interleaving a feed
    /// pushes the following rows down and breaks the picture into strips.
    /// </para>
    /// <para>
    /// Within a row each byte covers 8 <b>horizontal</b> dots and <b>bit 7 is the leftmost</b>.
    /// A set bit is a printed dot, so <c>0xFF</c> is black and <c>0x00</c> is white. A shorter
    /// row is padded with white on the right.
    /// </para>
    /// </summary>
    public static NcrCommand PrintRasterRow(ReadOnlySpan<byte> row)
    {
        if (row.IsEmpty) throw new ArgumentException("Raster row cannot be empty.", nameof(row));
        if (row.Length > RasterRowBytes) throw new ArgumentOutOfRangeException(nameof(row), $"A raster row cannot exceed {RasterRowBytes} bytes (576 dots).");

        var padded = new byte[RasterRowBytes];
        row.CopyTo(padded);

        return NcrCommand.Concat(NcrCommand.From(0x11), NcrCommand.Text(padded));
    }

    /// <summary>Bytes in one full-width <c>DC1</c> raster row on 80 mm paper (576 dots).</summary>
    public const int RasterRowBytes = 72;

    /// <summary>Bytes in one full-width <c>DC1</c> raster row on 58 mm paper.</summary>
    public const int RasterRowBytesNarrow = 53;

    /// <summary>
    /// <c>ESC * m n1 n2 data</c> - prints one 8-dot band of a bit image.
    /// <para>
    /// Per the NCR 7197 Series II command reference, <paramref name="n1"/> is the <b>low byte</b>
    /// and <paramref name="n2"/> the <b>high byte</b> of the horizontal column count:
    /// <c>columns = n1 + 256 * n2</c>. There is no vertical-size parameter - the band is always
    /// 8 dot rows and a print command (<c>LF</c>) must follow to commit it.
    /// </para>
    /// <para>
    /// A header that declares more bytes than are delivered is not a harmless mistake: the
    /// printer stays in graphics mode waiting for the rest and consumes every later byte as
    /// image data, including resets and text, until it is power-cycled. Derive both bytes from
    /// the band length, as this overload does, rather than passing a count in one byte.
    /// </para>
    /// </summary>
    /// <param name="mode">Density/mode selector; see <see cref="BitImageMode"/>.</param>
    /// <param name="band">
    /// One band of data: 8 dot rows of <c>band.Length / 8</c> columns each, so the band is
    /// <c>band.Length</c> columns wide.
    /// </param>
    public static NcrCommand BitImageBand(BitImageMode mode, ReadOnlySpan<byte> band)
    {
        if (band.IsEmpty) throw new ArgumentException("Bit image band cannot be empty.", nameof(band));
        if (band.Length % 8 != 0) throw new ArgumentException("A bit image band must contain whole 8-dot rows.", nameof(band));

        var columns = band.Length / 8;
        if (columns > 576)
            throw new ArgumentOutOfRangeException(nameof(band), "A bit image band is limited to 576 columns (the print head width).");

        return NcrCommand.Concat(
            NcrCommand.From(0x1B, 0x2A, (byte)mode, (byte)(columns % 256), (byte)(columns / 256)),
            NcrCommand.Text(band));
    }

    /// <summary>
    /// <c>ESC * m n1 n2 data</c> with a single dot row of data, i.e. one column per byte.
    /// The column count is split across <c>n1</c> (low) and <c>n2</c> (high) as
    /// <c>n1 + 256 * n2</c>, so this form addresses up to 576 columns.
    /// </summary>
    public static NcrCommand BitImageRow(BitImageMode mode, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) throw new ArgumentException("Bit image row cannot be empty.", nameof(data));
        if (data.Length > 576) throw new ArgumentOutOfRangeException(nameof(data), "A bit image row is limited to 576 columns (576 dots).");

        var columns = data.Length;
        return NcrCommand.Concat(
            NcrCommand.From(0x1B, 0x2A, (byte)mode, (byte)(columns % 256), (byte)(columns / 256)),
            NcrCommand.Text(data));
    }


    public static NcrCommand SetLineSpacing(byte dots) => NcrCommand.From(0x1B, 0x33, dots);
    public static NcrCommand DefaultLineSpacing() => NcrCommand.From(0x1B, 0x32);
    public static NcrCommand CharacterCodePage(byte codePage) => NcrCommand.From(0x1B, 0x74, codePage);

    public static NcrCommand Barcode(
        BarcodeType type,
        ReadOnlySpan<byte> data,
        BarcodeTextPosition textPosition = BarcodeTextPosition.None,
        byte width = 2,
        byte height = 50)
    {
        if (data.IsEmpty) throw new ArgumentException("Barcode data cannot be empty.", nameof(data));
        if (width is < 2 or > 6) throw new ArgumentOutOfRangeException(nameof(width), "NCR 7197 barcode width is 2..6.");
        if (height is < 1 or > 255) throw new ArgumentOutOfRangeException(nameof(height));

        return NcrCommand.Concat(
            NcrCommand.From(0x1D, 0x48, (byte)textPosition),
            NcrCommand.From(0x1D, 0x77, width),
            NcrCommand.From(0x1D, 0x68, height),
            NcrCommand.From(0x1D, 0x6B, (byte)type),
            NcrCommand.Text(data),
            NcrCommand.From(0x00));
    }
}

public enum Alignment : byte { Left = 0, Center = 1, Right = 2 }

/// <summary>Density/width mode for <c>ESC *</c> (Select Bit Image Mode).</summary>
public enum BitImageMode : byte
{
    /// <summary>8 dots tall per band, one column per byte. Up to 288 columns.</summary>
    SingleDensity = 0,

    /// <summary>8 dots tall per band, two horizontal dots per column. Up to 576 columns.</summary>
    DoubleDensity = 1,

    /// <summary>24 dots tall per band, three bytes per column. Up to 288 columns.</summary>
    SingleDensity24 = 32,

    /// <summary>24 dots tall per band, three bytes per column, double density. Up to 576 columns.</summary>
    DoubleDensity24 = 33
}

public enum UnderlineMode : byte { Off = 0, Single = 1, Double = 2 }
public enum BarcodeTextPosition : byte { None = 0, Above = 1, Below = 2, Both = 3 }

/// <summary>7197 native barcode identifiers used by GS k.</summary>
public enum BarcodeType : byte
{
    UpcA = 0,
    UpcE = 1,
    Ean13 = 2,
    Ean8 = 3,
    Code39 = 4,
    Interleaved2Of5 = 5,
    Codabar = 6,
    Code128 = 73
}
