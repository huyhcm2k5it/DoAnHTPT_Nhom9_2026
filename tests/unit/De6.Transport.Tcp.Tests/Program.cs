using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using De6.Transport.Tcp;
using De6.Transport.Tcp.Tests;

// No test framework/packages: this executable returns nonzero when any assertion fails (same style as the other harnesses).
// Breaks caught (V12 T31): frames split/merged wrongly, EOF or bad length not closing the channel, oversize allocation,
// interleaved concurrent writers, and a channel that keeps being used after its byte stream lost frame alignment.
var failures = 0;
var count = 0;

byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
byte[] Frame(byte[] payload) => FrameCodec.Encode(payload);
byte[] Header(uint length) { var h = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(h, length); return h; }
byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
string Text(ReadOnlyMemory<byte> bytes) => Encoding.UTF8.GetString(bytes.Span);
async Task<FrameError?> ProtocolErrorAsync(Func<Task> action)
{
    try { await action(); return null; }
    catch (FrameProtocolException exception) { return exception.Error; }
}
async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try { await action(); return false; }
    catch (T) { return true; }
}

// ---- Codec ----
await Check("encode: golden bytes for {} are 00 00 00 02 7B 7D", () =>
    Task.FromResult(FrameCodec.Encode(Utf8("{}")).SequenceEqual(new byte[] { 0, 0, 0, 2, 0x7B, 0x7D })));
await Check("encode: length prefix is big-endian (256 bytes -> 00 00 01 00)", () =>
    Task.FromResult(FrameCodec.Encode(new byte[256].Select(_ => (byte)'a').ToArray()).Take(4).SequenceEqual(new byte[] { 0, 0, 1, 0 })));
await Check("encode: rejects empty payload", () => ThrowsAsync<ArgumentException>(() => { FrameCodec.Encode(ReadOnlySpan<byte>.Empty); return Task.CompletedTask; }));
await Check("encode: accepts exactly 1 MiB", () =>
    Task.FromResult(FrameCodec.Encode(new byte[FrameCodec.MaxPayloadLength].Select(_ => (byte)'a').ToArray()).Length == FrameCodec.MaxPayloadLength + 4));
await Check("encode: rejects 1 MiB + 1", () =>
    ThrowsAsync<ArgumentException>(() => { FrameCodec.Encode(new byte[FrameCodec.MaxPayloadLength + 1]); return Task.CompletedTask; }));
await Check("encode: rejects invalid UTF-8", () =>
    ThrowsAsync<ArgumentException>(() => { FrameCodec.Encode(new byte[] { 0xC3, 0x28 }); return Task.CompletedTask; }));

