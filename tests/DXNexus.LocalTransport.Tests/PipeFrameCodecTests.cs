using System.Buffers.Binary;
using DXNexus.Contracts;
using DXNexus.LocalTransport;
using Xunit;

namespace DXNexus.LocalTransport.Tests;

public sealed class PipeFrameCodecTests
{
    [Fact]
    public async Task FrameRoundTripsWithExplicitProtocolAndNonce()
    {
        var nonce = new string('a', 48);
        var envelope = PipeEnvelope.Create("radio.snapshot", 12, nonce, new { frequencyHz = 92_300_000L });
        await using var stream = new MemoryStream();

        await PipeFrameCodec.WriteAsync(stream, envelope);
        stream.Position = 0;
        var decoded = await PipeFrameCodec.ReadAsync(stream);

        Assert.NotNull(decoded);
        Assert.Equal(Protocol.Version, decoded.Protocol);
        Assert.Equal("radio.snapshot", decoded.Type);
        Assert.Equal(12, decoded.Sequence);
        Assert.Equal(nonce, decoded.SessionNonce);
        Assert.Equal(92_300_000L, decoded.Payload.GetProperty("frequencyHz").GetInt64());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65537)]
    public async Task DeclaredInvalidLengthIsRejectedBeforeBodyAllocation(int length)
    {
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        await using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await PipeFrameCodec.ReadAsync(stream));
    }

    [Fact]
    public void PipeNameIsStableAndDoesNotExposeTheWindowsSid()
    {
        const string sid = "S-1-5-21-111111111-222222222-333333333-1001";

        var first = LocalPipeName.FromWindowsSid(sid);
        var second = LocalPipeName.FromWindowsSid(sid);

        Assert.Equal(first, second);
        Assert.StartsWith("DXNexus.SDRSharp.", first);
        Assert.DoesNotContain(sid, first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientAndServerCompleteHandshakeAndTransferMessage()
    {
        var pipeName = $"dxn-{Guid.NewGuid():N}"[..12];
        await using var server = new BridgePipeServer(pipeName);
        await using var client = new PluginBridgeClient(pipeName);
        var received = new TaskCompletionSource<PipeEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += (_, message) => received.TrySetResult(message);
        server.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await client.ConnectAsync(timeout.Token);
        await client.SendAsync("radio.snapshot", 7, new { frequencyHz = 101_700_000L }, timeout.Token);
        var message = await received.Task.WaitAsync(timeout.Token);

        Assert.Equal("radio.snapshot", message.Type);
        Assert.Equal(7, message.Sequence);
        Assert.Equal(101_700_000L, message.Payload.GetProperty("frequencyHz").GetInt64());
    }
}
