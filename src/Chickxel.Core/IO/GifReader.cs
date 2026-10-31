using System.Buffers.Binary;
using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// GIF87a/89a 읽기(순수 C#): 프레임마다 처리 방법(0·1 그대로, 2 배경으로 — 투명, 3 이전으로)과 투명 색을 적용해 캔버스 크기의 완성된 그림으로 푼다.
/// 지역·전역 팔레트, 인터레이스, 캔버스 밖으로 나간 프레임(잘림), 끝이 잘린 파일(읽은 프레임까지)을 다룬다.
/// 배경색 인덱스는 브라우저처럼 쓰지 않는다(처음 캔버스와 "배경으로"는 투명).
/// </summary>
public static class GifReader
{
    /// <summary>첫 바이트가 GIF 서명인지.</summary>
    public static bool IsGif(ReadOnlySpan<byte> header)
        => header.Length >= 6 && header[..3].SequenceEqual("GIF"u8) && (header[3..6].SequenceEqual("87a"u8) || header[3..6].SequenceEqual("89a"u8));

    public static DecodedAnimation Load(string path, int maxFrames = int.MaxValue)
    {
        using var fs = File.OpenRead(path);
        return Read(fs, maxFrames);
    }

    /// <summary>픽셀을 풀지 않고 캔버스 크기와 프레임 수만 센다(많은 프레임을 열기 전에 묻기용). GIF가 아니면 <see cref="InvalidDataException"/>.</summary>
    public static (int Width, int Height, int Frames) Count(string path)
    {
        using var fs = File.OpenRead(path);
        var r = new Reader(new BufferedStream(fs));
        var (w, h, _) = ReadHeader(r);
        int frames = 0;
        while (r.TryByte(out int block) && block != 0x3B)
        {
            if (block == 0x21)
            {
                if (!r.TryByte(out _)) break;
                if (!r.SkipSubBlocks()) break;
            }
            else if (block == 0x2C)
            {
                if (!r.TrySkip(8) || !r.TryByte(out int packed)) break;
                if ((packed & 0x80) != 0 && !r.TrySkip(3 << ((packed & 7) + 1))) break;
                if (!r.TryByte(out _) || !r.SkipSubBlocks()) break;
                frames++;
            }
            else break;
        }
        return (w, h, frames);
    }

    /// <summary>GIF를 프레임마다 푼다(<paramref name="maxFrames"/>까지). 형식이 잘못됐거나 프레임이 하나도 없으면 <see cref="InvalidDataException"/>.</summary>
    public static DecodedAnimation Read(Stream stream, int maxFrames = int.MaxValue)
    {
        var r = new Reader(stream is MemoryStream or BufferedStream ? stream : new BufferedStream(stream));
        var (width, height, globalTable) = ReadHeader(r);
        var compositor = new FrameCompositor(width, height);
        var images = new List<PixelImage>();
        var durations = new List<int>();
        int delayMs = 0, transparent = -1;
        var disposal = FrameDisposal.None;
        while (images.Count < maxFrames && r.TryByte(out int block) && block != 0x3B)
        {
            if (block == 0x21)
            {
                if (!r.TryByte(out int label)) break;
                if (label == 0xF9)
                {
                    var gce = r.ReadSubBlocks();
                    if (gce is null) break;
                    if (gce.Length >= 4)
                    {
                        int packed = gce[0];
                        disposal = ((packed >> 2) & 7) switch { 2 => FrameDisposal.Background, 3 => FrameDisposal.Previous, _ => FrameDisposal.None };
                        delayMs = BinaryPrimitives.ReadUInt16LittleEndian(gce.AsSpan(1)) * 10;
                        transparent = (packed & 1) != 0 ? gce[3] : -1;
                    }
                }
                else if (!r.SkipSubBlocks()) break;
            }
            else if (block == 0x2C)
            {
                var desc = new byte[9];
                if (!r.TryRead(desc)) break;
                int x = BinaryPrimitives.ReadUInt16LittleEndian(desc), y = BinaryPrimitives.ReadUInt16LittleEndian(desc.AsSpan(2));
                int w = BinaryPrimitives.ReadUInt16LittleEndian(desc.AsSpan(4)), h = BinaryPrimitives.ReadUInt16LittleEndian(desc.AsSpan(6));
                int packed = desc[8];
                var table = globalTable;
                if ((packed & 0x80) != 0)
                {
                    table = new byte[3 << ((packed & 7) + 1)];
                    if (!r.TryRead(table)) break;
                }
                if (!r.TryByte(out int minCode)) break;
                var data = r.ReadSubBlocks();
                if (data is null) break;
                var indices = new byte[w * h];
                // 데이터가 모자라면 남은 칸은 투명 — 브라우저처럼 그때까지 푼 부분만 보인다.
                int filled = LzwDecode(data, minCode, indices);
                if ((packed & 0x40) != 0) indices = Deinterlace(indices, w, h);
                var pixels = new Color32[w * h];
                int colors = table?.Length / 3 ?? 0;
                for (int i = 0; i < pixels.Length; i++)
                {
                    int k = indices[i];
                    if (k == transparent || k >= colors || ((packed & 0x40) == 0 && i >= filled)) continue;
                    pixels[i] = new Color32(table![k * 3], table[k * 3 + 1], table[k * 3 + 2]);
                }
                images.Add(compositor.Compose(new Rectangle(x, y, w, h), pixels, FrameBlend.Over, disposal));
                durations.Add(FrameDurations.FromFile(delayMs));
                (delayMs, transparent, disposal) = (0, -1, FrameDisposal.None);
            }
            else break;
        }
        if (images.Count == 0) throw new InvalidDataException(Localization.CoreStrings.Gif_NoFrames);
        return new DecodedAnimation(width, height, images, durations);
    }

