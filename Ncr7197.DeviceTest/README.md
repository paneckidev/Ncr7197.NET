# Ncr7197.DeviceTest

An interactive console app for checking a physical NCR 7197 / 7198 printer through the
`Ncr7197.NET` library. It prints real paper, so run it with the printer connected and loaded.

## Running

```bash
dotnet run --project Ncr7197.DeviceTest -- COM9 19200
```

Both arguments are optional. The project targets `net9.0-windows` because image loading uses
`System.Drawing.Common`.

The library is referenced with `ProjectReference`, so there is nothing to package first.

## Menu

```text
1 - Initialize printer
2 - Print text
3 - Print receipt
M - Print receipt with an alternative line advance
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
```

### Printing an image

Option `I` prints a picture file. It asks for:

- **image file** — any format `System.Drawing` can read (PNG, JPEG, BMP, GIF);
- **dot width** — `384` keeps a 384-dot source at 1:1 and is the safe default, `576` fills the
  printable width but resamples the picture;
- **dither** — Floyd-Steinberg error diffusion, which is what makes photographs readable;
  answering `n` uses a plain threshold instead.

The image is centred in the 576-dot printable row and printed through the raster (`DC1`) path,
one row per command.

`assets/thermal-test.png` (384x288) and `assets/sheep.png` are useful test pictures.

## Suggested first run

1. `1` Initialize
2. `P` Raw text — checks the transport alone
3. `2` Print text
4. `C` Cut
5. `3` Print receipt
6. `4` EAN-13
7. `I` Print an image
8. `6` Printer status and `8` Software version

Test `9` (cash drawer) last: it pulses the drawer port, so only run it with a drawer attached.

## If the printer does not respond

Check the port number, the baud rate, 8 data bits, no parity, 1 stop bit, and that DTR and RTS
are enabled — this family uses a DTR/DSR handshake. `RequestAsync` has a timeout, so a command
the firmware does not implement ends in a timeout rather than hanging.

Status reads (`6`, `7`, `8`) can behave differently from the printing tests, because they need
a reply from the device.

## Adding a procedure

Handlers are static local functions at the bottom of `Program.cs`, registered in the menu
`switch` and listed in `PrintMenu`. Use `printer.PrintAsync(receipt)` for a `Receipt`, or
`printer.SendAsync(command)` for raw bytes.
