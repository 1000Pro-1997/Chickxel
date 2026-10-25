using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Xunit;

namespace Gpixel.Core.Tests;

/// <summary>열기 확장: GIF·APNG 프레임 합성, 프레임 길이, 영상 가져오기 계산·ffmpeg 인자·출력 읽기, 확장자 판단.</summary>
public class MediaImportTests
{
    private static readonly Color32 Red = new(255, 0, 0), Green = new(0, 255, 0), Blue = new(0, 0, 255), None = Color32.Transparent;

    // ───────────── GIF ─────────────

    private sealed record GifFrameSpec(int X, int Y, int W, int H, byte[] Indices, int Disposal, int DelayCs, int Transparent = -1, bool Interlace = false);

    /// <summary>합성 테스트용 GIF89a(전역 팔레트, 프레임마다 그래픽 제어 확장)를 만든다.</summary>
    private static byte[] BuildGif(int w, int h, Color32[] palette, params GifFrameSpec[] frames)
    {
        using var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write("GIF89a"u8);
        bw.Write((ushort)w);
        bw.Write((ushort)h);
        int bits = 1;
        while (1 << bits < palette.Length) bits++;
        bw.Write((byte)(0x80 | (bits - 1)));
        bw.Write((byte)0);
        bw.Write((byte)0);
        for (int i = 0; i < 1 << bits; i++)
        {
            var c = i < palette.Length ? palette[i] : default;
            bw.Write(c.R);
            bw.Write(c.G);
            bw.Write(c.B);
        }
        foreach (var f in frames)
        {
            bw.Write([0x21, 0xF9, 4]);
            bw.Write((byte)((f.Disposal << 2) | (f.Transparent >= 0 ? 1 : 0)));
            bw.Write((ushort)f.DelayCs);
            bw.Write((byte)Math.Max(0, f.Transparent));
            bw.Write((byte)0);
            bw.Write((byte)0x2C);
            bw.Write((ushort)f.X);
            bw.Write((ushort)f.Y);
            bw.Write((ushort)f.W);
            bw.Write((ushort)f.H);
            bw.Write((byte)(f.Interlace ? 0x40 : 0));
            var data = f.Indices;
            if (f.Interlace)
            {
                data = new byte[f.Indices.Length];
                int row = 0;
                foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
                    for (int y = start; y < f.H; y += step, row++)
                        Array.Copy(f.Indices, y * f.W, data, row * f.W, f.W);
            }
            int minCode = Math.Max(2, bits);
            bw.Write((byte)minCode);
            bw.Flush();
            GifWriter.LzwEncode(data, minCode, ms);
        }
        bw.Write((byte)0x3B);
        bw.Flush();
        return ms.ToArray();
    }

    private static byte[] Fill(int count, byte value) => Enumerable.Repeat(value, count).ToArray();

    [Fact]
    public void Gif_PartialFramesApplyDisposalAndTransparency()
    {
        Color32[] palette = [new(0, 0, 0), Red, Green, Blue];
        var gif = BuildGif(4, 4, palette,
            new GifFrameSpec(0, 0, 4, 4, Fill(16, 1), Disposal: 1, DelayCs: 5),
            // 가운데 2×2 초록(왼쪽 위 한 칸은 투명 색 0 → 아래 빨강이 보임), 그다음 배경으로(투명)
            new GifFrameSpec(1, 1, 2, 2, [0, 2, 2, 2], Disposal: 2, DelayCs: 0, Transparent: 0),
            // 왼쪽 위 파랑, 그다음 이전으로
            new GifFrameSpec(0, 0, 1, 1, [3], Disposal: 3, DelayCs: 20),
            // 오른쪽 아래 초록, 캔버스 밖으로 나간 부분은 잘림
            new GifFrameSpec(3, 3, 2, 2, [2, 2, 2, 2], Disposal: 1, DelayCs: 1));
        var anim = GifReader.Read(new MemoryStream(gif));

        Assert.Equal((4, 4, 4), (anim.Width, anim.Height, anim.FrameCount));
        Assert.Equal([50, 100, 200, 100], anim.DurationsMs);
        Assert.All(anim.Images[0].Pixels, p => Assert.Equal(Red, p));

        var f1 = anim.Images[1];
        Assert.Equal(Red, f1[1, 1]);
        Assert.Equal(Green, f1[2, 1]);
        Assert.Equal(Green, f1[2, 2]);
        Assert.Equal(Red, f1[0, 0]);

        var f2 = anim.Images[2];
        Assert.Equal(Blue, f2[0, 0]);
        Assert.Equal(None, f2[1, 1]);
        Assert.Equal(None, f2[2, 2]);
        Assert.Equal(Red, f2[3, 0]);

        var f3 = anim.Images[3];
        Assert.Equal(Red, f3[0, 0]); // 이전으로 되돌림
        Assert.Equal(None, f3[1, 2]); // 배경으로 지운 칸은 그대로 투명
        Assert.Equal(Green, f3[3, 3]);
        Assert.Equal(Red, f3[3, 2]);
    }

