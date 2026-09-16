# Ncr7197.NET

A .NET 9 library for driving NCR 7197 / 7198 thermal receipt printers directly over a serial
port, in **7194 Native Mode**.

Text, barcodes, raster graphics, bit images, paper feed, cutting, the cash drawer and status
reads are all implemented and verified against a physical NCR 7198.

## Projects

```text
src/Ncr7197.NET              the library
  IPrinterTransport.cs       transport abstraction, so tests and bridges can substitute one
  SerialPrinterTransport.cs  RS-232 / virtual COM implementation
  Ncr7197Printer.cs          serialized high-level client
  Ncr7197Commands.cs         the native command set
  Receipt.cs                 fluent document builder
  Graphics.cs                monochrome raster image and bit-image banding
  LineAdvance.cs             vertical motion: which command moves the paper, and how far
  PrinterStatus.cs           raw status response wrapper
  NcrCommand.cs              a command as bytes
  PrinterExceptions.cs       communication exceptions

tests/Ncr7197.NET.Tests      unit tests, no hardware required
Ncr7197.DeviceTest           interactive menu that talks to a real printer and prints paper
```

## Quick start

```csharp
using Ncr7197;
using System.IO.Ports;

var transport = new SerialPrinterTransport(new SerialPrinterOptions(
    PortName: "COM9",
    BaudRate: 19200,
    DataBits: 8,
    StopBits: StopBits.One,
    Parity: Parity.None,
    Handshake: Handshake.None,
    DtrEnable: true,
    RtsEnable: true));

await using var printer = new Ncr7197Printer(transport);
await printer.OpenAsync();

var receipt = new Receipt()
    .Reset()
    .Align(Alignment.Center)
    .Bold()
    .DoubleWidth()
    .Line("NCR 7198")
    .DoubleWidth(false)
    .Bold(false)
    .Align(Alignment.Left)
    .Line("Hello from .NET")
    .Barcode(BarcodeType.Code39, "NCR7198")
    .FeedLines(3)
    .Cut();

await printer.PrintAsync(receipt);
```

`Reset()` emits `ESC @` (`1B 40`). Put it first in any document that follows other output: it
clears the input *and* print buffers, and without it residue from earlier commands can corrupt
what follows.

## Printing graphics

Two commands print bitmaps on this family. **`Raster()` is the one to use.**

### `Raster()` — one printer row per command

`DC1` (`0x11`) followed by **72 bytes = 576 dots = one whole 80 mm row**. The command has no
length byte, prints the row immediately and advances the paper exactly one dot row, so
successive rows stack with nothing in between.

```csharp
using var stream = File.OpenRead("logo.png");
var image = LoadYourImage(stream, width: 384);   // RasterImage, 1 bit per dot

await printer.PrintAsync(new Receipt()
    .Reset()
    .Align(Alignment.Center)
    .Raster(image)
    .FeedLines(3)
    .Cut());
```

Within a row, one byte covers 8 **horizontal** dots and **bit 7 is the leftmost** of the eight.
**A set bit is a printed dot**, so `0xFF` is black and `0x00` is white. A row shorter than 72
bytes is padded with white on the right; `Receipt.RasterRow(ReadOnlySpan<byte>)` emits a single
row if you would rather stream an image than hold it in memory.

Do **not** interleave a feed command between raster rows — each row already advances the paper,
and an extra feed breaks the picture into strips.

### `BitImage()` — 8-dot bands

`ESC * m n1 n2` prints one band of 8 dot rows. Here `n1` is the **low byte** and `n2` the
**high byte** of the horizontal column count: `columns = n1 + 256 * n2`, one data byte per
column. There is no vertical-size parameter. Data is **column-major** — one byte is 8
*vertical* dots with bit 7 at the top — and `Receipt.BitImage()` encodes that, advancing one
band with `ESC J 8` between commands.

Prefer `Raster()` unless you specifically need `ESC *`: it is bounded to 288 columns at single
density and 576 at double density, and its header is easier to get wrong.

### Rendering an image to dots

The library takes a 1-bit `RasterImage`; it does not decode image files. Converting a
photograph to dots needs a threshold or, much better, error diffusion — `Ncr7197.DeviceTest`
contains a worked example (`ImageLoader.cs` scales and converts to greyscale, `Dither.cs`
implements Floyd-Steinberg).

`--width 384` reproduces a 384-dot source 1:1; `--width 576` fills the paper but resamples.
To centre a narrower image, shift it right by `(576 - width) / 2` dots.

## Paper feed and line height

`Line()` and `FeedLines()` are sugar over one vertical-motion command, and **which command that
is decides whether the paper actually moves**. Some units silently ignore the documented
`0x14 n` ("Feed n Print Lines"): text prints but never advances, so every line overwrites the
previous one — which looks exactly like "the printer ignores `.FeedLines()`".

Measured on a unit that ignores `0x14`:

| Command | Result |
| --- | --- |
| `0x14 n` — Feed n Print Lines | **ignored** |
| `0x0A` — Print and Feed One Line | works |
| `0x0D 0x0A` — CR + LF | works |
| `0x1B 0x64 n` — Print and Feed n Lines | works, pitch one line spacing too large |
| `0x1B 0x4A n` — Print and Feed Paper | works, exact pitch |
| `0x15 n` — Feed n Dot Rows | works |

The default is `0x1B 0x4A` with **26 dot rows**, which is one line at 203 dpi (1/6 inch). If
your unit behaves differently, pick another primitive:

