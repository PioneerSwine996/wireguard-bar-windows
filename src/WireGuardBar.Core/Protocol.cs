using System.Buffers.Binary;
using System.Text.Json;

namespace WireGuardBar.Core;

/// One request and one response per pipe connection, each a length-prefixed JSON frame.
public sealed record HelperRequest(string Command, string? Name = null, string? Argument = null, string? Input = null);

public sealed record HelperResponse(bool Ok, string Output = "", string? Error = null);

public static class Protocol
{
    public const string PipeName = "wgbar-helper";
    /// Bumped whenever the helper's commands change; the app asks for setup when the installed one differs.
    public const int HelperVersion = 1;
    public const int MaxFrameBytes = 256 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        if (body.Length > MaxFrameBytes) throw new InvalidDataException("message too large");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaxFrameBytes) throw new InvalidDataException("bad frame length");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        return JsonSerializer.Deserialize<T>(body) ?? throw new InvalidDataException("empty message");
    }
}