    [Fact]
    public void Gif_InterlacedFrameIsDeinterlaced()
    {
        Color32[] palette = [new(0, 0, 0), Red, Green, Blue];
        var idx = new byte[2 * 9];
        for (int y = 0; y < 9; y++) idx[y * 2] = idx[y * 2 + 1] = (byte)(y % 4);
        var anim = GifReader.Read(new MemoryStream(BuildGif(2, 9, palette, new GifFrameSpec(0, 0, 2, 9, idx, 0, 10, Interlace: true))));
        for (int y = 0; y < 9; y++) Assert.Equal(palette[y % 4], anim.Images[0][1, y]);
    }

    [Fact]
    public void Gif_LzwDecodesLongNoisyDataWrittenByWriter()
    {
        var rng = new Random(7);
        var data = new byte[64 * 128];
        for (int i = 0; i < data.Length; i++) data[i] = (byte)(rng.Next(3) == 0 ? rng.Next(256) : i / 300 % 256);
        using var ms = new MemoryStream();
        GifWriter.LzwEncode(data, 8, ms);
        // 하위 블록 풀기
        var raw = new MemoryStream();
        var bytes = ms.ToArray();
        for (int p = 0; bytes[p] != 0; p += bytes[p] + 1) raw.Write(bytes, p + 1, bytes[p]);
        var output = new byte[data.Length];
        Assert.Equal(data.Length, GifReader.LzwDecode(raw.ToArray(), 8, output));
        Assert.Equal(data, output);
    }

