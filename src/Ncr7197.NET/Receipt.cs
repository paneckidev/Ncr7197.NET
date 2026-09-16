using System.Text;

namespace Ncr7197;

public sealed class Receipt
{
    /// <summary>
    /// Dot rows between the print head and the cutter, for the native <c>0x19</c> cut. Only
    /// relevant when <see cref="CutStyle"/> is <see cref="CutStyle.Native"/>;
    /// <see cref="CutStyle.FeedToCuttingPosition"/> lets the printer handle alignment.
    /// </summary>
    public const int NativeCutDistanceDots = 480;

    private readonly List<NcrCommand> _commands = [];
    private readonly Encoding _encoding;
    private int _fedSinceLastCut;

    public Receipt(Encoding? encoding = null, LineAdvanceMode lineAdvance = LineAdvanceMode.PrintAndFeedPaper)
    {
        _encoding = encoding ?? Encoding.ASCII;
        LineAdvance = lineAdvance;
    }

    /// <summary>
    /// Which command advances the paper. Change this if your 7197 needs a different
    /// vertical motion primitive; see <see cref="LineAdvanceMode"/>.
    /// </summary>
    public LineAdvanceMode LineAdvance { get; set; } = LineAdvanceMode.PrintAndFeedPaper;

    /// <summary>Dot rows per line when <see cref="LineAdvanceMode.PrintAndFeedPaper"/> is selected.</summary>
    public byte DotRowsPerLine { get; set; } = Ncr7197VerticalMotion.DefaultDotRowsPerLine;

    /// <summary>
    /// Extra dot rows to feed beyond the printer's own cutting position, used when
    /// <see cref="CutStyle"/> is <see cref="CutStyle.FeedToCuttingPosition"/>. Zero is the
    /// default: the printer already stops at its cutting position, so no host-side margin is
    /// needed. Raise it for a larger gap below the last line.
    /// </summary>
    public byte CutMarginDots { get; set; }

    /// <summary>
    /// How <see cref="Cut"/> cuts. The default delegates alignment to the printer, which
    /// feeds to its own cutting position and cuts cleanly below the last line.
    /// <see cref="CutStyle.Native"/> is the documented 7197 command but cuts at a fixed
    /// position, so it needs a large host-side margin instead.
    /// </summary>
    public CutStyle CutStyle { get; set; } = CutStyle.FeedToCuttingPosition;

    public Receipt Initialize()
    {
        _commands.Add(Ncr7197Commands.Initialize());
        return this;
    }

    /// <summary>
    /// Adds <c>ESC @</c>, which clears the input and print buffers.
    /// <para>
    /// Use this as the first call of a document that follows other output. <see cref="Initialize"/>
    /// alone does not clear the buffers, so residue from earlier commands stays in the printer
    /// and later output can appear corrupted or delayed.
    /// </para>
    /// </summary>
    public Receipt Reset()
    {
        _commands.Add(Ncr7197Commands.Reset());
        return this;
    }

    public Receipt Text(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _commands.Add(NcrCommand.Text(_encoding.GetBytes(text)));
        return this;
    }

    public Receipt Line(string text = "") => Text(text).FeedLines(1);

    public Receipt FeedLines(byte count = 1)
    {
        _commands.Add(Ncr7197VerticalMotion.Advance(LineAdvance, count, DotRowsPerLine));
        _fedSinceLastCut += count * Ncr7197VerticalMotion.DotsPerLine(LineAdvance, DotRowsPerLine);
        return this;
    }

    public Receipt FeedDots(byte count)
    {
        _commands.Add(Ncr7197Commands.FeedDots(count));
        _fedSinceLastCut += count;
        return this;
    }

    public Receipt UnderlineOff() => Underline(UnderlineMode.Off);

    public Receipt SetLineSpacing(byte dots)
    {
        _commands.Add(Ncr7197Commands.SetLineSpacing(dots));
        return this;
    }

    public Receipt DefaultLineSpacing()
    {
        _commands.Add(Ncr7197Commands.DefaultLineSpacing());
        return this;
    }

    public Receipt CodePage(byte codePage)
    {
        _commands.Add(Ncr7197Commands.CharacterCodePage(codePage));
        return this;
    }

    /// <summary>
    /// How image rows and bits are mapped onto <c>ESC *</c> band bytes. Defaults to
    /// <see cref="BitImageOrientation.ColumnMajorBit7Top"/>, the layout documented by NCR and
    /// confirmed on hardware.
    /// </summary>
    public BitImageOrientation BitImageOrientation { get; set; } = BitImageOrientation.ColumnMajorBit7Top;