    private static (int Width, int Height, byte[]? GlobalTable) ReadHeader(Reader r)
    {
        var head = new byte[13];
        if (!r.TryRead(head) || !IsGif(head)) throw new InvalidDataException(Localization.CoreStrings.Gif_InvalidFile);
        int w = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(6)), h = BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(8));
        if (w < 1 || h < 1 || w > PixelImage.MaxSize || h > PixelImage.MaxSize) throw new InvalidDataException(Localization.CoreStrings.Gif_InvalidFile);
        byte[]? table = null;
        if ((head[10] & 0x80) != 0)
        {
            table = new byte[3 << ((head[10] & 7) + 1)];
            if (!r.TryRead(table)) throw new InvalidDataException(Localization.CoreStrings.Gif_InvalidFile);
        }
        return (w, h, table);
    }

    /// <summary>GIF 변형 LZW를 푼다. 채운 인덱스 수를 돌려준다(잘못된 코드나 데이터 끝이면 거기서 멈춤).</summary>
    internal static int LzwDecode(byte[] data, int minCodeSize, byte[] output)
    {
        if (minCodeSize is < 1 or > 11) return 0;
        int clear = 1 << minCodeSize, end = clear + 1;
        var prefix = new short[4096];
        var suffix = new byte[4096];
        for (int i = 0; i < clear; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
        }
        int codeSize = minCodeSize + 1, next = end + 1, prev = -1;
        int bits = 0, buffer = 0, pos = 0, outPos = 0;
        var stack = new byte[4096];
        while (outPos < output.Length)
        {
            while (bits < codeSize)
            {
                if (pos >= data.Length) return outPos;
                buffer |= data[pos++] << bits;
                bits += 8;
            }
            int code = buffer & ((1 << codeSize) - 1);
            buffer >>= codeSize;
            bits -= codeSize;
            if (code == clear)
            {
                codeSize = minCodeSize + 1;
                next = end + 1;
                prev = -1;
                continue;
            }
            if (code == end) return outPos;
            int first;
            if (prev < 0)
            {
                if (code >= clear) return outPos;
                output[outPos++] = (byte)code;
                prev = code;
                continue;
            }
            int cur = code;
            if (code > next || code >= 4096) return outPos;
            if (code == next)
            {
                // KwKwK: 이전 문자열 + 이전 문자열의 첫 글자
                cur = prev;
            }
            int n = 0;
            for (int c = cur; c >= 0; c = prefix[c]) stack[n++] = suffix[c];
            first = stack[n - 1];
            for (int i = n - 1; i >= 0 && outPos < output.Length; i--) output[outPos++] = stack[i];
            if (code == next && outPos < output.Length) output[outPos++] = (byte)first;
            if (next < 4096)
            {
                prefix[next] = (short)prev;
                suffix[next] = (byte)first;
                next++;
                if (next == 1 << codeSize && codeSize < 12) codeSize++;
            }
            prev = code;
        }
        return outPos;
    }

    private static byte[] Deinterlace(byte[] src, int w, int h)
    {
        var dst = new byte[src.Length];
        int row = 0;
        ReadOnlySpan<(int Start, int Step)> passes = [(0, 8), (4, 8), (2, 4), (1, 2)];
        foreach (var (start, step) in passes)
            for (int y = start; y < h; y += step, row++)
                src.AsSpan(row * w, w).CopyTo(dst.AsSpan(y * w, w));
        return dst;
    }

    /// <summary>끝이 잘린 파일을 예외 없이 읽는 작은 도우미.</summary>
    private sealed class Reader(Stream s)
    {
        public bool TryByte(out int b)
        {
            b = s.ReadByte();
            return b >= 0;
        }

        public bool TryRead(Span<byte> buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int n = s.Read(buffer[total..]);
                if (n == 0) return false;
                total += n;
            }
            return true;
        }

        public bool TrySkip(int count)
        {
            Span<byte> tmp = stackalloc byte[256];
            while (count > 0)
            {
                int n = s.Read(tmp[..Math.Min(tmp.Length, count)]);
                if (n == 0) return false;
                count -= n;
            }
            return true;
        }

        public bool SkipSubBlocks()
        {
            while (true)
            {
                if (!TryByte(out int size)) return false;
                if (size == 0) return true;
                if (!TrySkip(size)) return false;
            }
        }

        /// <summary>하위 블록을 이어 붙여 읽는다. 끝 블록 전에 파일이 끝나면 읽은 데까지(하나도 없으면 null).</summary>
        public byte[]? ReadSubBlocks()
        {
            using var ms = new MemoryStream();
            Span<byte> block = stackalloc byte[255];
            while (true)
            {
                if (!TryByte(out int size)) return ms.Length > 0 ? ms.ToArray() : null;
                if (size == 0) return ms.ToArray();
                if (!TryRead(block[..size])) return ms.Length > 0 ? ms.ToArray() : null;
                ms.Write(block[..size]);
            }
        }
    }
}