```csharp
var defaultAdvance = new Receipt();                                       // ESC J 26
var lineFeed      = new Receipt(lineAdvance: LineAdvanceMode.LineFeed);   // 0x0A
var escD          = new Receipt(lineAdvance: LineAdvanceMode.PrintAndFeedLines);
var native        = new Receipt(lineAdvance: LineAdvanceMode.FeedPrintLines);

var tight = new Receipt { DotRowsPerLine = 0x19 };                        // ESC J 25
```

## Cutting

The native cuts (`0x19`, `0x1B 0x6D`) cut at a **fixed position**; the blade sits roughly
58–60 mm downstream of the print head, so the host must feed that whole distance or the blade
lands in the middle of a printed line. The classic symptom is a receipt cut through the
`TOTAL` line.

`GS V 66 n` (`1D 56 42 n`) makes the printer feed to its own cutting position instead, and
`Cut()` uses it by default:

| Command | Result |
| --- | --- |
| `GS V 66 0` | feeds to the cutting position, clean cut below the text |
| `GS V 66 200` | the same, plus 200 dot rows of margin |
| `GS V 1` | cuts immediately, **through the printed text** |
| `0x19` | cuts at a fixed position, needs ~480 dot rows of host feed |

```csharp
var receipt = new Receipt().Line("Thank you!").Cut();          // printer positions the cut
var roomy   = new Receipt { CutMarginDots = 200 };             // extra margin, still positioned
var native  = new Receipt { CutStyle = CutStyle.Native };      // documented native command
```

`CutMarginDots` is only the *extra* margin and defaults to `0`. `CutStyle.Native` tops the feed
up to `Receipt.NativeCutDistanceDots` (480), counting any trailing `FeedLines`/`FeedDots`
towards it. NCR documents `0x19` and `0x1B 0x6D` as performing the same physical cut, each
leaving a 5 mm tab, so `Cut(bool full)` does not select a different physical result here.

## Status

```csharp
var status = await printer.GetPrinterStatusAsync();
Console.WriteLine(status);                              // raw bytes
Console.WriteLine(status.PrimaryByte.ToString("X2"));

await printer.GetDrawerStatusAsync();
await printer.GetTransmitStatusAsync(0x01);
await printer.GetSoftwareVersionAsync();                // e.g. "0.01.0035.26.00"
```

Responses are **not** interpreted bit by bit, because the meaning depends on firmware and
configuration; use `PrinterStatusResponse.Data`.

`RequestAsync` collects a response until the printer goes quiet, `maxBytes` is reached, or the
timeout expires. This matters: a 7197 answers `PrinterStatus` with a *single* byte, so a reader
that waits for a fixed length blocks forever and holds the COM port open.

A unit can also report a transient non-zero status immediately after a long image (for example
`0x16`, bits 4 and 2) while the mechanism finishes; it clears within about a second.

## Barcodes

```csharp
.Barcode(BarcodeType.Code39, "NCR7198")
.Barcode(BarcodeType.Ean13, "5901234123457", BarcodeTextPosition.Below, width: 2, height: 60)
```

UPC-A, UPC-E, EAN-13, EAN-8, Code 39, Code 128, Interleaved 2 of 5, Codabar, Code 93, Code 11
and MSI are defined.

## Device notes from the verified unit

```text
Model (diagnostics form) : 7197 Series II / 5001-9001
Printer Emulation        : 7194 Native Mode
Baud rate                : 19200
Flow control             : DTR/DSR, 4 KB receive buffer
Columns per line         : 44 (80 mm paper)
```

`DtrEnable` and `RtsEnable` must be **true** for a DTR/DSR handshake: `SerialPrinterTransport`
re-arms them around `OpenAsync`.

`Receipt` does not wrap text. Longer lines wrap at the paper's right margin.

## Known limitations

- **Two-sided printing is not reachable from 7194 Native Mode.** The `1F 60`–`1F 6D` command
  set that selects thermal printing modes, printing side and back-side data belongs to the 7168
  family; this firmware does not answer `1F 6D` (the mode query). Printing on both sides also
  requires two-sided ("2ST") thermal paper, which reacts to two different activation
  temperatures — on ordinary thermal paper the back cannot be marked at all.
- **`ESC .` (Print Advanced Raster Graphics) is only verified for single-byte payloads.**
  `1B 2E 00 01 01 00 FF` prints one 8-dot raster and `1B 2E 00 01 08 00 FF` repeats it 8 times
  (`r` is a repeat count). Multi-byte payloads were never made to render; use `Raster()`.
- **`GS *` / `GS /` (downloaded bit image)** are documented for this family but not implemented.
- **The exact dot pitch is unmeasured.** Everything is consistent with 1 dot = 1/203 inch
  (384 dots ≈ 48 mm), but no print has been checked against a ruler.
- The first print after a long pause can show faint horizontal banding. Later prints are clean,
  so this is thermal warm-up rather than a data problem.

## `Ncr7197.DeviceTest`

An interactive menu for checking a real printer: initialization, text, receipts, barcodes, an
image file, a raster test, status and version reads, drawer pulse and cut. It targets
`net9.0-windows` because image loading uses `System.Drawing.Common`.

```bash
dotnet run --project Ncr7197.DeviceTest -- COM9 19200
```

Press `I` to print an image file: it prompts for a path, a dot width (384 keeps the source
pixels 1:1, 576 fills the paper), and whether to dither.

## Tests

```bash
dotnet test
```

No hardware and no printer required.