// ---- Reading ----
await Check("read: one frame round-trips (Vietnamese UTF-8)", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Frame(Utf8("{\"ghe\":\"Đặt vé\"}"))));
    return Text(await channel.ReadFrameAsync(default)) == "{\"ghe\":\"Đặt vé\"}";
});
await Check("read: frame arriving one byte at a time", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Frame(Utf8("{\"a\":1}")), chunk: 1));
    return Text(await channel.ReadFrameAsync(default)) == "{\"a\":1}";
});
await Check("read: header split across reads (1 + 3 bytes)", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Frame(Utf8("{\"a\":1}")), chunk: 3));
    return Text(await channel.ReadFrameAsync(default)) == "{\"a\":1}";
});
await Check("read: three frames coalesced in one receive", async () =>
{
    var input = Concat(Frame(Utf8("{\"n\":1}")), Frame(Utf8("{\"n\":2}")), Frame(Utf8("{\"n\":3}")));
    await using var channel = new StreamFrameChannel(new ScriptedStream(input));
    var a = Text(await channel.ReadFrameAsync(default));
    var b = Text(await channel.ReadFrameAsync(default));
    var c = Text(await channel.ReadFrameAsync(default));
    return a == "{\"n\":1}" && b == "{\"n\":2}" && c == "{\"n\":3}";
});
await Check("read: clean EOF on a frame boundary -> FrameChannelClosedException", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Frame(Utf8("{}"))));
    await channel.ReadFrameAsync(default);
    return await ThrowsAsync<FrameChannelClosedException>(async () => await channel.ReadFrameAsync(default));
});
await Check("read: EOF inside header -> TruncatedHeader", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(new byte[] { 0, 0 }));
    return await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default)) == FrameError.TruncatedHeader;
});
await Check("read: EOF inside body -> TruncatedBody", async () =>
{
    var input = Concat(Header(10), Utf8("{\"a\""));
    await using var channel = new StreamFrameChannel(new ScriptedStream(input));
    return await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default)) == FrameError.TruncatedBody;
});
await Check("read: length 0 -> InvalidLength", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Header(0)));
    return await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default)) == FrameError.InvalidLength;
});
await Check("read: length 1 MiB + 1 -> InvalidLength", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Header((uint)FrameCodec.MaxPayloadLength + 1)));
    return await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default)) == FrameError.InvalidLength;
});
await Check("read: length 0xFFFFFFFF -> InvalidLength without allocating 4 GiB", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Header(uint.MaxValue)));
    return await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default)) == FrameError.InvalidLength;
});
await Check("read: invalid UTF-8 body -> InvalidUtf8", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Concat(Header(2), new byte[] { 0xC3, 0x28 })));
    return await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default)) == FrameError.InvalidUtf8;
});
await Check("read: after a protocol error the channel is closed and refuses further use", async () =>
{
    var stream = new ScriptedStream(Concat(Header(0), Frame(Utf8("{}"))));
    var channel = new StreamFrameChannel(stream);
    await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default));
    var again = await ProtocolErrorAsync(async () => await channel.ReadFrameAsync(default));
    var write = await ProtocolErrorAsync(async () => await channel.WriteFrameAsync(Utf8("{}"), default));
    return channel.IsFaulted && stream.Disposed && again == FrameError.ChannelFaulted && write == FrameError.ChannelFaulted;
});
await Check("read: cancellation mid-read closes the channel (alignment unknown)", async () =>
{
    using var cts = new CancellationTokenSource();
    var channel = new StreamFrameChannel(new BlockingReadStream());
    var pending = channel.ReadFrameAsync(cts.Token).AsTask();
    cts.Cancel();
    var cancelled = await ThrowsAsync<OperationCanceledException>(async () => await pending);
    return cancelled && channel.IsFaulted;
});
await Check("read: two concurrent readers each get a whole, distinct frame", async () =>
{
    await using var channel = new StreamFrameChannel(new ScriptedStream(Concat(Frame(Utf8("{\"n\":1}")), Frame(Utf8("{\"n\":2}"))), chunk: 2));
    var first = channel.ReadFrameAsync(default).AsTask();
    var second = channel.ReadFrameAsync(default).AsTask();
    var results = (await Task.WhenAll(first, second)).Select(Text).Order().ToArray();
    return results.SequenceEqual(new[] { "{\"n\":1}", "{\"n\":2}" });
});

// ---- Writing ----
await Check("write: stream receives header + payload exactly", async () =>
{
    var stream = new ScriptedStream(Array.Empty<byte>());
    await using var channel = new StreamFrameChannel(stream);
    await channel.WriteFrameAsync(Utf8("{}"), default);
    return stream.Written.ToArray().SequenceEqual(new byte[] { 0, 0, 0, 2, 0x7B, 0x7D });
});
await Check("write: invalid payload is rejected without faulting the channel", async () =>
{
    var stream = new ScriptedStream(Array.Empty<byte>());
    await using var channel = new StreamFrameChannel(stream);
    var rejected = await ThrowsAsync<ArgumentException>(async () => await channel.WriteFrameAsync(ReadOnlyMemory<byte>.Empty, default));
    await channel.WriteFrameAsync(Utf8("{}"), default);
    return rejected && !channel.IsFaulted && stream.Written.Length == 6;
});
await Check("write: 40 concurrent writers never interleave frames", async () =>
{
    var stream = new ChunkingWriteStream();
    await using var channel = new StreamFrameChannel(stream);
    var writers = Enumerable.Range(0, 40).Select(i => channel.WriteFrameAsync(Utf8("{\"w\":\"" + new string((char)('a' + i % 26), 3000 + i) + "\"}"), default).AsTask());
    await Task.WhenAll(writers);
    var bytes = stream.Written.ToArray();
    var offset = 0;
    var frames = 0;
    while (offset < bytes.Length)
    {
        var length = FrameCodec.ParseLength(bytes.AsSpan(offset, 4));
        var body = Encoding.UTF8.GetString(bytes, offset + 4, length);
        var letters = body[6..^2];
        if (letters.Distinct().Count() != 1) return false;
        offset += 4 + length;
        frames++;
    }
    return frames == 40 && offset == bytes.Length;
});
await Check("write: cancelled while waiting for the writer turn leaves the channel healthy", async () =>
{
    var stream = new GatedWriteStream();
    await using var channel = new StreamFrameChannel(stream);
    var first = channel.WriteFrameAsync(Utf8("{\"n\":1}"), default).AsTask();
    await stream.WriteEntered;
    using var cts = new CancellationTokenSource();
    var second = channel.WriteFrameAsync(Utf8("{\"n\":2}"), cts.Token).AsTask();
    cts.Cancel();
    var cancelled = await ThrowsAsync<OperationCanceledException>(async () => await second);
    stream.Release();
    await first;
    return cancelled && !channel.IsFaulted && stream.WriteCalls == 1;
});
await Check("write: I/O failure faults and closes the channel", async () =>
{
    var stream = new GatedWriteStream { FailWrites = true };
    var channel = new StreamFrameChannel(stream);
    stream.Release();
    var failed = await ThrowsAsync<IOException>(async () => await channel.WriteFrameAsync(Utf8("{}"), default));
    var refused = await ProtocolErrorAsync(async () => await channel.WriteFrameAsync(Utf8("{}"), default));
    return failed && channel.IsFaulted && stream.Disposed && refused == FrameError.ChannelFaulted;
});

