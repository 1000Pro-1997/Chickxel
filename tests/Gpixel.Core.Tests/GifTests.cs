using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Xunit;

namespace Gpixel.Core.Tests;

public class GifTests
{
    private static readonly Color32 Red = new(255, 0, 0), Green = new(0, 200, 0), Blue = new(0, 0, 255);

    private static Gif Encode(IReadOnlyList<GifFrame> frames, GifOptions? options = null)
    {
        using var ms = new MemoryStream();
        GifWriter.Write(frames, ms, options ?? new GifOptions());
        return Gif.Decode(ms.ToArray());
    }

    private static PixelImage Image(int w, int h, Func<int, int, Color32> f)
    {
        var img = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++) img[x, y] = f(x, y);
        return img;
    }

    [Fact]
    public void ExactPalette_RoundTripsFramesPixelForPixel()
    {
        var a = Image(5, 3, (x, y) => (x + y) % 3 == 0 ? Red : (x + y) % 3 == 1 ? Green : Blue);
        var b = Image(5, 3, (x, y) => x < 2 ? Blue : new Color32(10, 20, 30));
        var gif = Encode([new GifFrame(a, 100), new GifFrame(b, 100)]);

        Assert.True(gif.HasGlobalTable);
        Assert.Equal((5, 3), (gif.Width, gif.Height));
        Assert.Equal(2, gif.Frames.Count);
        Assert.Equal(a.Pixels, gif.Frames[0].Pixels);
        Assert.Equal(b.Pixels, gif.Frames[1].Pixels);
        Assert.All(gif.Frames, f => Assert.Null(f.TransparentIndex));
        Assert.All(gif.Frames, f => Assert.Equal(1, f.Disposal));
    }

    [Fact]
    public void Scale_EnlargesWithNearestNeighbour()
    {
        var a = Image(2, 2, (x, y) => x == y ? Red : Blue);
        var gif = Encode([new GifFrame(a, 100)], new GifOptions { Scale = 3 });

        Assert.Equal((6, 6), (gif.Width, gif.Height));
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                Assert.Equal(a[x / 3, y / 3], gif.Frames[0].Pixels[y * 6 + x]);
    }

    [Fact]
    public void Transparency_LowAlphaBecomesTransparentIndexWithRestoreDisposal()
    {
        var a = Image(4, 1, (x, _) => x switch
        {
            0 => Color32.Transparent,
            1 => Red.WithAlpha(127),
            2 => Green.WithAlpha(128),
            _ => Blue,
        });
        var gif = Encode([new GifFrame(a, 100), new GifFrame(a, 100)]);

        var f = gif.Frames[0];
        Assert.NotNull(f.TransparentIndex);
        Assert.Equal(2, f.Disposal);
        Assert.Equal(f.TransparentIndex, f.Indices[0]);
        Assert.Equal(f.TransparentIndex, f.Indices[1]);
        Assert.NotEqual(f.TransparentIndex, f.Indices[2]);
        Assert.Equal([Color32.Transparent, Color32.Transparent, Green, Blue], f.Pixels);
    }

    [Fact]
    public void Background_CompositesInsteadOfTransparency()
    {
        var a = Image(3, 1, (x, _) => x switch { 0 => Color32.Transparent, 1 => new Color32(255, 255, 255, 128), _ => Red });
        var gif = Encode([new GifFrame(a, 100)], new GifOptions { Background = Color32.Black });

        var f = gif.Frames[0];
        Assert.Null(f.TransparentIndex);
        Assert.Equal([Color32.Black, new Color32(128, 128, 128), Red], f.Pixels);
    }

    [Fact]
    public void Delays_RoundAccumulatedTimeWithMinimum()
    {
        // 33ms ×3 = 99ms → 3·4·3cs(누적 반올림, 합 10cs). 5ms는 최소 2cs, 그 뒤 프레임이 늘어난 만큼 덜 씀.
        Assert.Equal([3, 4, 3], GifWriter.DelaysCs([33, 33, 33]));
        Assert.Equal([2, 2, 6], GifWriter.DelaysCs([5, 15, 80]));

        var img = Image(1, 1, (_, _) => Red);
        var gif = Encode([new GifFrame(img, 33), new GifFrame(img, 33), new GifFrame(img, 33)]);
        Assert.Equal([3, 4, 3], gif.Frames.Select(f => f.DelayCs));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    public void Loop_WritesNetscapeExtension(int playCount, int expectedLoop)
    {
        var img = Image(1, 1, (_, _) => Red);
        var gif = Encode([new GifFrame(img, 100), new GifFrame(img, 100)], new GifOptions { PlayCount = playCount });
        Assert.Equal(expectedLoop, gif.LoopCount);
    }

    [Fact]
    public void Loop_PlayOnceOmitsExtension()
    {
        var img = Image(1, 1, (_, _) => Red);
        var gif = Encode([new GifFrame(img, 100), new GifFrame(img, 100)], new GifOptions { PlayCount = 1 });
        Assert.Null(gif.LoopCount);
    }

    [Fact]
    public void Lzw_HandlesTableResetsOnLargeNoisyImage()
    {
        // 255색 무작위 128×128 → 코드 수가 4096을 훌쩍 넘어 12비트·지우기 코드를 여러 번 지난다.
        var rng = new Random(7);
        var palette = Enumerable.Range(0, 255).Select(i => new Color32((byte)i, (byte)(i * 7), (byte)(255 - i))).Distinct().ToArray();
        var img = Image(128, 128, (_, _) => palette[rng.Next(palette.Length)]);
        var gif = Encode([new GifFrame(img, 100)]);

        Assert.True(gif.HasGlobalTable);
        Assert.True(gif.Frames[0].CodesRead > 4096 * 2);
        Assert.True(gif.Frames[0].ClearsSeen > 1);
        Assert.Equal(img.Pixels, gif.Frames[0].Pixels);
    }

    [Fact]
    public void ManyColours_UseLocalTablesOfAtMost256AndStayClose()
    {
        var a = TestUtil.RandomImage(40, 40, seed: 3, opaque: true);
        var b = Image(40, 40, (x, y) => new Color32((byte)(x * 6), (byte)(y * 6), 128));
        var gif = Encode([new GifFrame(a, 100), new GifFrame(b, 100)]);

        Assert.False(gif.HasGlobalTable);
        Assert.All(gif.Frames, f => Assert.True(f.LocalTableSize is > 0 and <= 256));
        Assert.All(gif.Frames, f => Assert.True(f.Pixels.Distinct().Count() <= 256));
        // 그라디언트 프레임(1600색 중 단순한 분포)은 가깝게 줄어야 한다.
        double err = b.Pixels.Zip(gif.Frames[1].Pixels, (s, d) => Math.Abs(s.R - d.R) + Math.Abs(s.G - d.G) + Math.Abs(s.B - d.B)).Average();
        Assert.True(err < 12, $"평균 오차 {err}");
    }

    [Fact]
    public void ProjectFrames_FollowTagPlaybackOrderDurationsAndSkipHiddenLayers()
    {
        var p = new SpriteProject(1, 1);
        p.AddFrame();
        p.AddFrame();
        var bottom = p.Layers[0];
        var top = p.AddLayer(name: "top");
        Color32[] colors = [Red, Green, Blue];
        for (int f = 0; f < 3; f++)
        {
            p.BeginCelStroke(p.IndexOf(bottom), f).Set(0, 0, colors[f]);
            Assert.True(p.CommitCelStroke("점"));
            p.BeginCelStroke(p.IndexOf(top), f).Set(0, 0, Color32.White);
            Assert.True(p.CommitCelStroke("점"));
            p.SetFrameDuration(f, 100 + 10 * f);
        }
        top.Visible = false;
        var tag = p.AddTag("walk", 0, 2, TagDirection.PingPong);

        var frames = GifWriter.ProjectFrames(p, tag);
        Assert.Equal([Red, Green, Blue, Green], frames.Select(f => f.Image[0, 0]));
        Assert.Equal([100, 110, 120, 110], frames.Select(f => f.DurationMs));
        Assert.Equal([Red, Green, Blue], GifWriter.ProjectFrames(p, null).Select(f => f.Image[0, 0]));
    }

    /// <summary>시험용 최소 GIF 디코더(전역/지역 팔레트, GCE, NETSCAPE 확장, LZW).</summary>
    private sealed class Gif
    {
        public int Width, Height;
        public bool HasGlobalTable;
        public int? LoopCount;
        public readonly List<Frame> Frames = [];

        public sealed class Frame
        {
            public int DelayCs, Disposal;
            public int? TransparentIndex;
            public int LocalTableSize;
            public byte[] Indices = [];
            public Color32[] Pixels = [];
            public int CodesRead, ClearsSeen;
        }

        public static Gif Decode(byte[] d)
        {
            var gif = new Gif();
            int p = 0;
            int U16() { int v = d[p] | d[p + 1] << 8; p += 2; return v; }
            Assert.Equal("GIF89a", System.Text.Encoding.ASCII.GetString(d, 0, 6));
            p = 6;
            gif.Width = U16();
            gif.Height = U16();
            int flags = d[p++];
            p += 2;
            Color32[]? global = null;
            if ((flags & 0x80) != 0)
            {
                gif.HasGlobalTable = true;
                global = Table(d, ref p, 1 << ((flags & 7) + 1));
            }
            int delay = 0, disposal = 0;
            int? trans = null;
            while (true)
            {
                int b = d[p++];
                if (b == 0x3B) break;
                if (b == 0x21)
                {
                    int label = d[p++];
                    var blocks = SubBlocks(d, ref p);
                    if (label == 0xF9)
                    {
                        var g = blocks[0];
                        disposal = (g[0] >> 2) & 7;
                        delay = g[1] | g[2] << 8;
                        trans = (g[0] & 1) != 0 ? g[3] : null;
                    }
                    else if (label == 0xFF && System.Text.Encoding.ASCII.GetString(blocks[0]) == "NETSCAPE2.0")
                        gif.LoopCount = blocks[1][1] | blocks[1][2] << 8;
                    continue;
                }
                Assert.Equal(0x2C, b);
                int x = U16(), y = U16(), w = U16(), h = U16();
                Assert.Equal((0, 0, gif.Width, gif.Height), (x, y, w, h));
                int f = d[p++];
                var frame = new Frame { DelayCs = delay, Disposal = disposal, TransparentIndex = trans };
                var table = global;
                if ((f & 0x80) != 0)
                {
                    frame.LocalTableSize = 1 << ((f & 7) + 1);
                    table = Table(d, ref p, frame.LocalTableSize);
                }
                Assert.NotNull(table);
                int minCode = d[p++];
                var data = SubBlocks(d, ref p).SelectMany(s => s).ToArray();
                frame.Indices = Lzw(data, minCode, w * h, frame);
                frame.Pixels = frame.Indices.Select(i => i == trans ? Color32.Transparent : table![i]).ToArray();
                gif.Frames.Add(frame);
                trans = null;
                delay = disposal = 0;
            }
            return gif;
        }

        private static Color32[] Table(byte[] d, ref int p, int n)
        {
            var t = new Color32[n];
            for (int i = 0; i < n; i++, p += 3) t[i] = new Color32(d[p], d[p + 1], d[p + 2]);
            return t;
        }

        private static List<byte[]> SubBlocks(byte[] d, ref int p)
        {
            var list = new List<byte[]>();
            while (d[p] != 0)
            {
                int n = d[p++];
                list.Add(d[p..(p + n)]);
                p += n;
            }
            p++;
            return list;
        }

        private static byte[] Lzw(byte[] data, int minCode, int count, Frame stats)
        {
            int clear = 1 << minCode, eoi = clear + 1;
            var prefix = new int[4096];
            var suffix = new byte[4096];
            var length = new int[4096];
            for (int i = 0; i < clear; i++) { prefix[i] = -1; suffix[i] = (byte)i; length[i] = 1; }
            var output = new List<byte>(count);
            int codeSize = minCode + 1, next = eoi + 1, prev = -1;
            long bits = 0;
            int bitCount = 0, pos = 0;
            bool ended = false;
            byte First(int code) { while (prefix[code] >= 0) code = prefix[code]; return suffix[code]; }
            void Append(int code)
            {
                var tmp = new byte[length[code]];
                for (int i = tmp.Length - 1; i >= 0; i--) { tmp[i] = suffix[code]; code = prefix[code]; }
                output.AddRange(tmp);
            }
            while (!ended)
            {
                while (bitCount < codeSize)
                {
                    Assert.True(pos < data.Length, "LZW 데이터가 끝 코드 전에 끝남");
                    bits |= (long)data[pos++] << bitCount;
                    bitCount += 8;
                }
                int code = (int)(bits & ((1 << codeSize) - 1));
                bits >>= codeSize;
                bitCount -= codeSize;
                stats.CodesRead++;
                if (code == clear)
                {
                    stats.ClearsSeen++;
                    codeSize = minCode + 1;
                    next = eoi + 1;
                    prev = -1;
                    continue;
                }
                if (code == eoi) { ended = true; continue; }
                if (prev < 0)
                {
                    Assert.True(code < clear);
                    Append(code);
                    prev = code;
                    continue;
                }
                Assert.True(code <= next, $"잘못된 LZW 코드 {code} (다음 {next})");
                byte k = code < next ? First(code) : First(prev);
                if (next < 4096)
                {
                    prefix[next] = prev;
                    suffix[next] = k;
                    length[next] = length[prev] + 1;
                    next++;
                }
                Append(code);
                if (next == 1 << codeSize && codeSize < 12) codeSize++;
                prev = code;
            }
            Assert.Equal(count, output.Count);
            return [.. output];
        }
    }
}
