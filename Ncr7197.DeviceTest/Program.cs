using System.IO.Ports;
using System.Text;
using Ncr7197;

Console.WriteLine("NCR 7197.NET - hardware test");
Console.WriteLine("=============================");
Console.WriteLine();

var portName = args.Length > 0 ? args[0] : Prompt("COM port", "COM9");
var baudRate = args.Length > 1 && int.TryParse(args[1], out var parsedBaud)
    ? parsedBaud
    : 9600;

Console.WriteLine();
Console.WriteLine($"Port: {portName}");
Console.WriteLine($"Baud: {baudRate}");
Console.WriteLine();

try
{
    var transport = new SerialPrinterTransport(
        new SerialPrinterOptions(
            PortName: portName,
            BaudRate: baudRate,
            DataBits: 8,
            StopBits: StopBits.One,
            Parity: Parity.None,
            Handshake: Handshake.None,
            DtrEnable: true,
            RtsEnable: true));

    await using var printer = new Ncr7197Printer(transport);

    Console.WriteLine("Opening serial port...");
    await printer.OpenAsync();
    Console.WriteLine("OK");
    Console.WriteLine();

    while (true)
    {
        PrintMenu();

        var choice = Console.ReadLine()?.Trim().ToLowerInvariant();

        try
        {
            switch (choice)
            {
                case "1":
                    await Initialize(printer);
                    break;

                case "2":
                    await PrintText(printer);
                    break;

                case "3":
                    await PrintReceipt(printer);
                    break;

                case "m":
                    await PrintReceiptFixed(printer);
                    break;

                case "4":
                    await PrintBarcode(printer);
                    break;

                case "5":
                    await PrintRasterTest(printer);
                    break;

                case "i":
                    await PrintImage(printer);
                    break;

                case "6":
                    await GetPrinterStatus(printer);
                    break;

                case "7":
                    await GetDrawerStatus(printer);
                    break;

                case "8":
                    await GetSoftwareVersion(printer);
                    break;

                case "9":
                    await printer.OpenDrawerAsync();
                    Console.WriteLine("Open-drawer command sent.");
                    break;

                case "c":
                    await printer.CutAsync();
                    Console.WriteLine("Full-cut command sent.");
                    break;

                case "p":
                    await printer.SendAsync(NcrCommand.Text(Encoding.ASCII.GetBytes(
                        "NCR 7197.NET raw transport test\r\n")));
                    Console.WriteLine("Raw text command sent.");
                    break;

                case "q":
                    return;

                default:
                    Console.WriteLine("Unknown option.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"ERROR: {ex.GetType().Name}");
            Console.WriteLine(ex.Message);

            if (ex.InnerException is not null)
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("Press Enter to continue...");
        Console.ReadLine();
    }
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Could not open/use the printer.");
    Console.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    Environment.ExitCode = 1;
}

static async Task Initialize(Ncr7197Printer printer)
{
    await printer.InitializeAsync();
    Console.WriteLine("Initialize command sent.");
}

static async Task PrintText(Ncr7197Printer printer)
{
    var text = Prompt("Text", "Hello from Ncr7197.NET!");

    var receipt = new Receipt()
        .Initialize()
        .Align(Alignment.Center)
        .Bold()
        .Line("NCR 7197.NET")
        .Bold(false)
        .Align(Alignment.Left)
        .Line(text)
        .FeedLines(3)
        .Cut();

    await printer.PrintAsync(receipt);
    Console.WriteLine("Text test printed.");
}

static async Task PrintReceipt(Ncr7197Printer printer)
{
    var receipt = new Receipt()
        .Initialize()
        .Align(Alignment.Center)
        .Bold()
        .DoubleWidth()
        .Line("NCR 7197.NET")
        .DoubleWidth(false)
        .Bold(false)
        .Line("Hardware test")
        .Line("------------------------------")
        .Align(Alignment.Left)
        .Line("Product                 Price")
        .Line("Test item              12.34")
        .Line("Another item             5.00")
        .Line("------------------------------")
        .Bold()
        .Align(Alignment.Right)
        .Line("TOTAL                  17.34")
        .Bold(false)
        .Align(Alignment.Center)
        .Line("")
        .Line("Thank you!")
        .FeedLines(3)
        .Cut();

    await printer.PrintAsync(receipt);
    Console.WriteLine("Receipt test printed.");
}

static async Task PrintReceiptFixed(Ncr7197Printer printer)
{
    // Same layout as procedure 3, but the receipt is kept inside 32 columns
    // (58 mm paper) and the line advance is the one this unit actually obeys.
    var receipt = new Receipt(lineAdvance: LineAdvanceMode.PrintAndFeedLines)
        .Initialize()
        .Align(Alignment.Center)
        .Bold()
        .DoubleWidth()
        .Line("NCR 7197")
        .DoubleWidth(false)
        .Bold(false)
        .Line("Hardware test")
        .Line("------------------------------")
        .Align(Alignment.Left)
        .Line("Product             Price")
        .Line("Test item           12.34")
        .Line("Another item         5.00")
        .Line("------------------------------")
        .Bold()
        .Align(Alignment.Right)
        .Line("TOTAL  17.34")
        .Bold(false)
        .Align(Alignment.Center)
        .Line("")
        .Line("Thank you!")
        .FeedLines(3)
        .Cut();

    await printer.PrintAsync(receipt);
    Console.WriteLine($"Receipt printed using {receipt.LineAdvance}.");
}

static async Task PrintBarcode(Ncr7197Printer printer)
{
    Console.WriteLine("EAN-13 test value: 5901234123457");

    var data = new ReadOnlyMemory<byte>(
        Encoding.UTF8.GetBytes("5901234123457")).Span;

    var command = Ncr7197Commands.Barcode(
        BarcodeType.Ean13,
        data,
        BarcodeTextPosition.Below,
        width: 2,
        height: 60);

    await printer.SendAsync(
        NcrCommand.Concat(
            Ncr7197Commands.Initialize(),
            NcrCommand.Text(Encoding.ASCII.GetBytes("\r\nEAN-13 test\r\n")),
            command,
            Ncr7197Commands.FeedLines(3),
            Ncr7197Commands.FullCut()));

    Console.WriteLine("EAN-13 barcode command sent.");
}

static async Task PrintRasterTest(Ncr7197Printer printer)
{
    // A solid block spanning the full printable width: 576 dots = 72 bytes per row. 64 rows is
    // about 8 mm at 203 dpi, so the block is unmistakable rather than a thin line.
    const int rowBytes = Ncr7197Commands.RasterRowBytes;
    const int height = 64;

    var data = new byte[rowBytes * height];
    Array.Fill(data, (byte)0xFF);

    var image = new RasterImage(rowBytes * 8, height, data);

    var receipt = new Receipt()
        .Initialize()
        .Align(Alignment.Center)
        .Line("Raster test")
        .Raster(image)
        .Line()
        .Line("--- end ---")
        .FeedLines(3)
        .Cut();

    await printer.PrintAsync(receipt);
    Console.WriteLine($"Raster sent: {image.Width}x{height} dots ({image.Width / 8} bytes per row, {height} rows).");
}

static async Task PrintImage(Ncr7197Printer printer)
{
    // Prints a picture file through the raster path: one DC1 (0x11) row per image row, each
    // command carrying a full 72-byte (576-dot) row. That is the simplest verified way to put a
    // bitmap on this family - no column-count header to get wrong, and each row advances the
    // paper by itself.
    var suggested = FindSampleImage();
    var path = ResolveInputPath(Prompt("Image file", suggested ?? ""));

    if (path is null)
    {
        Console.WriteLine("No file given.");
        return;
    }

    if (!File.Exists(path))
    {
        // Say exactly what was looked for and where, rather than leaving the user guessing
        // whether the path or the working directory was wrong.
        Console.WriteLine($"No such file: {path}");
        Console.WriteLine($"  resolved from working directory: {Directory.GetCurrentDirectory()}");

        if (suggested is not null) Console.WriteLine($"  an image that does exist: {suggested}");

        return;
    }

    var widthText = Prompt("Dot width (384 keeps the source pixels 1:1, 576 fills the paper)", "384");
    var width = int.Parse(widthText);
    width = Math.Clamp(width / 8 * 8, 8, Ncr7197Commands.RasterRowBytes * 8);

    var useDither = !string.Equals(Prompt("Dither (y/n)", "y"), "n", StringComparison.OrdinalIgnoreCase);

    var (grey, w, h) = ImageLoader.LoadGrey(path, width);

    bool[] dots = useDither
        ? Dither.FloydSteinberg(grey, w, h, 128)
        : Threshold(grey, 128);

    // Centre the picture in the printable row so it does not sit against the left margin.
    var margin = (Ncr7197Commands.RasterRowBytes * 8) - w;
    var shift = margin > 0 ? margin / 2 : 0;

    Console.WriteLine($"{Path.GetFileName(path)} -> {w}x{h} dots, {dots.Count(d => d)} printed, shift {shift} dots.");

    var receipt = new Receipt()
        .Initialize()
        .Align(Alignment.Center)
        .Line(Path.GetFileName(path))
        .FeedLines(1);

    for (var y = 0; y < h; y++)
    {
        var row = new byte[Ncr7197Commands.RasterRowBytes];

        for (var x = 0; x < w; x++)
        {
            if (!dots[(y * w) + x]) continue;

            // One byte covers 8 horizontal dots and bit 7 is the leftmost of the eight.
            var target = x + shift;
            row[target / 8] |= (byte)(0x80 >> (target % 8));
        }

        receipt.RasterRow(row);
    }

    receipt.FeedLines(3).Cut();

    await printer.PrintAsync(receipt);
    Console.WriteLine($"Image sent: {h} raster rows, {receipt.Build().Length} bytes.");
}

static bool[] Threshold(byte[] grey, int level)
{
    var result = new bool[grey.Length];

    for (var i = 0; i < grey.Length; i++)
        result[i] = grey[i] < level;

    return result;
}

static async Task GetPrinterStatus(Ncr7197Printer printer)
{
    var status = await printer.GetPrinterStatusAsync();

    Console.WriteLine($"Bytes: {status.Data.Length}");
    Console.WriteLine($"HEX:   {Convert.ToHexString(status.Data)}");

    if (status.HasData)
        Console.WriteLine($"Byte0: 0x{status.PrimaryByte:X2}");
}

static async Task GetDrawerStatus(Ncr7197Printer printer)
{
    var status = await printer.GetDrawerStatusAsync();

    Console.WriteLine($"Bytes: {status.Data.Length}");
    Console.WriteLine($"HEX:   {Convert.ToHexString(status.Data)}");

    if (status.HasData)
        Console.WriteLine($"Byte0: 0x{status.PrimaryByte:X2}");
}

static async Task GetSoftwareVersion(Ncr7197Printer printer)
{
    var status = await printer.GetSoftwareVersionAsync();

    Console.WriteLine($"Bytes: {status.Data.Length}");
    Console.WriteLine($"HEX:   {Convert.ToHexString(status.Data)}");

    if (status.HasData)
    {
        var printable = Encoding.ASCII.GetString(status.Data)
            .Replace("\0", "\\0");

        Console.WriteLine($"ASCII: {printable}");
    }
}

static void PrintMenu()
{
    Console.WriteLine("""
        1 - Initialize printer
        2 - Print text
        3 - Print receipt (line advance broken on units that ignore 0x14)
        M - Print receipt with working line advance (0x1B 0x64 n)
        4 - Print EAN-13 barcode
        5 - Print raster test
        I - Print an image file
        6 - Read printer status
        7 - Read drawer status
        8 - Read software version
        9 - Open cash drawer
        C - Cut paper
        P - Raw text test
        Q - Quit
        """);

    Console.Write("Select: ");
}

static string Prompt(string name, string defaultValue)
{
    Console.Write($"{name} [{defaultValue}]: ");
    var value = Console.ReadLine()?.Trim();

    return string.IsNullOrWhiteSpace(value)
        ? defaultValue
        : value;
}

// A relative path is taken against the working directory, which depends on where the app was
// launched from. Naming a file that exists makes the wrong-directory case impossible to miss.
static string? FindSampleImage()
{
    var roots = new List<string> { Directory.GetCurrentDirectory() };

    // Also look beside the executable and upwards, so `dotnet run` from the project folder
    // still finds the repository's assets.
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory is not null)
    {
        roots.Add(directory.FullName);
        directory = directory.Parent;
    }

    foreach (var root in roots)
    {
        var assets = Path.Combine(root, "assets");
        if (!Directory.Exists(assets)) continue;

        var image = Directory.EnumerateFiles(assets)
            .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .FirstOrDefault();

        if (image is not null) return image;
    }

    return null;
}

static string? ResolveInputPath(string input)
{
    var trimmed = input.Trim().Trim('"');
    if (trimmed.Length == 0) return null;

    return Path.GetFullPath(trimmed);
}
