namespace Ncr7197;

/// <summary>
/// How image rows and bits are mapped onto <c>ESC *</c> band bytes.
/// <para>
/// <see cref="ColumnMajorBit7Top"/> is the layout NCR documents and the one this printer uses.
/// The row-major variants describe the Epson ESC/POS convention and are kept for firmware that
/// follows it; they do not produce correct glyphs on 7194 Native Mode. Solid fills render under
/// any of them, so test a sparse, asymmetric glyph, not a filled rectangle.
/// </para>
/// </summary>
public enum BitImageOrientation
{
    /// <summary>Rows top-down, bit 7 is the leftmost dot. The Epson ESC/POS convention.</summary>
    RowMajorBit7Left = 0,

    /// <summary>Rows top-down, bit 0 is the leftmost dot.</summary>
    RowMajorBit0Left = 1,

    /// <summary>Rows bottom-up, bit 7 is the leftmost dot.</summary>
    RowMajorReversedRowsBit7Left = 2,

    /// <summary>
    /// Rows bottom-up, bit 0 is the leftmost dot.
    /// </summary>
    RowMajorReversedRowsBit0Left = 3,

    /// <summary>
    /// <b>The default, and the one the hardware actually uses.</b> Data is
    /// <b>column-major</b>: each byte describes 8 <b>vertical</b> dots, bit 7 is the
    /// <b>topmost</b> dot of that column, and successive bytes march to the right.
    /// A set bit is a printed dot, so <c>0xFF</c> is black and <c>0x00</c> is white.
    /// <para>
    /// Confirmed on an NCR 7198 in 7194 Native Mode against the NCR 7197 Series II command
    /// reference, which documents the data as "printed down then across" with the MSB at the
    /// top of each column. The row-major variants above are kept only because they were the
    /// earlier working hypothesis; solid rectangles render under any of them, which is why
    /// testing with filled blocks gave misleading results for so long.
    /// </para>
    /// </summary>
    ColumnMajorBit7Top = 4
}

/// <summary>How the paper is cut.</summary>
public enum CutStyle
{
    /// <summary>
    /// <c>GS V 66 n</c> - feeds paper to the printer's cutting position, then cuts.
    /// The default: the printer performs the alignment, so the host does not need to feed
    /// the full print-head-to-cutter distance.
    /// </summary>
    FeedToCuttingPosition = 0,

    /// <summary>
    /// <c>0x19</c> / <c>0x1B 0x6D</c> - the cut command documented for 7197 native mode.
    /// It cuts at a fixed position without feeding to the cutter, so the paper must already
    /// have travelled <see cref="Receipt.NativeCutDistanceDots"/> dot rows since the last
    /// cut. Use it only when <see cref="CutStyle.FeedToCuttingPosition"/> is unavailable.
    /// </summary>
    Native = 1
}

/// <summary>
/// Selects which native command the high-level API uses to advance the paper.
///
/// This exists because "Feed n Print Lines" (<c>0x14 n</c>) is documented for the
/// 7197 native command set but is NOT obeyed by every unit / interface
/// configuration. On a printer that ignores <c>0x14</c> the paper only moves when
/// a line reaches the right margin, which makes receipts look like the printer is
/// choosing its own line feed.
/// </summary>
public enum LineAdvanceMode
{
    /// <summary>
    /// <c>0x1B 0x4A n</c> - the default. Prints the buffered line and feeds n dot rows,
    /// which gives an exact, predictable line pitch on units that ignore
    /// <c>0x1B 0x64 n</c> or advance too far with it.
    /// See <see cref="Ncr7197VerticalMotion.DefaultDotRowsPerLine"/>.
    /// </summary>
    PrintAndFeedPaper = 0,

    /// <summary>
    /// <c>0x1B 0x64 n</c> - prints the buffered line and feeds n lines, one command
    /// for the whole count. Line height is whatever the printer's default spacing is.
    /// </summary>
    PrintAndFeedLines = 1,

    /// <summary><c>0x0A</c> per line - the classic print-and-feed-one-line character.</summary>
    LineFeed = 2,

    /// <summary><c>0x0D 0x0A</c> per line - carriage return plus line feed.</summary>
    CarriageReturnLineFeed = 3,

    /// <summary>
    /// <c>0x14 n</c> - what the NCR 7197 native documentation describes for
    /// "Feed n Print Lines". Kept for units that honour it; some 7197 configurations
    /// ignore it, in which case the paper never advances.
    /// </summary>
    FeedPrintLines = 4
}

/// <summary>
/// Builds the vertical-motion command for a given <see cref="LineAdvanceMode"/>.
/// Kept separate from <see cref="Ncr7197Commands"/> so the raw native commands stay
/// byte-for-byte documented constants.
/// </summary>
public static class Ncr7197VerticalMotion
{
    /// <summary>
    /// Dot rows used for one line when <see cref="LineAdvanceMode.PrintAndFeedPaper"/>
    /// is selected. 26 rows is one line at 203 dpi (1/6 inch).
    /// </summary>
    public const byte DefaultDotRowsPerLine = 0x1A;

    /// <summary>Builds the command that prints the buffered line and advances <paramref name="lines"/> lines.</summary>
    public static NcrCommand Advance(LineAdvanceMode mode, byte lines = 1, byte dotRowsPerLine = DefaultDotRowsPerLine)
    {
        if (lines == 0) lines = 1;

        return mode switch
        {
            LineAdvanceMode.PrintAndFeedLines => Ncr7197Commands.PrintAndFeedLines(lines),
            LineAdvanceMode.PrintAndFeedPaper => Ncr7197Commands.PrintAndFeedPaper(SaturatingMultiply(dotRowsPerLine, lines)),
            LineAdvanceMode.LineFeed => NcrCommand.From(Repeat(0x0A, lines)),
            LineAdvanceMode.CarriageReturnLineFeed => NcrCommand.From(Repeat(0x0A, lines, prefix: 0x0D)),
            LineAdvanceMode.FeedPrintLines => Ncr7197Commands.FeedLines(lines),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown line advance mode.")
        };
    }

    /// <summary>Feeds <paramref name="count"/> dot rows without printing (0x15 n).</summary>
    public static NcrCommand FeedDots(byte count) => Ncr7197Commands.FeedDots(count);

    /// <summary>
    /// Rough conversion of one line of vertical motion into dot rows, used when the
    /// caller needs a distance in dots but the selected mode works in lines.
    /// </summary>
    public static int DotsPerLine(LineAdvanceMode mode, byte dotRowsPerLine = DefaultDotRowsPerLine)
        => mode switch
        {
            LineAdvanceMode.PrintAndFeedPaper => dotRowsPerLine,
            _ => DefaultDotRowsPerLine
        };

    private static byte[] Repeat(byte value, int count, byte? prefix = null)
    {
        var stride = prefix.HasValue ? 2 : 1;
        var buffer = new byte[stride * count];

        for (var i = 0; i < count; i++)
        {
            if (prefix.HasValue)
                buffer[i * 2] = prefix.Value;

            buffer[(i * stride) + stride - 1] = value;
        }

        return buffer;
    }

    private static byte SaturatingMultiply(byte left, byte right)
    {
        var product = left * right;
        return product > byte.MaxValue ? byte.MaxValue : (byte)product;
    }
}
