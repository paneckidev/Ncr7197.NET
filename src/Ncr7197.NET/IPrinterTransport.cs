namespace Ncr7197;

/// <summary>Low-level byte transport used by the NCR 7197 client.</summary>
public interface IPrinterTransport : IAsyncDisposable
{
    ValueTask OpenAsync(CancellationToken cancellationToken = default);
    ValueTask CloseAsync(CancellationToken cancellationToken = default);
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
    bool IsOpen { get; }
}