    [Fact]
    public void Gif_RoundTripsGifWriterOutputAndCountsFrames()
    {
        var a = new PixelImage(3, 2);
        a[0, 0] = Red;
        a[2, 1] = Blue;
        var b = new PixelImage(3, 2);
        b[1, 1] = Green;
        var path = Path.Combine(Path.GetTempPath(), $"gpixel-gifreader-{Guid.NewGuid():N}.gif");
        try
        {
            GifWriter.Save([new GifFrame(a, 120), new GifFrame(b, 40)], path, new GifOptions());
            var anim = GifReader.Load(path);
            Assert.Equal([120, 40], anim.DurationsMs);
            Assert.Equal(a.Pixels, anim.Images[0].Pixels);
            Assert.Equal(b.Pixels, anim.Images[1].Pixels); // 투명이 있으면 배경으로(2) — 앞 프레임이 남지 않음
            Assert.Equal((3, 2, 2), GifReader.Count(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Gif_TruncatedFileKeepsDecodedFramesAndMaxFramesStops()
    {
        Color32[] palette = [new(0, 0, 0), Red];
        var gif = BuildGif(2, 2, palette,
            new GifFrameSpec(0, 0, 2, 2, Fill(4, 1), 1, 10),
            new GifFrameSpec(0, 0, 2, 2, Fill(4, 0), 1, 10),
            new GifFrameSpec(0, 0, 2, 2, Fill(4, 1), 1, 10));
        Assert.Equal(2, GifReader.Read(new MemoryStream(gif), maxFrames: 2).FrameCount);
        var cut = gif[..^12];
        var anim = GifReader.Read(new MemoryStream(cut));
        Assert.InRange(anim.FrameCount, 2, 3);
        Assert.Throws<InvalidDataException>(() => GifReader.Read(new MemoryStream("GIF89a"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => GifReader.Read(new MemoryStream("not a gif at all"u8.ToArray())));
    }

    // ───────────── APNG ─────────────

    private sealed record ApngFrameSpec(int X, int Y, int W, int H, Color32[] Pixels, int DelayNum, int DelayDen, byte Dispose, byte Blend);

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(head, data.Length);
        Encoding.ASCII.GetBytes(type, head[4..]);
        s.Write(head);
        s.Write(data);
        s.Write(new byte[4]); // CRC(읽기는 보지 않음)
    }

    private static byte[] Zlib(int w, int h, Color32[] px)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            for (int y = 0; y < h; y++)
            {
                z.WriteByte(0);
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    z.Write([c.R, c.G, c.B, c.A]);
                }
            }
        return ms.ToArray();
    }

    private static byte[] BuildApng(int w, int h, Color32[]? defaultImage, params ApngFrameSpec[] frames)
    {
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), h);
        ihdr[8] = 8;
        ihdr[9] = 6;
        Chunk(ms, "IHDR", ihdr);
        var actl = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(actl, frames.Length);
        Chunk(ms, "acTL", actl);
        int seq = 0;
        if (defaultImage is not null) Chunk(ms, "IDAT", Zlib(w, h, defaultImage));
        for (int i = 0; i < frames.Length; i++)
        {
            var f = frames[i];
            var fctl = new byte[26];
            BinaryPrimitives.WriteInt32BigEndian(fctl, seq++);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(4), f.W);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(8), f.H);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(12), f.X);
            BinaryPrimitives.WriteInt32BigEndian(fctl.AsSpan(16), f.Y);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(20), (ushort)f.DelayNum);
            BinaryPrimitives.WriteUInt16BigEndian(fctl.AsSpan(22), (ushort)f.DelayDen);
            fctl[24] = f.Dispose;
            fctl[25] = f.Blend;
            Chunk(ms, "fcTL", fctl);
            var z = Zlib(f.W, f.H, f.Pixels);
            if (i == 0 && defaultImage is null) Chunk(ms, "IDAT", z);
            else
            {
                var fdat = new byte[z.Length + 4];
                BinaryPrimitives.WriteInt32BigEndian(fdat, seq++);
                z.CopyTo(fdat, 4);
                Chunk(ms, "fdAT", fdat);
            }
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    [Fact]
    public void Apng_DisposeAndBlendOpsComposite()
    {
        var halfBlue = new Color32(0, 0, 255, 128);
        var apng = BuildApng(4, 4, null,
            new ApngFrameSpec(0, 0, 4, 4, Enumerable.Repeat(Red, 16).ToArray(), 1, 20, Dispose: 0, Blend: 0),
            new ApngFrameSpec(1, 1, 2, 2, [halfBlue, halfBlue, halfBlue, None], 0, 0, Dispose: 1, Blend: 1),
            new ApngFrameSpec(0, 0, 1, 1, [None], 30, 1000, Dispose: 2, Blend: 0),
            new ApngFrameSpec(3, 3, 1, 1, [Green], 5, 1000, Dispose: 0, Blend: 0));
        var anim = ApngReader.Read(new MemoryStream(apng))!;
        Assert.Equal((4, 4, 4), (anim.Width, anim.Height, anim.FrameCount));
        Assert.Equal([50, 100, 30, 100], anim.DurationsMs); // 1/20s, 0/0 → 0 → 기본, 30ms, 5ms → 기본

        var f1 = anim.Images[1];
        var mixed = FrameCompositor.Over(Red, halfBlue);
        Assert.Equal(mixed, f1[1, 1]);
        Assert.Equal(Red, f1[2, 2]); // 투명 픽셀 Over → 아래 그대로
        Assert.Equal(255, mixed.A);
        Assert.InRange(mixed.B, 120, 135);

        var f2 = anim.Images[2];
        Assert.Equal(None, f2[0, 0]); // Source로 투명을 덮음
        Assert.Equal(None, f2[1, 1]); // 앞 프레임의 배경으로
        Assert.Equal(Red, f2[3, 0]);

        var f3 = anim.Images[3];
        Assert.Equal(Red, f3[0, 0]); // 이전으로
        Assert.Equal(Green, f3[3, 3]);
    }

    [Fact]
    public void Apng_HiddenDefaultImageIsSkippedAndPlainPngIsNotAnimated()
    {
        var apng = BuildApng(2, 1, [Blue, Blue], new ApngFrameSpec(0, 0, 2, 1, [Red, Green], 1, 10, 0, 0));
        var anim = ApngReader.Read(new MemoryStream(apng))!;
        Assert.Equal(1, anim.FrameCount);
        Assert.Equal([Red, Green], anim.Images[0].Pixels);

        using var plain = new MemoryStream();
        PngIO.Save(new PixelImage(2, 2), plain);
        plain.Position = 0;
        Assert.Null(ApngReader.Read(plain));

        var path = Path.Combine(Path.GetTempPath(), $"gpixel-apng-{Guid.NewGuid():N}.png");
        try
        {
            File.WriteAllBytes(path, apng);
            Assert.Equal(1, ApngReader.CountFrames(path));
            File.WriteAllBytes(path, plain.ToArray());
            Assert.Equal(0, ApngReader.CountFrames(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ───────────── 프레임 길이·프로젝트 ─────────────

    [Theory]
    [InlineData(0, 100)]
    [InlineData(10, 100)]
    [InlineData(11, 11)]
    [InlineData(70, 70)]
    [InlineData(5000, 5000)]
    public void Durations_FromFileFollowBrowsers(int ms, int expected) => Assert.Equal(expected, FrameDurations.FromFile(ms));

    [Theory]
    [InlineData(12, 83)]
    [InlineData(24, 42)]
    [InlineData(30, 33)]
    [InlineData(200, 10)]
    [InlineData(0.5, 2000)]
    public void Durations_FromFps(double fps, int expected) => Assert.Equal(expected, FrameDurations.FromFps(fps));

    [Fact]
    public void Project_FromFramesMakesOneLayerWithTimedFrames()
    {
        var a = new PixelImage(3, 2);
        a[1, 1] = Red;
        var p = SpriteProject.FromFrames([a, new PixelImage(3, 2)], [40, 250], "walk");
        Assert.Equal((3, 2), (p.Width, p.Height));
        Assert.Single(p.Layers);
        Assert.Equal("walk", p.Layers[0].Name);
        Assert.Equal([40, 250], p.Frames.Select(f => f.DurationMs));
        Assert.Throws<ArgumentException>(() => SpriteProject.FromFrames([a, new PixelImage(2, 2)], [1, 1]));
    }

    // ───────────── 영상 ─────────────

    private const string Mp4Probe = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'clip.mp4':
          Metadata:
            major_brand     : isom
          Duration: 00:00:20.13, start: 0.000000, bitrate: 353 kb/s
          Stream #0:0[0x1](und): Video: h264 (High) (avc1 / 0x31637661), yuv420p(progressive), 1280x688, 350 kb/s, 24 fps, 24 tbr, 12288 tbn (default)
            Metadata:
              handler_name    : VideoHandler
          Stream #0:1[0x2](und): Audio: aac (LC) (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 128 kb/s (default)
        At least one output file must be specified
        """;

    private const string AvifProbe = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'anim.avif':
          Duration: 00:00:01.00, start: 0.000000, bitrate: 14 kb/s
          Stream #0:0[0x1]: Video: av1 (libdav1d) (High) (av01 / 0x31307661), gbrp(pc, gbr/unknown/unknown), 64x48 [SAR 1:1 DAR 4:3], 1 fps, 1 tbr, 1 tbn (default)
          Stream #0:1[0x1](und): Video: av1 (libdav1d) (High) (av01 / 0x31307661), gbrp(pc, gbr/bt709/iec61966-2-1, progressive), 64x48 [SAR 1:1 DAR 4:3], 5 kb/s, 5 fps, 5 tbr, 10240 tbn (default)
        """;

    private const string StillProbe = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'still.avif':
          Duration: N/A, start: 0.000000, bitrate: N/A
          Stream #0:0[0x1]: Video: av1 (libdav1d) (High) (av01 / 0x31307661), gbrp(pc, gbr/unknown/unknown), 64x48 [SAR 1:1 DAR 4:3], 1 fps, 1 tbr, 1 tbn (default)
        """;

    private const string WebmAlphaRotatedProbe = """
        Input #0, matroska,webm, from 'a.webm':
          Duration: 00:00:02.50, start: 0.000000, bitrate: 200 kb/s
          Stream #0:0: Video: vp9 (Profile 0), yuv420p(tv, progressive), 640x360, SAR 1:1 DAR 16:9, 29.97 fps, 29.97 tbr, 1k tbn (default)
            Metadata:
              alpha_mode      : 1
            Side data:
              displaymatrix: rotation of -90.00 degrees
        """;

    [Fact]
    public void Probe_ParsesDurationSizeFpsAndPicksStream()
    {
        var mp4 = Ffmpeg.ParseProbe(Mp4Probe)!;
        Assert.Equal(20.13, mp4.DurationSec!.Value, 3);
        Assert.Equal(new VideoStreamInfo(0, 1280, 688, 24, "h264", false), mp4.Best);
        Assert.False(mp4.IsStill);

        var avif = Ffmpeg.ParseProbe(AvifProbe)!;
        Assert.Equal(2, avif.Streams.Count);
        Assert.Equal(1, avif.Best.Index); // 같은 크기면 재생 트랙(5 fps)

        Assert.True(Ffmpeg.ParseProbe(StillProbe)!.IsStill);

        var webm = Ffmpeg.ParseProbe(WebmAlphaRotatedProbe)!;
        Assert.Equal((360, 640, 29.97, true, "vp9"), (webm.Best.Width, webm.Best.Height, webm.Best.Fps, webm.Best.Alpha, webm.Best.Codec));

        Assert.Null(Ffmpeg.ParseProbe("x.txt: Invalid data found when processing input"));
    }

    [Fact]
    public void Plan_DefaultsCapFpsAndShrinkBigVideos()
    {
        var mp4 = Ffmpeg.ParseProbe(Mp4Probe)!;
        var d = VideoImportPlan.Defaults(mp4);
        Assert.Equal(12, d.Fps);
        Assert.Equal(120, d.MaxFrames);
        Assert.Equal(VideoScaleMode.FitLongSide, d.Scale);
        Assert.Equal((256, 138), VideoImportPlan.OutputSize(mp4.Best, d));
        Assert.Equal(120, VideoImportPlan.EstimateFrames(mp4, d));
        Assert.Equal(83, VideoImportPlan.FrameDurationMs(d));

        var small = Ffmpeg.ParseProbe(AvifProbe)!;
        var ds = VideoImportPlan.Defaults(small);
        Assert.Equal((5, VideoScaleMode.Keep), (ds.Fps, ds.Scale));
        Assert.Equal(5, VideoImportPlan.EstimateFrames(small, ds));
    }

    [Fact]
    public void Plan_SegmentMathClampsToVideo()
    {
        var mp4 = Ffmpeg.ParseProbe(Mp4Probe)!;
        var o = new VideoImportOptions { StartSec = 2, EndSec = 4.5, Fps = 10, MaxFrames = 1000 };
        Assert.Equal(2.5, VideoImportPlan.SegmentSec(mp4, o)!.Value, 6);
        Assert.Equal(25, VideoImportPlan.EstimateFrames(mp4, o));
        Assert.Equal(242, VideoImportPlan.EstimateFrames(mp4, o with { StartSec = 0, EndSec = null, Fps = 12 }));
        Assert.Equal(1, VideoImportPlan.EstimateFrames(mp4, o with { StartSec = 30, EndSec = 40 })); // 영상 밖 → 최소 1
        Assert.Equal(1, VideoImportPlan.EstimateFrames(Ffmpeg.ParseProbe(StillProbe)!, o));
        Assert.Equal(25L * 1280 * 688 * 4, VideoImportPlan.EstimateBytes(mp4, o));
        Assert.Equal((688, 1280), VideoImportPlan.OutputSize(new VideoStreamInfo(0, 344, 640, 30, "h264", false),
            o with { Scale = VideoScaleMode.FitLongSide, LongSide = 1280 }));
    }

    [Fact]
    public void Args_DecodeRawRgbaWithSegmentFpsScaleAndLimit()
    {
        var mp4 = Ffmpeg.ParseProbe(Mp4Probe)!;
        var o = new VideoImportOptions { StartSec = 1.5, EndSec = 3, Fps = 12, MaxFrames = 50, Scale = VideoScaleMode.FitLongSide, LongSide = 128, Smooth = false };
        Assert.Equal(
            ["-hide_banner", "-nostdin", "-loglevel", "error", "-ss", "1.5", "-i", "clip.mp4", "-t", "1.5", "-map", "0:0", "-an", "-sn", "-dn",
             "-vf", "fps=12,scale=128:69:flags=neighbor", "-frames:v", "50", "-f", "rawvideo", "-pix_fmt", "rgba", "-"],
            Ffmpeg.DecodeArgs("clip.mp4", mp4, o));

        var keep = Ffmpeg.DecodeArgs("clip.mp4", mp4, new VideoImportOptions { Fps = 23.976, Scale = VideoScaleMode.Keep });
        Assert.DoesNotContain("-ss", keep);
        Assert.DoesNotContain("-t", keep);
        Assert.Contains("fps=23.976", keep);

        var webm = Ffmpeg.DecodeArgs("a.webm", Ffmpeg.ParseProbe(WebmAlphaRotatedProbe)!, new VideoImportOptions());
        Assert.Equal(["-c:v", "libvpx-vp9"], webm.Skip(4).Take(2));

        var still = Ffmpeg.DecodeArgs("s.avif", Ffmpeg.ParseProbe(StillProbe)!, new VideoImportOptions { StartSec = 3 });
        Assert.DoesNotContain(still, a => a.StartsWith("fps=", StringComparison.Ordinal));
        Assert.DoesNotContain("-ss", still);
        Assert.Equal("1", still[still.ToList().IndexOf("-frames:v") + 1]);
        Assert.Equal(["-hide_banner", "-nostdin", "-i", "x.mp4"], Ffmpeg.ProbeArgs("x.mp4"));
    }

    [Fact]
    public void Ffmpeg_CommonLocationsPerPlatform()
    {
        var win = Ffmpeg.CommonLocations(true, @"C:\Users\me", @"C:\Users\me\AppData\Local", @"C:\Program Files", @"C:\ProgramData", [@"D:\pkg\ffmpeg-8"]).ToList();
        Assert.Contains(Path.Combine(@"C:\Users\me\AppData\Local", "Microsoft", "WinGet", "Links", "ffmpeg.exe"), win);
        Assert.Contains(Path.Combine(@"D:\pkg\ffmpeg-8", "bin", "ffmpeg.exe"), win);
        Assert.Contains(@"C:\ffmpeg\bin\ffmpeg.exe", win);
        var mac = Ffmpeg.CommonLocations(false, "/Users/me", "", "", "", []).ToList();
        Assert.Contains("/opt/homebrew/bin/ffmpeg", mac);
    }

    // ───────────── 확장자 ─────────────

    [Theory]
    [InlineData("a.gpixel", MediaKind.Project)]
    [InlineData("a.PNG", MediaKind.Image)]
    [InlineData("a.jpeg", MediaKind.Image)]
    [InlineData("a.gif", MediaKind.Image)]
    [InlineData("a.webp", MediaKind.Image)]
    [InlineData("a.heic", MediaKind.Image)]
    [InlineData("a.MP4", MediaKind.Video)]
    [InlineData("a.mkv", MediaKind.Video)]
    [InlineData("a.txt", MediaKind.None)]
    [InlineData("noext", MediaKind.None)]
    public void Formats_KindOf(string path, MediaKind kind) => Assert.Equal(kind, MediaFormats.KindOf(path));

    [Fact]
    public void Formats_FfmpegNeedAndPatterns()
    {
        Assert.True(MediaFormats.NeedsFfmpeg("x.webm"));
        Assert.True(MediaFormats.NeedsFfmpeg("x.avif"));
        Assert.False(MediaFormats.NeedsFfmpeg("x.gif"));
        Assert.True(MediaFormats.IsRaster("x.ico"));
        Assert.Equal(["*.gif", "*.png"], MediaFormats.Patterns([".gif", ".png", ".gif"]));
        Assert.Contains(".gpixel", MediaFormats.AllExtensions);
        Assert.Contains(".mov", MediaFormats.AllExtensions);
    }
}
