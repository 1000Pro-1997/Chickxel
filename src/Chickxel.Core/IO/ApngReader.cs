using System.Buffers.Binary;
using System.Drawing;
using System.Text;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 움직이는 PNG(APNG) 읽기(순수 C#, <see cref="PngIO"/>의 픽셀 풀기를 씀): acTL·fcTL·fdAT를 읽어 프레임마다 dispose_op·blend_op를 적용한
/// 캔버스 크기의 완성된 그림으로 푼다. 첫 fcTL보다 앞선 IDAT(기본 그림)는 애니메이션에 넣지 않는다(규격대로).
/// </summary>
public static class ApngReader
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>APNG(acTL이 첫 IDAT보다 앞에 있는 PNG)면 프레임 수, 그냥 PNG이거나 PNG가 아니면 0. 픽셀은 풀지 않는다.</summary>
    public static int CountFrames(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[8];
            if (fs.Read(head) != 8 || !head.SequenceEqual(Signature)) return 0;
            while (TryChunkHeader(fs, out int length, out string type))
            {
                if (type == "acTL" && length >= 8)
                {
                    Span<byte> b = stackalloc byte[4];
                    return fs.Read(b) == 4 ? (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32BigEndian(b)) : 0;
                }
                if (type is "IDAT" or "IEND") return 0;
                fs.Seek(length + 4L, SeekOrigin.Current);
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public static DecodedAnimation? Load(string path, int maxFrames = int.MaxValue)
    {
        using var fs = File.OpenRead(path);
        return Read(fs, maxFrames);
    }

    /// <summary>
    /// APNG를 프레임마다 푼다(<paramref name="maxFrames"/>까지). acTL이 없는 그냥 PNG면 null.
    /// 형식이 잘못됐으면 <see cref="InvalidDataException"/>(읽은 프레임이 있으면 거기까지 돌려줌).
    /// </summary>
    public static DecodedAnimation? Read(Stream stream, int maxFrames = int.MaxValue)
    {
        Span<byte> sig = stackalloc byte[8];
        if (stream.ReadAtLeast(sig, 8, throwOnEndOfStream: false) != 8 || !sig.SequenceEqual(Signature))
            throw new InvalidDataException(Localization.CoreStrings.Png_InvalidFile);
        var header = new PngIO.PngData();
        bool animated = false, sawHeader = false, sawIdat = false;
        FrameControl? pending = null;
        MemoryStream? data = null;
        FrameCompositor? compositor = null;
        var images = new List<PixelImage>();
        var durations = new List<int>();

        void Finish()
        {
            if (pending is not { } fc || data is null || compositor is null || images.Count >= maxFrames) return;
            var frame = new PngIO.PngData
            {
                Width = fc.Width, Height = fc.Height, BitDepth = header.BitDepth, ColorType = header.ColorType, Interlace = header.Interlace,
                Palette = header.Palette, PaletteAlpha = header.PaletteAlpha, TransparentKey = header.TransparentKey, Idat = data,
            };
            var image = PngIO.Decode(frame);
            // 첫 프레임의 "이전으로"는 "배경으로"와 같다(규격).
            var dispose = fc.Dispose == FrameDisposal.Previous && images.Count == 0 ? FrameDisposal.Background : fc.Dispose;
            // 첫 프레임은 투명 캔버스 위라 blend가 결과에 영향이 없다.
            images.Add(compositor.Compose(new Rectangle(fc.X, fc.Y, fc.Width, fc.Height), image.Pixels, fc.Blend, dispose));
            durations.Add(FrameDurations.FromFile(fc.DelayMs));
            pending = null;
            data = null;
        }

        try
        {
            while (images.Count < maxFrames && TryChunkHeader(stream, out int length, out string type))
            {
                if (length < 0) throw new InvalidDataException(Localization.CoreStrings.Png_InvalidChunkLength);
                if (type == "IEND") break;
                if (type is not ("IHDR" or "PLTE" or "tRNS" or "acTL" or "fcTL" or "IDAT" or "fdAT"))
                {
                    Skip(stream, length + 4L);
                    continue;
                }
                var chunk = new byte[length];
                if (stream.ReadAtLeast(chunk, length, throwOnEndOfStream: false) != length) break;
                Skip(stream, 4);
                switch (type)
                {
                    case "IHDR":
                        if (length < 13) throw new InvalidDataException(Localization.CoreStrings.Png_InvalidHeader);
                        header.Width = BinaryPrimitives.ReadInt32BigEndian(chunk);
                        header.Height = BinaryPrimitives.ReadInt32BigEndian(chunk.AsSpan(4));
                        header.BitDepth = chunk[8];
                        header.ColorType = chunk[9];
                        header.Interlace = chunk[12];
                        sawHeader = true;
                        break;
                    case "PLTE":
                        header.Palette = chunk;
                        break;
                    case "tRNS":
                        PngIO.ReadTransparency(header, chunk);
                        break;
                    case "acTL":
                        if (!sawIdat) animated = true;
                        break;
                    case "fcTL":
                        if (!animated) break;
                        Finish();
                        if (!sawHeader) throw new InvalidDataException(Localization.CoreStrings.Png_HeaderMissing);
                        compositor ??= new FrameCompositor(header.Width, header.Height);
                        pending = FrameControl.Parse(chunk, header.Width, header.Height);
                        data = new MemoryStream();
                        break;
                    case "IDAT":
                        if (!animated) return null;
                        sawIdat = true;
                        data?.Write(chunk); // fcTL이 앞에 없으면 기본 그림 — 애니메이션에서 뺀다
                        break;
                    case "fdAT":
                        if (animated && length > 4) data?.Write(chunk.AsSpan(4));
                        break;
                }
            }
            if (!animated) return null;
            Finish();
        }
        catch (InvalidDataException) when (images.Count > 0)
        {
        }
        if (images.Count == 0) throw new InvalidDataException(Localization.CoreStrings.Png_Truncated);
        return new DecodedAnimation(header.Width, header.Height, images, durations);
    }

    private static bool TryChunkHeader(Stream s, out int length, out string type)
    {
        Span<byte> head = stackalloc byte[8];
        if (s.ReadAtLeast(head, 8, throwOnEndOfStream: false) != 8)
        {
            (length, type) = (0, "");
            return false;
        }
        length = BinaryPrimitives.ReadInt32BigEndian(head);
        type = Encoding.ASCII.GetString(head[4..8]);
        return true;
    }

    private static void Skip(Stream s, long count)
    {
        if (s.CanSeek)
        {
            s.Seek(count, SeekOrigin.Current);
            return;
        }
        Span<byte> tmp = stackalloc byte[4096];
        while (count > 0)
        {
            int n = s.Read(tmp[..(int)Math.Min(tmp.Length, count)]);
            if (n == 0) return;
            count -= n;
        }
    }

    /// <summary>fcTL 한 개: 칸, 길이(ms), 처리 방법, 합성 방법.</summary>
    private readonly record struct FrameControl(int X, int Y, int Width, int Height, int DelayMs, FrameDisposal Dispose, FrameBlend Blend)
    {
        public static FrameControl Parse(byte[] c, int canvasW, int canvasH)
        {
            if (c.Length < 26) throw new InvalidDataException(Localization.CoreStrings.Png_InvalidChunkLength);
            int w = BinaryPrimitives.ReadInt32BigEndian(c.AsSpan(4)), h = BinaryPrimitives.ReadInt32BigEndian(c.AsSpan(8));
            int x = BinaryPrimitives.ReadInt32BigEndian(c.AsSpan(12)), y = BinaryPrimitives.ReadInt32BigEndian(c.AsSpan(16));
            if (w < 1 || h < 1 || x < 0 || y < 0 || (long)x + w > canvasW || (long)y + h > canvasH)
                throw new InvalidDataException(Localization.CoreStrings.Png_InvalidHeader);
            int num = BinaryPrimitives.ReadUInt16BigEndian(c.AsSpan(20)), den = BinaryPrimitives.ReadUInt16BigEndian(c.AsSpan(22));
            int ms = (int)Math.Round(num * 1000.0 / (den == 0 ? 100 : den));
            var dispose = c[24] switch { 1 => FrameDisposal.Background, 2 => FrameDisposal.Previous, _ => FrameDisposal.None };
            var blend = c[25] == 1 ? FrameBlend.Over : FrameBlend.Source;
            return new FrameControl(x, y, w, h, ms, dispose, blend);
        }
    }
}
