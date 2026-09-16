using System.IO.Ports;

namespace Ncr7197;

public sealed record SerialPrinterOptions(
    string PortName,
    int BaudRate = 9600,
    int DataBits = 8,
    StopBits StopBits = StopBits.One,
    Parity Parity = Parity.None,
    Handshake Handshake = Handshake.None,
    bool DtrEnable = true,
    bool RtsEnable = true,
    int ReadTimeoutMs = 1000,
    int WriteTimeoutMs = 1000);

/// <summary>RS-232/virtual-COM transport backed by System.IO.Ports.</summary>
public sealed class SerialPrinterTransport : IPrinterTransport
{
    private readonly SerialPrinterOptions _options;
    private SerialPort? _port;

    public SerialPrinterTransport(SerialPrinterOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    public bool IsOpen => _port?.IsOpen == true;

    public ValueTask OpenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOpen)
            return ValueTask.CompletedTask;

        if (_port is not null)
        {
            // Reopening an existing port: .NET keeps DTR/RTS asserted, so a printer using
            // DTR/DSR flow control never sees a clean transition. Toggle the control lines
            // while the port is closed so the printer re-arms its handshake.
            ToggleControlLines();
            _port.Dispose();
        }

        _port = new SerialPort(
            _options.PortName,
            _options.BaudRate,
            _options.Parity,
            _options.DataBits,
            _options.StopBits)
        {
            Handshake = _options.Handshake,
            DtrEnable = _options.DtrEnable,
            RtsEnable = _options.RtsEnable,
            ReadTimeout = _options.ReadTimeoutMs,
            WriteTimeout = _options.WriteTimeoutMs
        };

        _port.Open();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Drives DTR and RTS false and back. Needed because the Windows serial driver keeps
    /// them asserted across a close/open cycle, which leaves a DTR/DSR printer thinking the
    /// host is still ready to receive when it is not.
    /// </summary>
    private void ToggleControlLines()
    {
        try
        {
            _port!.DtrEnable = false;
            _port.RtsEnable = false;
            Thread.Sleep(ControlLineToggleDelayMs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The port may already be gone; there is nothing useful to do about it here.
        }
    }

    private const int ControlLineToggleDelayMs = 60;

    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _port?.Close();
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();

        // SerialPort.Write has no Memory<byte> overload. Copying is intentional here;
        // the actual I/O remains synchronous because System.IO.Ports exposes it that way.
        _port!.Write(data.Span.ToArray(), 0, data.Length);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();

        // SerialPort.Read(byte[], int, int) is the synchronous API and does not accept
        // a CancellationToken. BaseStream exposes the modern Stream.ReadAsync overload,
        // which gives RequestAsync real cancellation/timeout semantics.
        return await _port!.BaseStream
            .ReadAsync(buffer, cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        _port?.Dispose();
        _port = null;
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (!IsOpen)
            throw new InvalidOperationException("The printer transport is not open.");
    }
}
