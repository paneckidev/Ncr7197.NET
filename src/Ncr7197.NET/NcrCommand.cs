namespace Ncr7197;

public readonly record struct NcrCommand(ReadOnlyMemory<byte> Bytes)
{
    public static NcrCommand From(params byte[] bytes) => new(bytes);
    public static NcrCommand Text(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());

    public static NcrCommand Concat(params NcrCommand[] commands)
    {
        var length = commands.Sum(c => c.Bytes.Length);
        var result = new byte[length];
        var offset = 0;
        foreach (var command in commands)
        {
            command.Bytes.Span.CopyTo(result.AsSpan(offset));
            offset += command.Bytes.Length;
        }
        return new(result);
    }
}