    /// <summary>
    /// Adds a monochrome image, printed as <c>ESC *</c> bit-image bands.
    /// <para>
    /// The image is emitted in 8-dot bands. Each band is one <c>ESC *</c> command followed by
    /// the exact number of bytes its header declares (<c>columns = n1 + 256 * n2</c>, with
    /// <c>bandBytes = columns / 8</c>), and the paper is advanced 8 dot rows between bands so
    /// they stack instead of overprinting.
    /// </para>
    /// <para>
    /// One band prints the whole band width, so at most 576 columns are addressable. An image
    /// wider than 288 dots is wider than the 80 mm print head's single-density limit, and the
    /// printer accepts the data but renders it compressed, so anything past the first ~160
    /// columns is lost; prefer keeping images to 384 dots or fewer.
    /// </para>
    /// </summary>
    /// <param name="image">Image to print. Height is padded to whole 8-dot bands.</param>
    /// <param name="mode">
    /// <see cref="BitImageMode.SingleDensity"/> renders at 1 dot per column, which is the
    /// native resolution of a 203 dpi print head. <see cref="BitImageMode.DoubleDensity"/>
    /// halves the horizontal dot pitch, doubling the printed width of the same image.
    /// </param>
    public Receipt BitImage(RasterImage image, BitImageMode mode = BitImageMode.SingleDensity)
    {
        ArgumentNullException.ThrowIfNull(image);

        var columns = image.Width;

        if (columns > 576)
        {
            throw new ArgumentOutOfRangeException(
                nameof(image),
                $"A single ESC * band addresses at most 576 columns (the print head width); this image is {columns} dots wide.");
        }

        for (var band = 0; band < image.BitImageBandCount; band++)
        {
            // n1 is the low byte and n2 the high byte of the column count - not a byte count.
            // A header that disagrees with the number of bytes sent leaves the printer waiting
            // in graphics mode for data that never arrives.
            var bandData = image.GetBitImageBand(band, BitImageOrientation);
            _commands.Add(NcrCommand.Concat(
                NcrCommand.From(0x1B, 0x2A, (byte)mode, (byte)(columns % 256), (byte)(columns / 256)),
                NcrCommand.Text(bandData.Span)));

            // Move down exactly one 8-dot band, otherwise the next band joins the same line.
            if (band < image.BitImageBandCount - 1)
                _commands.Add(Ncr7197Commands.PrintAndFeedPaper(8));
        }

        return this;
    }

    /// <summary>
    /// Adds a raster image, one <c>DC1</c> (<c>0x11</c>) row per image row.
    /// <para>
    /// This is the simplest way to print a picture on this family: each command carries a full
    /// 72-byte (576-dot) row, prints it immediately and advances the paper exactly one dot row,
    /// so successive rows stack with no feed between them and there is no column header to get
    /// wrong.
    /// </para>
    /// <para>
    /// A single row spans the printable width edge to edge, a marker in byte 36 lands at
    /// mid-paper, and successive rows stack into a clean staircase.
    /// </para>
    /// </summary>
    public Receipt Raster(RasterImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        for (var row = 0; row < image.Height; row++)
            _commands.Add(Ncr7197Commands.PrintRasterRow(image.GetRow(row).Span));

        return this;
    }

    /// <summary>
    /// Adds a single raster row: one <c>DC1</c> command carrying <paramref name="row"/>.
    /// <para>
    /// Rows stack directly, so a picture can be streamed one row at a time without building a
    /// whole <see cref="RasterImage"/> in memory. The row is padded with white to the full
    /// printable width, and must not be interleaved with a paper-advance command, which would
    /// push the following rows down and break the picture into strips.
    /// </para>
    /// </summary>
    /// <param name="row">
    /// Row bytes, bit 7 of each byte is the leftmost of its 8 dots. Up to 72 bytes for 80 mm
    /// paper, or <see cref="Ncr7197Commands.RasterRowBytesNarrow"/> for 58 mm.
    /// </param>
    public Receipt RasterRow(ReadOnlySpan<byte> row)
    {
        _commands.Add(Ncr7197Commands.PrintRasterRow(row));
        return this;
    }

    public Receipt Align(Alignment alignment)
    {
        _commands.Add(Ncr7197Commands.Align(alignment));
        return this;
    }

    public Receipt Bold(bool enabled = true)
    {
        _commands.Add(Ncr7197Commands.Bold(enabled));
        return this;
    }

    public Receipt DoubleWidth(bool enabled = true)
    {
        _commands.Add(Ncr7197Commands.DoubleWidth(enabled));
        return this;
    }

    public Receipt Underline(UnderlineMode mode = UnderlineMode.Single)
    {
        _commands.Add(Ncr7197Commands.Underline(mode));
        return this;
    }

    public Receipt Reverse(bool enabled = true)
    {
        _commands.Add(Ncr7197Commands.Reverse(enabled));
        return this;
    }

    public Receipt Barcode(
        BarcodeType type,
        string data,
        BarcodeTextPosition textPosition = BarcodeTextPosition.None,
        byte width = 2,
        byte height = 50)
    {
        ArgumentNullException.ThrowIfNull(data);
        _commands.Add(Ncr7197Commands.Barcode(type, _encoding.GetBytes(data), textPosition, width, height));
        return this;
    }

    public Receipt Cut(bool full = true)
    {
        if (CutStyle == CutStyle.FeedToCuttingPosition)
        {
            // The printer feeds the paper to the cutter, so the host supplies nothing
            // beyond the optional extra margin.
            _commands.Add(Ncr7197Commands.FeedToCuttingPositionAndCut(CutMarginDots));
            _fedSinceLastCut = 0;
            return this;
        }

        // Native 0x19 / 0x1B 6D cut at a fixed position: the paper must already have
        // travelled from the print head to the blade, otherwise the blade severs a
        // printed line. Top the feed up to the required distance.
        var missing = NativeCutDistanceDots - _fedSinceLastCut;
        while (missing > 0)
        {
            var chunk = (byte)Math.Min(missing, byte.MaxValue);
            _commands.Add(Ncr7197Commands.PrintAndFeedPaper(chunk));
            missing -= chunk;
        }

        _fedSinceLastCut = 0;

        _commands.Add(full ? Ncr7197Commands.FullCut() : Ncr7197Commands.PartialCut());
        return this;
    }

    public Receipt OpenDrawer(byte drawer = 0)
    {
        _commands.Add(Ncr7197Commands.OpenDrawer(drawer));
        return this;
    }

    public byte[] Build()
    {
        var command = NcrCommand.Concat(_commands.ToArray());
        return command.Bytes.ToArray();
    }
}
