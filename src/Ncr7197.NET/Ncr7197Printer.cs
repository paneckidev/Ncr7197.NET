namespace Ncr7197;

/// <summary>High-level asynchronous client for NCR 7197 native mode.</summary>
public sealed class Ncr7197Printer : IAsyncDisposable
{
    /// <summary>
    /// Grace period granted for each chunk after the first one when collecting a
    /// status response, so a short answer is returned promptly instead of waiting
    /// out the full timeout for bytes that are never coming.
    /// </summary>
    private const int InterChunkGraceMs = 60;

    /// <summary>Delay between polls while waiting for the first response byte.</summary>
    private const int PollIntervalMs = 5;

    private readonly IPrinterTransport _transport;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Ncr7197Printer(IPrinterTransport transport)
        => _transport = transport ?? throw new ArgumentNullException(nameof(transport));

    public bool IsOpen => _transport.IsOpen;

    public ValueTask OpenAsync(CancellationToken cancellationToken = default)
        => _transport.OpenAsync(cancellationToken);

    public ValueTask CloseAsync(CancellationToken cancellationToken = default)
        => _transport.CloseAsync(cancellationToken);

    public Task PrintAsync(Receipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return SendAsync(receipt.Build(), cancellationToken);
    }

    public Task SendAsync(NcrCommand command, CancellationToken cancellationToken = default)
        => SendAsync(command.Bytes, cancellationToken);

    public async Task SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await _transport.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not Ncr7197Exception)
            {
                throw new Ncr7197CommunicationException("Failed to write data to the NCR 7197.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<byte[]> RequestAsync(
        NcrCommand command,
        int maxBytes = 32,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (timeout is { } t && t <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await _transport.WriteAsync(command.Bytes, cancellationToken).ConfigureAwait(false);
                var buffer = new byte[maxBytes];
                var read = 0;
                var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(1));

                // A status response is a short burst, not a fixed-length frame: a 7197
                // answers e.g. PrinterStatus with exactly one byte, tens of milliseconds
                // later. So we keep polling until the printer has actually said something
                // and then gone quiet, rather than insisting on filling the whole buffer.
                while (read < buffer.Length)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;

                    // Once something has arrived, only a short grace window is granted, so
                    // a complete multi-byte answer is collected without burning the full
                    // timeout on bytes that are never coming.
                    var window = read == 0 ? remaining : TimeSpan.FromMilliseconds(InterChunkGraceMs);
                    if (window > remaining) window = remaining;

                    int count;
                    try
                    {
                        count = await ReadChunkAsync(buffer.AsMemory(read), window, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (TimeoutException)
                    {
                        break;
                    }

                    if (count > 0)
                    {
                        read += count;
                        continue;
                    }

                    // SerialStream.ReadAsync can complete with 0 bytes when the port is
                    // simply empty, which is NOT end-of-response. Wait for the deadline.
                    if (read == 0)
                    {
                        await Task.Delay(PollIntervalMs, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    break;
                }
                return buffer[..read];
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not Ncr7197Exception)
            {
                throw new Ncr7197CommunicationException("Failed to read a response from the NCR 7197.", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => SendAsync(Ncr7197Commands.Initialize(), cancellationToken);

    public Task CutAsync(bool full = true, CancellationToken cancellationToken = default)
        => SendAsync(full ? Ncr7197Commands.FullCut() : Ncr7197Commands.PartialCut(), cancellationToken);

    public Task OpenDrawerAsync(byte drawer = 0, CancellationToken cancellationToken = default)
        => SendAsync(Ncr7197Commands.OpenDrawer(drawer), cancellationToken);

    public async Task<PrinterStatusResponse> GetPrinterStatusAsync(CancellationToken cancellationToken = default)
        => new(await RequestAsync(Ncr7197Commands.PrinterStatus(), cancellationToken: cancellationToken).ConfigureAwait(false));

    public async Task<PrinterStatusResponse> GetDrawerStatusAsync(CancellationToken cancellationToken = default)
        => new(await RequestAsync(Ncr7197Commands.DrawerStatus(), cancellationToken: cancellationToken).ConfigureAwait(false));

    public async Task<PrinterStatusResponse> GetTransmitStatusAsync(byte statusId, CancellationToken cancellationToken = default)
        => new(await RequestAsync(Ncr7197Commands.TransmitStatus(statusId), cancellationToken: cancellationToken).ConfigureAwait(false));

    public async Task<PrinterStatusResponse> GetSoftwareVersionAsync(CancellationToken cancellationToken = default)
        => new(await RequestAsync(Ncr7197Commands.SoftwareVersion(), maxBytes: 64, cancellationToken: cancellationToken).ConfigureAwait(false));

    public ValueTask DisposeAsync()
    {
        _gate.Dispose();
        return _transport.DisposeAsync();
    }

    /// <summary>
    /// Reads one chunk with a hard time bound. <see cref="System.IO.Ports.SerialPort"/>'s
    /// <c>BaseStream.ReadAsync</c> does not honour a <see cref="CancellationToken"/> on
    /// every platform, so the wait is bounded explicitly instead of trusting the token.
    /// </summary>
    private async Task<int> ReadChunkAsync(Memory<byte> buffer, TimeSpan window, CancellationToken cancellationToken)
    {
        var readTask = _transport.ReadAsync(buffer, cancellationToken);

        return await readTask
            .AsTask()
            .WaitAsync(window, cancellationToken)
            .ConfigureAwait(false);
    }
}