// ---- Lifetime ----
await Check("dispose: idempotent, closes stream, later use throws ObjectDisposedException", async () =>
{
    var stream = new ScriptedStream(Array.Empty<byte>());
    var channel = new StreamFrameChannel(stream);
    await channel.DisposeAsync();
    await channel.DisposeAsync();
    return stream.Disposed
        && await ThrowsAsync<ObjectDisposedException>(async () => await channel.ReadFrameAsync(default))
        && await ThrowsAsync<ObjectDisposedException>(async () => await channel.WriteFrameAsync(Utf8("{}"), default));
});
await Check("dispose: leaveOpen keeps the stream usable by the caller", async () =>
{
    var stream = new ScriptedStream(Array.Empty<byte>());
    await new StreamFrameChannel(stream, leaveOpen: true).DisposeAsync();
    return !stream.Disposed;
});
await Check("ctor: rejects null and non-duplex streams", async () =>
{
    var nullRejected = await ThrowsAsync<ArgumentNullException>(() => { _ = new StreamFrameChannel(null!); return Task.CompletedTask; });
    var readOnlyRejected = await ThrowsAsync<ArgumentException>(() => { _ = new StreamFrameChannel(new MemoryStream(new byte[1], writable: false)); return Task.CompletedTask; });
    return nullRejected && readOnlyRejected;
});

// ---- Real TCP on loopback ----
await Check("tcp loopback: mixed sizes up to 1 MiB, both directions, order kept", async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    using var client = new TcpClient();
    var accept = listener.AcceptTcpClientAsync();
    await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
    using var server = await accept;
    await using var clientChannel = new StreamFrameChannel(client.GetStream());
    await using var serverChannel = new StreamFrameChannel(server.GetStream());
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var sizes = new[] { 2, 5, 100, 70_000, FrameCodec.MaxPayloadLength, 9 };
    byte[] Payload(int size) => size < 8 ? Utf8(new string('a', size)) : Utf8("{\"p\":\"" + new string('x', size - 8) + "\"}");
    var sender = Task.Run(async () =>
    {
        foreach (var size in sizes) await clientChannel.WriteFrameAsync(Payload(size), timeout.Token);
    });
    foreach (var size in sizes)
    {
        var got = await serverChannel.ReadFrameAsync(timeout.Token);
        if (got.Length != size) return false;
    }
    await sender;
    await serverChannel.WriteFrameAsync(Utf8("{\"ok\":true}"), timeout.Token);
    return Text(await clientChannel.ReadFrameAsync(timeout.Token)) == "{\"ok\":true}";
});
await Check("tcp loopback: peer closing between frames -> FrameChannelClosedException", async () =>
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    using var client = new TcpClient();
    var accept = listener.AcceptTcpClientAsync();
    await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
    var server = await accept;
    await using var clientChannel = new StreamFrameChannel(client.GetStream());
    await using (var serverChannel = new StreamFrameChannel(server.GetStream()))
        await serverChannel.WriteFrameAsync(Utf8("{}"), default);
    server.Dispose();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await clientChannel.ReadFrameAsync(timeout.Token);
    return await ThrowsAsync<FrameChannelClosedException>(async () => await clientChannel.ReadFrameAsync(timeout.Token));
});

Console.WriteLine($"{count - failures}/{count} assertions passed.");
return failures == 0 ? 0 : 1;

async Task Check(string name, Func<Task<bool>> assertion)
{
    count++;
    try
    {
        if (!await assertion()) throw new InvalidOperationException("assertion returned false");
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.GetType().Name}: {exception.Message}");
    }
}
