using System.Text;
using Xunit;

namespace Ncr7197.NET.Tests;

public class NcrCommandTests
{
    [Fact]
    public void FullCut_HasExpectedBytes()
    {
        Assert.Equal(new byte[] { 0x19 }, Ncr7197Commands.FullCut().Bytes.ToArray());
    }

    [Fact]
    public void OpenDrawer_HasExpectedBytes()
    {
        Assert.Equal(new byte[] { 0x1B, 0x70, 0x00, 25, 250 }, Ncr7197Commands.OpenDrawer().Bytes.ToArray());
    }

    [Fact]
    public void Receipt_ComposesCommands()
    {
        var bytes = new Receipt(Encoding.ASCII)
            .Initialize()
            .Bold()
            .Text("HELLO")
            .Bold(false)
            .FeedLines()
            .Cut()
            .Build();

        Assert.Equal(0x10, bytes[0]);
        Assert.Contains((byte)0x1B, bytes);

        // the default cut asks the printer to feed to its cutting position
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, bytes[^4..]);
    }

    [Fact]
    public void Barcode_UsesNativeGsKCommand()
    {
        var data = new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes("5901234123457")).Span;
        var bytes = Ncr7197Commands.Barcode(BarcodeType.Ean13, data).Bytes.ToArray();
        Assert.Equal(new byte[] { 0x1D, 0x48, 0x00 }, bytes[..3]);
        Assert.Contains((byte)0x6B, bytes);
        Assert.Equal(0x00, bytes[^1]);
    }
}
