using Xunit;

namespace Ncr7197.Tests;

public class Ncr7197CommandsTests
{
    [Fact]
    public void Initialize_IsSingleByteCommand()
        => Assert.Equal([0x10], Ncr7197Commands.Initialize().Bytes.ToArray());

    [Fact]
    public void FullCut_IsCorrect()
        => Assert.Equal([0x19], Ncr7197Commands.FullCut().Bytes.ToArray());

    [Fact]
    public void OpenDrawer_UsesExpectedCommand()
        => Assert.Equal([0x1B, 0x70, 0x00, 25, 250], Ncr7197Commands.OpenDrawer().Bytes.ToArray());

    [Fact]
    public void RasterRow_IsCommandFollowedByPaddedPayload()
    {
        // DC1 takes no length byte: it is the command immediately followed by the 72 row bytes,
        // zero-padded to the full printable width.
        var bytes = Ncr7197Commands.PrintRasterRow([0xAA, 0x55]).Bytes.ToArray();

        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0xAA, bytes[1]);
        Assert.Equal(0x55, bytes[2]);
        Assert.Equal(1 + Ncr7197Commands.RasterRowBytes, bytes.Length);
        Assert.All(bytes[3..], b => Assert.Equal(0x00, b));
    }

    [Fact]
    public void RasterImage_ValidatesDimensions()
        => Assert.Throws<ArgumentException>(() => new RasterImage(10, 1, new byte[2]));

    [Fact]
    public void RasterImage_ReturnsRows()
    {
        var image = new RasterImage(16, 2, [1, 2, 3, 4]);

        Assert.Equal([1, 2], image.GetRow(0).ToArray());
        Assert.Equal([3, 4], image.GetRow(1).ToArray());
    }

    [Fact]
    public void Raster_SingleRow_IsWrittenAsARasterCommand()
    {
        var receipt = new Receipt().Raster(new RasterImage(16, 1, [0xAA, 0x55]));

        var bytes = receipt.Build();

        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0xAA, bytes[1]);
        Assert.Equal(0x55, bytes[2]);
        Assert.Equal(1 + Ncr7197Commands.RasterRowBytes, bytes.Length);
    }

    [Fact]
    public void Raster_FullWidthSolidBlock_IsBlackToBothEdgesOfEveryRow()
    {
        // This is the shape DeviceTest procedure 5 prints, checked here so the byte layout is
        // verified without touching paper: a solid block the full printable width, 64 rows
        // tall (about 8 mm at 203 dpi).
        const int rowBytes = Ncr7197Commands.RasterRowBytes;
        const int height = 64;

        var data = new byte[rowBytes * height];
        Array.Fill(data, (byte)0xFF);

        var bytes = new Receipt().Raster(new RasterImage(rowBytes * 8, height, data)).Build();

        Assert.Equal(height * (1 + rowBytes), bytes.Length);

        for (var row = 0; row < height; row++)
        {
            var offset = row * (1 + rowBytes);

            Assert.Equal(0x11, bytes[offset]);

            // Every one of the 72 bytes in the row is black, so the block reaches both edges.
            Assert.All(
                bytes.AsSpan(offset + 1, rowBytes).ToArray(),
                b => Assert.Equal(0xFF, b));
        }
    }

    [Fact]
    public void Raster_MultiRow_EmitsOneCommandPerRow()
    {
        // Each DC1 row prints and advances one dot row by itself, so rows stack with no feed
        // between them; interleaving one would break the picture into strips.
        var image = new RasterImage(16, 2, [0xAA, 0x55, 0x0F, 0xF0]);

        var bytes = new Receipt().Raster(image).Build();

        Assert.Equal(2 * (1 + Ncr7197Commands.RasterRowBytes), bytes.Length);
        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0xAA, bytes[1]);
        Assert.Equal(0x55, bytes[2]);
        Assert.Equal(0x11, bytes[1 + Ncr7197Commands.RasterRowBytes]);
        Assert.Equal(0x0F, bytes[2 + Ncr7197Commands.RasterRowBytes]);
        Assert.Equal(0xF0, bytes[3 + Ncr7197Commands.RasterRowBytes]);
    }

    [Fact]
    public void RasterRow_EmitsASinglePaddedCommand()
    {
        var bytes = new Receipt().RasterRow([0xAA, 0x55]).Build();

        Assert.Equal(1 + Ncr7197Commands.RasterRowBytes, bytes.Length);
        Assert.Equal(0x11, bytes[0]);
        Assert.Equal(0xAA, bytes[1]);
        Assert.Equal(0x55, bytes[2]);
        Assert.All(bytes[3..], b => Assert.Equal(0x00, b));
    }

    [Fact]
    public void RasterRow_RejectsARowWiderThanThePrintHead()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new Receipt().RasterRow(new byte[Ncr7197Commands.RasterRowBytes + 1]));

    [Fact]
    public void BitImageRow_EmitsEscStarHeader()
    {
        var command = Ncr7197Commands.BitImageRow(BitImageMode.SingleDensity, [0xAA, 0x55]);

        Assert.Equal([0x1B, 0x2A, 0x00, 0x02, 0x00, 0xAA, 0x55], command.Bytes.ToArray());
    }

    [Fact]
    public void BitImageRow_SplitsTheColumnCountAcrossN1AndN2()
    {
        // 384 columns is 0x180: low byte in n1, high byte in n2. A header that disagrees with
        // the number of bytes sent leaves the printer stuck in graphics mode waiting for the
        // rest, consuming every later byte as image data.
        var command = Ncr7197Commands.BitImageRow(BitImageMode.SingleDensity, new byte[384]);

        Assert.Equal([0x1B, 0x2A, 0x00, 0x80, 0x01], command.Bytes.Span[..5].ToArray());
    }

    [Fact]
    public void BitImageRow_UsesTheRequestedMode()
    {
        var command = Ncr7197Commands.BitImageRow(BitImageMode.DoubleDensity, [0xFF]);

        Assert.Equal([0x1B, 0x2A, 0x01, 0x01, 0x00, 0xFF], command.Bytes.ToArray());
    }

    [Fact]
    public void BitImageRow_RejectsTooWideARow()
    {
        var tooWide = new byte[577];

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Ncr7197Commands.BitImageRow(BitImageMode.SingleDensity, tooWide));
    }

    [Fact]
    public void RasterImage_BandCountRoundsUpToEightDots()
    {
        Assert.Equal(1, new RasterImage(8, 1, new byte[1]).BitImageBandCount);
        Assert.Equal(1, new RasterImage(8, 8, new byte[8]).BitImageBandCount);
        Assert.Equal(2, new RasterImage(8, 9, new byte[9]).BitImageBandCount);
        Assert.Equal(2, new RasterImage(8, 16, new byte[16]).BitImageBandCount);
    }

    [Fact]
    public void RasterImage_DefaultOrientation_IsTheEpsonConvention()
    {
        // default: rows top-down, bit 7 = leftmost dot, so the band is the source unchanged
        var data = new byte[8];
        data[0] = 0x80;

        var image = new RasterImage(8, 8, data);
        var band = image.GetBitImageBand(0).ToArray();

        Assert.Equal(0x80, band[0]);
        Assert.Equal(0x00, band[^1]);
    }

    [Fact]
    public void RasterImage_ReversedRowsBit0Left_MatchesTheMeasuredPayload()
    {
        // The "L" that rendered correctly on the measured unit was sent as
        // FF 01 01 01 01 01 01 01 for source rows 80 80 80 80 80 80 80 FF.
        byte[] source = [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0xFF];

        var image = new RasterImage(8, 8, source);
        var band = image
            .GetBitImageBand(0, BitImageOrientation.RowMajorReversedRowsBit0Left)
            .ToArray();

        Assert.Equal([0xFF, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01, 0x01], band);
    }

    [Fact]
    public void RasterImage_OrientationOnlyAffectsMyImagesNotSolidOnes()
    {
        // a fully lit band is identical under every orientation, which is why solid shapes
        // rendered correctly before the orientation was known
        var data = new byte[8];
        Array.Fill(data, (byte)0xFF);

        var image = new RasterImage(8, 8, data);

        foreach (var orientation in Enum.GetValues<BitImageOrientation>())
        {
            var band = image.GetBitImageBand(0, orientation).ToArray();
            Assert.All(band, b => Assert.Equal(0xFF, b));
        }
    }

    [Fact]
    public void Receipt_BitImage_EncodesColumnsDownThenAcross()
    {
        // 8x8 dots, with 0x80 (the leftmost dot of a row) set in the FIRST row and in the LAST
        // row only. Column 0 therefore runs black, white x6, black: top dot and bottom dot set,
        // the six dots between them clear, which is byte 0x81. A row-major reading would give
        // 0xFF, 0x00, ... instead.
        var source = new byte[8];
        source[0] = 0x80;
        source[7] = 0x80;
        var image = new RasterImage(8, 8, source);

        var bytes = new Receipt().BitImage(image).Build();

        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal(0x08, bytes[3]);            // 8 columns
        Assert.Equal(0x00, bytes[4]);
        Assert.Equal(0x81, bytes[5]);            // column 0: top and bottom dots only
        Assert.Equal(0x00, bytes[6]);            // column 1 is blank
    }

    [Fact]
    public void Receipt_BitImage_PutsTheColumnCountInN1AndN2()
    {
        // 16 dots wide = 16 columns = 0x10, so n1 = 0x10 and n2 = 0x00.
        var image = new RasterImage(16, 8, new byte[2 * 8]);

        var bytes = new Receipt().BitImage(image).Build();

        Assert.Equal([0x1B, 0x2A, 0x00, 0x10, 0x00], bytes[..5]);
        Assert.Equal(5 + 16, bytes.Length);
    }

    [Fact]
    public void RasterImage_PadsTheLastBandWithBlankRows()
    {
        var data = new byte[9];
        data[8] = 0xFF;

        var image = new RasterImage(8, 9, data);

        Assert.Equal(8, image.GetBitImageBand(1).Length);
        Assert.Contains(image.GetBitImageBand(1).ToArray(), b => b != 0);
    }

    [Fact]
    public void RasterImage_BandPreservesEveryByteOfEveryRow()
    {
        // 24 dots wide = 3 bytes per row. All 24 bytes of the band must survive.
        var data = new byte[3 * 8];
        Array.Fill(data, (byte)0xFF);

        var image = new RasterImage(24, 8, data);
        var band = image.GetBitImageBand(0).ToArray();

        Assert.Equal(24, band.Length);
        Assert.All(band, b => Assert.Equal(0xFF, b));
    }

    [Fact]
    public void Receipt_BitImage_EmitsOneCommandPerBand()
    {
        // 8 dots wide = 8 columns, one band of 8 rows = 8 data bytes.
        var image = new RasterImage(8, 8, new byte[8]);

        var bytes = new Receipt().BitImage(image).Build();

        Assert.Equal([0x1B, 0x2A, 0x00, 0x08, 0x00], bytes[..5]);
        Assert.Equal(5 + 8, bytes.Length);
    }

    [Fact]
    public void BitImageBand_PutsTheColumnCountInN1AndN2()
    {
        // 24 dots wide = 3 bytes per row, so one band is 3 columns and carries 24 bytes.
        var command = Ncr7197Commands.BitImageBand(BitImageMode.SingleDensity, new byte[8 * 3]);

        Assert.Equal([0x1B, 0x2A, 0x00, 0x03, 0x00], command.Bytes.Span[..5].ToArray());
        Assert.Equal(5 + 24, command.Bytes.Length);
    }

    [Fact]
    public void BitImageBand_SplitsTheColumnCountAcrossBothHeaderBytes()
    {
        // 576 columns = 0x240, so n1 = 0x40 and n2 = 0x02. This is the case the old
        // single-byte byte-count header could not express at all.
        var command = Ncr7197Commands.BitImageBand(BitImageMode.DoubleDensity, new byte[8 * 576]);

        Assert.Equal([0x1B, 0x2A, 0x01, 0x40, 0x02], command.Bytes.Span[..5].ToArray());
    }

    [Fact]
    public void BitImageBand_AcceptsAFullWidthBandAtSingleDensity()
    {
        // 384 columns is a plain 48 mm band at single density and is verified on hardware:
        // ESC * 0 128 1 with 384 bytes renders a clean full-width band. The head width, not
        // the mode, is the limit.
        var command = Ncr7197Commands.BitImageBand(BitImageMode.SingleDensity, new byte[8 * 384]);

        Assert.Equal([0x1B, 0x2A, 0x00, 0x80, 0x01], command.Bytes.Span[..5].ToArray());
        Assert.Equal(5 + (8 * 384), command.Bytes.Length);
    }

    [Fact]
    public void BitImageBand_RejectsABandWiderThanThePrintHead()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Ncr7197Commands.BitImageBand(BitImageMode.SingleDensity, new byte[8 * 577]));

    [Fact]
    public void BitImageBand_RejectsAPartialRow()
    {
        Assert.Throws<ArgumentException>(
            () => Ncr7197Commands.BitImageBand(BitImageMode.SingleDensity, new byte[7]));
    }

    [Fact]
    public void Receipt_BitImage_FeedsEightDotsBetweenBands()
    {
        // two bands: command, ESC J 8, command
        var image = new RasterImage(8, 16, new byte[16]);

        var bytes = new Receipt().BitImage(image).Build();

        var band = new List<byte> { 0x1B, 0x2A, 0x00, 0x08, 0x00 };
        band.AddRange(new byte[8]);

        var expected = new List<byte>(band);
        expected.AddRange([0x1B, 0x4A, 0x08]);
        expected.AddRange(band);

        Assert.Equal(expected, bytes);
    }

    [Fact]
    public void Receipt_BitImage_DoesNotFeedAfterTheLastBand()
    {
        var bytes = new Receipt().BitImage(new RasterImage(8, 8, new byte[8])).Build();

        Assert.Equal(5 + 8, bytes.Length);
        Assert.DoesNotContain((byte)0x4A, bytes);
    }

    [Fact]
    public void Receipt_BitImage_RejectsAnImageWiderThanThePrintableArea()
    {
        // 584 dots is past the 576-column limit of one band.
        var image = new RasterImage(584, 8, new byte[73 * 8]);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Receipt().BitImage(image, BitImageMode.DoubleDensity));
    }

    [Fact]
    public void Receipt_BitImage_Accepts248DotsWide()
    {
        // 248 dots = 31 columns -> 248 data bytes, just inside single density.
        var image = new RasterImage(248, 8, new byte[31 * 8]);

        var bytes = new Receipt().BitImage(image).Build();

        Assert.Equal([0x1B, 0x2A, 0x00, 0xF8, 0x00], bytes[..5]);
        Assert.Equal(5 + 248, bytes.Length);
    }
}
