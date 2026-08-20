using System.Buffers.Binary;
using System.Text.Json;
using DXNexus.Contracts;

namespace DXNexus.LocalTransport;

public static class PipeFrameCodec
{
    public static async ValueTask WriteAsync(Stream stream, PipeEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(envelope);
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, PipeJson.Options);
        if (body.Length is <= 0 or > Protocol.MaximumPipeMessageBytes)
        {
            throw new InvalidDataException($"Pipe message size {body.Length} is outside the permitted range.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<PipeEnvelope?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        if (!await ReadExactlyOrEofAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var bodyLength = BinaryPrimitives.ReadInt32BigEndian(header);
        if (bodyLength is <= 0 or > Protocol.MaximumPipeMessageBytes)
        {
            throw new InvalidDataException($"Pipe message declares invalid size {bodyLength}.");
        }

        var body = GC.AllocateUninitializedArray<byte>(bodyLength);
        if (!await ReadExactlyOrEofAsync(stream, body, cancellationToken).ConfigureAwait(false))
        {
            throw new EndOfStreamException("Pipe closed during a message body.");
        }

        return JsonSerializer.Deserialize<PipeEnvelope>(body, PipeJson.Options)
            ?? throw new InvalidDataException("Pipe message contained JSON null.");
    }

    private static async ValueTask<bool> ReadExactlyOrEofAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return offset == 0 ? false : throw new EndOfStreamException("Pipe closed during a frame.");
            }

            offset += read;
        }

        return true;
    }
}

