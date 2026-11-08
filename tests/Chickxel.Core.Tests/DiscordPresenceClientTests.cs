using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.Community;

namespace Chickxel.Core.Tests;

public sealed class DiscordPresenceClientTests
{
    /// <summary>Discord 쪽 끝: 앱이 보낸 틀을 읽고 답을 쓴다.</summary>
    private sealed class FakeDiscord(Stream toApp, Stream fromApp)
    {
        public async Task<(int Op, JsonObject? Json)> ReadAsync()
        {
            var header = new byte[8];
            await fromApp.ReadExactlyAsync(header);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            var body = new byte[length];
            await fromApp.ReadExactlyAsync(body);
            return (BinaryPrimitives.ReadInt32LittleEndian(header), JsonNode.Parse(body) as JsonObject);
        }

        public async Task WriteAsync(int op, string json)
        {
            var body = Encoding.UTF8.GetBytes(json);
            var frame = new byte[8 + body.Length];
            BinaryPrimitives.WriteInt32LittleEndian(frame, op);
            BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4), body.Length);
            body.CopyTo(frame, 8);
            await toApp.WriteAsync(frame);
            await toApp.FlushAsync();
        }
    }

    /// <summary>읽기·쓰기를 서로 다른 파이프로 잇는 앱 쪽 통로.</summary>
    private sealed class Duplex(Stream read, Stream write) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => read.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => read.ReadAsync(buffer, cancellationToken);
        public override void Write(byte[] buffer, int offset, int count) => write.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => write.WriteAsync(buffer, cancellationToken);
        public override void Flush() => write.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => write.FlushAsync(cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static (Stream App, FakeDiscord Discord) Connect()
    {
        var toApp = new Pipe();
        var fromApp = new Pipe();
        return (new Duplex(toApp.Reader.AsStream(), fromApp.Writer.AsStream()), new FakeDiscord(toApp.Writer.AsStream(), fromApp.Reader.AsStream()));
    }

    [Fact]
    public async Task Handshake_SendsTheAppId_AndWaitsForReady_ThenSetActivitySendsTheActivity_AnswersPings()
    {
        var (app, discord) = Connect();
        var connecting = DiscordPresenceClient.HandshakeAsync(app, "1568875254703587348", 4242, CancellationToken.None);
        var (op, hello) = await discord.ReadAsync();
        Assert.Equal((0, "1568875254703587348", 1), (op, hello!["client_id"]!.GetValue<string>(), hello["v"]!.GetValue<int>()));
        await discord.WriteAsync(1, """{ "cmd": "DISPATCH", "evt": "READY", "data": { "v": 1 } }""");
        await using var client = await connecting;
        Assert.NotNull(client);

        var start = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var setting = client!.SetActivityAsync(new DiscordActivity(start, "https://chickxel.com/static/chickxel.png", "Chickxel 1.0"));
        var (setOp, set) = await discord.ReadAsync();
        Assert.Equal((1, "SET_ACTIVITY", 4242), (setOp, set!["cmd"]!.GetValue<string>(), set["args"]!["pid"]!.GetValue<int>()));
        var sent = set["args"]!["activity"]!.AsObject();
        // Only "playing" and the elapsed time with the picture: no text lines or buttons.
        Assert.Equal(["timestamps", "assets"], sent.Select(p => p.Key));
        Assert.Equal(1_800_000_000L, sent["timestamps"]!["start"]!.GetValue<long>());
        Assert.Equal(("https://chickxel.com/static/chickxel.png", "Chickxel 1.0"), (sent["assets"]!["large_image"]!.GetValue<string>(), sent["assets"]!["large_text"]!.GetValue<string>()));

        // A ping before the answer gets a pong with the same body; the answer is matched by its nonce.
        await discord.WriteAsync(3, """{ "n": 7 }""");
        var (pongOp, pong) = await discord.ReadAsync();
        Assert.Equal((4, 7), (pongOp, pong!["n"]!.GetValue<int>()));
        await discord.WriteAsync(1, """{ "cmd": "SET_ACTIVITY", "nonce": "other", "evt": null, "data": {} }""");
        Assert.False(setting.IsCompleted);
        await discord.WriteAsync(1, $$"""{ "cmd": "SET_ACTIVITY", "nonce": "{{set["nonce"]!.GetValue<string>()}}", "evt": null, "data": {} }""");
        await setting;

        // null clears: the args carry no activity. A refusal is an IOException.
        var clearing = client.SetActivityAsync(null);
        var (_, clear) = await discord.ReadAsync();
        Assert.False(clear!["args"]!.AsObject().ContainsKey("activity"));
        await discord.WriteAsync(1, $$"""{ "cmd": "SET_ACTIVITY", "nonce": "{{clear["nonce"]!.GetValue<string>()}}", "evt": "ERROR", "data": { "code": 4000, "message": "bad" } }""");
        await Assert.ThrowsAsync<IOException>(() => clearing);
    }

    [Fact]
    public async Task Handshake_ClosedByDiscord_IsNull()
    {
        var (app, discord) = Connect();
        var connecting = DiscordPresenceClient.HandshakeAsync(app, "123", 1, CancellationToken.None);
        await discord.ReadAsync();
        await discord.WriteAsync(2, """{ "code": 4000, "message": "Invalid Client ID" }""");
        Assert.Null(await connecting);
    }

    [Fact]
    public void Activity_PictureText_IsKeptWithinDiscordLimits()
    {
        var start = DateTimeOffset.FromUnixTimeSeconds(1);
        var text = new DiscordActivity(start, LargeText: new string('a', 200)).ToJson()["assets"]!["large_text"]!.GetValue<string>();
        Assert.Equal(128, text.Length);
        Assert.EndsWith("…", text);
        // Shorter than Discord's 2-character minimum, or no picture at all: no assets.
        Assert.False(new DiscordActivity(start, LargeText: "x").ToJson().ContainsKey("assets"));
    }
}
