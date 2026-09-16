namespace Ncr7197;

/// <summary>Raw response returned by an NCR status command.</summary>
public readonly record struct PrinterStatusResponse(byte[] Data)
{
    public bool HasData => Data is { Length: > 0 };
    public byte PrimaryByte => HasData ? Data[0] : (byte)0;

    public override string ToString() => Convert.ToHexString(Data);
}

/// <summary>
/// Represents a status request without pretending that firmware-specific status bits
/// have a universal meaning. Use the raw bytes for the exact 7197 firmware/configuration.
/// </summary>
public enum PrinterStatusRequest
{
    Printer,
    Drawer,
    Transmit,
    SoftwareVersion
}
