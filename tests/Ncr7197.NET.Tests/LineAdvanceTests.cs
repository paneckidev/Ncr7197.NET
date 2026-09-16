using Xunit;

namespace Ncr7197.Tests;

public class LineAdvanceTests
{
    [Fact]
    public void PrintAndFeedLines_IsParameterised()
    {
        Assert.Equal([0x1B, 0x64, 0x03], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.PrintAndFeedLines, 3)
            .Bytes.ToArray());
    }

    [Fact]
    public void LineFeed_RepeatsOneBytePerLine()
    {
        Assert.Equal([0x0A, 0x0A, 0x0A], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.LineFeed, 3)
            .Bytes.ToArray());
    }

    [Fact]
    public void CarriageReturnLineFeed_EmitsCrLfPerLine()
    {
        Assert.Equal([0x0D, 0x0A, 0x0D, 0x0A], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.CarriageReturnLineFeed, 2)
            .Bytes.ToArray());
    }

    [Fact]
    public void PrintAndFeedPaper_ScalesDotRowsPerLine()
    {
        Assert.Equal([0x1B, 0x4A, 0x1A], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.PrintAndFeedPaper, 1, dotRowsPerLine: 0x1A)
            .Bytes.ToArray());

        Assert.Equal([0x1B, 0x4A, 0x34], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.PrintAndFeedPaper, 2, dotRowsPerLine: 0x1A)
            .Bytes.ToArray());
    }

    [Fact]
    public void PrintAndFeedPaper_SaturatesInsteadOfWrapping()
    {
        Assert.Equal([0x1B, 0x4A, 0xFF], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.PrintAndFeedPaper, 10, dotRowsPerLine: 0x40)
            .Bytes.ToArray());
    }

    [Fact]
    public void FeedPrintLines_KeepsTheNativeCommandAvailable()
    {
        Assert.Equal([0x14, 0x02], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.FeedPrintLines, 2)
            .Bytes.ToArray());
    }

    [Fact]
    public void Advance_TreatsZeroLinesAsOne()
    {
        Assert.Equal([0x1B, 0x64, 0x01], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.PrintAndFeedLines, 0)
            .Bytes.ToArray());

        Assert.Equal([0x1B, 0x4A, 0x1A], Ncr7197VerticalMotion
            .Advance(LineAdvanceMode.PrintAndFeedPaper, 0)
            .Bytes.ToArray());
    }

    [Fact]
    public void Receipt_Line_UsesTheConfiguredAdvance()
    {
        var receipt = new Receipt(lineAdvance: LineAdvanceMode.LineFeed)
            .Text("Hello")
            .Line();

        Assert.Equal(
            [0x48, 0x65, 0x6C, 0x6C, 0x6F, 0x0A],
            receipt.Build());
    }

    [Fact]
    public void Receipt_DefaultAdvance_IsDotBasedAndNotTheIgnoredNativeCommand()
    {
        var receipt = new Receipt().Line("AB");

        Assert.Equal([0x41, 0x42, 0x1B, 0x4A, 0x1A], receipt.Build());
    }

    [Fact]
    public void Receipt_FeedLines_ScalesTheDotFeed()
    {
        var receipt = new Receipt().FeedLines(4);

        Assert.Equal([0x1B, 0x4A, 0x68], receipt.Build());
    }

    [Fact]
    public void Receipt_FeedLines_IsOneCommandInPrintAndFeedLinesMode()
    {
        var receipt = new Receipt(lineAdvance: LineAdvanceMode.PrintAndFeedLines).FeedLines(4);

        Assert.Equal([0x1B, 0x64, 0x04], receipt.Build());
    }

    [Fact]
    public void Receipt_DotRowsPerLine_IsHonoured()
    {
        var receipt = new Receipt { DotRowsPerLine = 0x19 }.Line("A");

        Assert.Equal([0x41, 0x1B, 0x4A, 0x19], receipt.Build());
    }

    [Fact]
    public void Receipt_FeedDots_StaysDotBased()
    {
        var receipt = new Receipt().FeedDots(0x60);

        Assert.Equal([0x15, 0x60], receipt.Build());
    }

    [Fact]
    public void Receipt_LineAdvance_CanBeChangedAfterConstruction()
    {
        var receipt = new Receipt { LineAdvance = LineAdvanceMode.CarriageReturnLineFeed };
        receipt.Line("A");

        Assert.Equal([0x41, 0x0D, 0x0A], receipt.Build());
    }

    [Fact]
    public void Receipt_Cut_FeedsToTheCuttingPositionByDefault()
    {
        var receipt = new Receipt().Line("A").Cut();

        Assert.Equal([0x41, 0x1B, 0x4A, 0x1A, 0x1D, 0x56, 0x42, 0x00], receipt.Build());
    }

    [Fact]
    public void Receipt_Cut_ExtraMarginIsPassedToThePrinter()
    {
        var receipt = new Receipt { CutMarginDots = 200 }.Line("A").Cut();

        Assert.Equal([0x41, 0x1B, 0x4A, 0x1A, 0x1D, 0x56, 0x42, 0xC8], receipt.Build());
    }

    [Fact]
    public void Receipt_Cut_NoHostFeedWithoutAnExtraMargin()
    {
        // regression guard: the host must NOT emit 0x1B 0x4A padding for the default cut
        var receipt = new Receipt().Line("A").Cut();

        Assert.Equal(8, receipt.Build().Length);
    }

    [Fact]
    public void Receipt_NativeCut_TopsUpToTheMeasuredCutterDistance()
    {
        // native 0x19 cannot feed to the cutter, so the host must cover 480 dot rows
        var receipt = new Receipt { CutStyle = CutStyle.Native }.Line("A").Cut();

        // 26 fed by the line, then 255 + 199 to reach 480, then the native cut
        Assert.Equal(
            [0x41, 0x1B, 0x4A, 0x1A, 0x1B, 0x4A, 0xFF, 0x1B, 0x4A, 0xC7, 0x19],
            receipt.Build());
    }

    [Fact]
    public void Receipt_NativeCut_DoesNotFeedWhenTheDistanceIsAlreadyCovered()
    {
        var receipt = new Receipt { CutStyle = CutStyle.Native }.FeedLines(20).Cut();

        Assert.Equal([0x1B, 0x4A, 0xFF, 0x19], receipt.Build());
    }

    [Fact]
    public void Receipt_NativeCut_AccountsForFeedDots()
    {
        // 26 (line) + 200 (dots) = 226 fed, 254 missing for the native cut
        var receipt = new Receipt { CutStyle = CutStyle.Native }.Line("A").FeedDots(200).Cut();

        Assert.Equal([0x41, 0x1B, 0x4A, 0x1A, 0x15, 0xC8, 0x1B, 0x4A, 0xFE, 0x19], receipt.Build());
    }
}
