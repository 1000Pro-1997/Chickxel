using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Chickxel.Core.Model;
using Chickxel.Core.Localization;

namespace Chickxel.Core.IO;

/// <summary>움직이는 PNG의 프레임 하나와 보여 줄 시간(밀리초).</summary>
public readonly record struct PngFrame(PixelImage Image, int DurationMs);

/// <summary>
/// 순수 C# PNG 읽기/쓰기. 모든 표준 PNG(회색·RGB·팔레트·알파, 1~16비트, 인터레이스)를 RGBA로 읽고,
/// 8비트 RGBA PNG로 무손실 저장한다. 감마(gAMA) 등 색 보정은 적용하지 않아 픽셀 값이 그대로 유지된다.
/// </summary>
public static class PngIO
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static PixelImage Load(string path)
    {
        using var fs = File.OpenRead(path);
        return Load(fs);
    }

    /// <summary>PNG 스트림을 읽는다. 형식이 잘못되면 <see cref="InvalidDataException"/>.</summary>
    public static PixelImage Load(Stream stream)
    {
        var header = ReadChunks(stream, readData: true);
        return Decode(header);
    }

    /// <summary>픽셀을 읽지 않고 크기만 읽는다(썸네일 목록 등).</summary>
    public static (int Width, int Height) ReadSize(string path)
    {
        using var fs = File.OpenRead(path);
        var h = ReadChunks(fs, readData: false);
        return (h.Width, h.Height);
    }

    /// <summary>
    /// 8비트 RGBA PNG로 저장한다. 같은 폴더의 임시 파일에 먼저 쓴 뒤 바꿔치기하므로
    /// 실패해도 기존 파일이 깨지지 않는다.
    /// </summary>
    public static void Save(PixelImage image, string path)
    {
        var full = Path.GetFullPath(path);
        var temp = full + ".chickxel-tmp";
        try
        {
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                Save(image, fs);
            FileReplace.Commit(temp, full);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public static void Save(PixelImage image, Stream stream) => Save(image, stream, null);

    /// <summary>
    /// 8비트 RGBA PNG에 글 덩어리 하나(tEXt: <paramref name="keyword"/> + NUL + <paramref name="text"/>, Latin-1)를 IHDR 바로 뒤에 붙여 쓴다.
    /// 글은 ASCII여야 한다(그 밖의 글자는 <see cref="ArgumentException"/> — JSON이면 \u 이스케이프로 넣는다). 같은 입력은 늘 같은 바이트다.
    /// </summary>
    public static void SaveWithText(PixelImage image, Stream stream, string keyword, string text)
    {
        if (keyword.Length is < 1 or > 79 || keyword.Any(c => c is < ' ' or > '~') || text.Any(c => c > '~' || (c < ' ' && c != '\n')))
            throw new ArgumentException("PNG text must be printable ASCII.", nameof(text));
        var data = new byte[keyword.Length + 1 + text.Length];
        Encoding.ASCII.GetBytes(keyword, data);
        Encoding.ASCII.GetBytes(text, data.AsSpan(keyword.Length + 1));
        Save(image, stream, null, data);
    }

    /// <summary>
    /// PNG 바이트에서 글 덩어리(tEXt, 또는 압축하지 않은 iTXt) 중 <paramref name="keyword"/>인 첫 것의 글(tEXt는 Latin-1, iTXt는 UTF-8)을 읽는다. 없으면 null.
    /// 덩어리 구조(길이·CRC)가 틀리거나 글이 <paramref name="maxBytes"/>를 넘으면 <see cref="InvalidDataException"/>. 그림은 풀지 않는다.
    /// </summary>
    public static string? ReadText(ReadOnlySpan<byte> png, string keyword, int maxBytes)
    {
        if (png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature)) throw new InvalidDataException(CoreStrings.Png_InvalidFile);
        int at = Signature.Length;
        while (at + 12 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png[at..]);
            if (length < 0 || length > png.Length - at - 12) throw new InvalidDataException(CoreStrings.Png_InvalidChunkLength);
            var type = png.Slice(at + 4, 4);
            var data = png.Slice(at + 8, length);
            uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
            if (crc != BinaryPrimitives.ReadUInt32BigEndian(png[(at + 8 + length)..])) throw new InvalidDataException(CoreStrings.Png_InvalidFile);
            at += 12 + length;
            if (type.SequenceEqual("IEND"u8)) break;
            bool plain = type.SequenceEqual("tEXt"u8);
            if (!plain && !type.SequenceEqual("iTXt"u8)) continue;
            int nul = data.IndexOf((byte)0);
            if (nul < 0 || !Encoding.Latin1.GetString(data[..nul]).Equals(keyword, StringComparison.Ordinal)) continue;
            var rest = data[(nul + 1)..];
            if (!plain)
            {
                // iTXt: 압축 표시(0만 읽음)·압축 방법, 언어 태그 NUL, 번역 키워드 NUL, UTF-8 글
                if (rest.Length < 2 || rest[0] != 0) throw new InvalidDataException(CoreStrings.Png_InvalidFile);
                rest = rest[2..];
                for (int skip = 0; skip < 2; skip++)
                {
                    int end = rest.IndexOf((byte)0);
                    if (end < 0) throw new InvalidDataException(CoreStrings.Png_InvalidFile);
                    rest = rest[(end + 1)..];
                }
            }
            if (rest.Length > maxBytes) throw new InvalidDataException(CoreStrings.Png_InvalidChunkLength);
            try
            {
                return plain ? Encoding.Latin1.GetString(rest) : new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(rest);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException(CoreStrings.Png_InvalidFile);
            }
        }
        return null;
    }

    /// <summary>
    /// 거른 데이터가 이보다 크면 줄 묶음(<see cref="ChunkBytes"/>)마다 나눠 여러 코어에서 거르고 압축한다. 묶음마다 압축 사전이 새로 시작해
    /// 파일이 아주 조금 커지는 대신, 큰 시트(8k×8k)를 여러 배 빨리 쓴다. 이보다 작은 그림(셀 대부분)은 한 번에 압축해 예전과 같은 바이트를 쓴다.
    /// </summary>
    private const int ParallelMinBytes = 8 << 20;

    /// <summary>나눠 압축할 때 묶음 하나의 거른 데이터 크기(대략).</summary>
    private const int ChunkBytes = 2 << 20;

    /// <param name="stage">진행(단위: 픽셀, 모두 <c>Width × Height</c>).</param>
    internal static void Save(PixelImage image, Stream stream, WorkStage? stage, byte[]? text = null)
    {
        stream.Write(Signature);
        WriteHeader(stream, image.Width, image.Height);
        if (text is not null) WriteChunk(stream, "tEXt", text);
        int rowBytes = image.Width * 4 + 1;
        if ((long)rowBytes * image.Height <= ParallelMinBytes)
        {
            using var compressed = Compress(image, stage);
            WriteChunk(stream, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        }
        else WriteChunk(stream, "IDAT", CompressParallel(image, rowBytes, stage));
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// 움직이는 PNG(APNG)로 저장한다: 같은 크기의 프레임을 통째로 바꿔 가며(겹치기·지우기 없음) 끝없이 되풀이한다.
    /// 첫 프레임이 기본 그림(IDAT)이라 APNG를 모르는 읽기(<see cref="Load(Stream)"/> 포함)는 첫 프레임을 본다. 프레임이 하나면 보통 PNG로 쓴다.
    /// 작은 그림(미리보기)용이라 나눠 압축하지 않는다.
    /// </summary>
    public static void SaveAnimated(IReadOnlyList<PngFrame> frames, Stream stream)
    {
        if (frames.Count == 0) throw new ArgumentException("No frames.", nameof(frames));
        int w = frames[0].Image.Width, h = frames[0].Image.Height;
        if (frames.Any(frame => frame.Image.Width != w || frame.Image.Height != h))
            throw new ArgumentException("Frames must have the same size.", nameof(frames));
        if (frames.Count == 1)
        {
            Save(frames[0].Image, stream, null);
            return;
        }
        stream.Write(Signature);
        WriteHeader(stream, w, h);
        Span<byte> actl = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(actl, frames.Count);
        BinaryPrimitives.WriteInt32BigEndian(actl[4..], 0); // 끝없이
        WriteChunk(stream, "acTL", actl);
        Span<byte> fctl = stackalloc byte[26];
        int sequence = 0;
        for (int i = 0; i < frames.Count; i++)
        {
            fctl.Clear();
            BinaryPrimitives.WriteInt32BigEndian(fctl, sequence++);
            BinaryPrimitives.WriteInt32BigEndian(fctl[4..], w);
            BinaryPrimitives.WriteInt32BigEndian(fctl[8..], h);
            BinaryPrimitives.WriteUInt16BigEndian(fctl[20..], (ushort)Math.Clamp(frames[i].DurationMs, 1, ushort.MaxValue));
            BinaryPrimitives.WriteUInt16BigEndian(fctl[22..], 1000); // 지연 단위: 1/1000초
            WriteChunk(stream, "fcTL", fctl); // 오프셋 0, dispose_op 0(그대로), blend_op 0(덮어쓰기)
            using var compressed = Compress(frames[i].Image, null);
            var data = compressed.GetBuffer().AsSpan(0, (int)compressed.Length);
            if (i == 0)
            {
                WriteChunk(stream, "IDAT", data);
                continue;
            }
            var fdat = new byte[4 + data.Length];
            BinaryPrimitives.WriteInt32BigEndian(fdat, sequence++);
            data.CopyTo(fdat.AsSpan(4));
            WriteChunk(stream, "fdAT", fdat);
        }
        WriteChunk(stream, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteHeader(Stream stream, int w, int h)
    {
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, w);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        ihdr[10] = 0; // deflate
        ihdr[11] = 0; // adaptive filter
        ihdr[12] = 0; // no interlace
        WriteChunk(stream, "IHDR", ihdr);
    }

    /// <summary>줄마다 걸러 한 번에 zlib으로 압축한 그림 데이터.</summary>
    private static MemoryStream Compress(PixelImage image, WorkStage? stage)
    {
        int w = image.Width, stride = w * 4;
        var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var prev = new byte[stride];
            var cur = new byte[stride];
            var candidate = new byte[stride];
            var best = new byte[stride + 1];
            for (int y = 0; y < image.Height; y++)
            {
                FilterRow(image.Pixels, w, y, prev, cur, candidate, best);
                z.Write(best);
                (prev, cur) = (cur, prev);
                stage?.Step(w);
            }
        }
        return compressed;
    }

    /// <summary>
    /// 줄 묶음마다 따로 거르고 raw deflate로 압축해 이어 붙인 zlib 데이터. 마지막이 아닌 묶음은 동기 flush(바이트 경계, 끝 블록 아님)에서 끊어
    /// 이어 붙여도 하나의 deflate 흐름이 되고, Adler-32는 묶음마다 구해 합친다. 필터는 줄마다 그 줄과 윗줄 원본만 보므로 나눠도 같다.
    /// </summary>
    private static byte[] CompressParallel(PixelImage image, int rowBytes, WorkStage? stage)
    {
        int w = image.Width, h = image.Height, stride = w * 4;
        int rowsPerChunk = Math.Max(1, ChunkBytes / rowBytes);
        int chunks = (h + rowsPerChunk - 1) / rowsPerChunk;
        var parts = new byte[chunks][];
        var adlers = new uint[chunks];
        Parallel.For(0, chunks, c =>
        {
            int y0 = c * rowsPerChunk, y1 = Math.Min(h, y0 + rowsPerChunk), length = (y1 - y0) * rowBytes;
            var filtered = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
            try
            {
                var prev = new byte[stride];
                var cur = new byte[stride];
                var candidate = new byte[stride];
                if (y0 > 0) RawRow(image.Pixels, w, y0 - 1, prev);
                for (int y = y0; y < y1; y++)
                {
                    FilterRow(image.Pixels, w, y, prev, cur, candidate, filtered.AsSpan((y - y0) * rowBytes, rowBytes));
                    (prev, cur) = (cur, prev);
                }
                adlers[c] = Adler32(filtered.AsSpan(0, length));
                using var ms = new MemoryStream(length / 4 + 64);
                var deflate = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true);
                deflate.Write(filtered, 0, length);
                if (c < chunks - 1)
                {
                    deflate.Flush(); // 동기 flush: 끝 블록 없이 바이트 경계까지 내보낸다
                    parts[c] = ms.ToArray();
                    deflate.Dispose(); // 끝 블록은 버린다(이미 복사함)
                }
                else
                {
                    deflate.Dispose();
                    parts[c] = ms.ToArray();
                }
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(filtered);
            }
            stage?.Step((long)(y1 - y0) * w);
        });

        uint adler = adlers[0];
        for (int c = 1; c < chunks; c++)
        {
            int rows = Math.Min(h, (c + 1) * rowsPerChunk) - c * rowsPerChunk;
            adler = Adler32Combine(adler, adlers[c], (long)rows * rowBytes);
        }
        var result = new byte[2 + parts.Sum(p => (long)p.Length) + 4];
        result[0] = 0x78; // deflate, 32K 창
        result[1] = 0xDA; // 최대 압축(헤더 검사값 포함)
        int at = 2;
        foreach (var p in parts)
        {
            p.CopyTo(result, at);
            at += p.Length;
        }
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(at), adler);
        return result;
    }

    /// <summary><paramref name="y"/>행 원본 RGBA 바이트.</summary>
    private static void RawRow(Color32[] px, int w, int y, byte[] row)
    {
        int start = y * w;
        for (int x = 0; x < w; x++)
        {
            var c = px[start + x];
            int i = x * 4;
            row[i] = c.R; row[i + 1] = c.G; row[i + 2] = c.B; row[i + 3] = c.A;
        }
    }

    /// <summary>
    /// <paramref name="y"/>행을 거른 줄(맨 앞 1바이트 = 필터 종류)을 <paramref name="dest"/>에 쓴다. 행마다 절댓값 합이 가장 작은 필터를 고른다(표준 휴리스틱).
    /// <paramref name="prev"/> = 윗줄 원본(첫 줄이면 0), <paramref name="cur"/>에는 이 줄 원본이 남는다.
    /// </summary>
    private static void FilterRow(Color32[] px, int w, int y, byte[] prev, byte[] cur, byte[] candidate, Span<byte> dest)
    {
        RawRow(px, w, y, cur);
        long bestScore = long.MaxValue;
        for (byte f = 0; f <= 4; f++)
        {
            long score = ApplyFilter(f, cur, prev, candidate, 4);
            if (score >= bestScore) continue;
            bestScore = score;
            dest[0] = f;
            candidate.CopyTo(dest[1..]);
        }
    }

    private const uint AdlerMod = 65521;

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        const int NMax = 5552; // 이만큼 더해도 uint가 넘치지 않는다
        uint a = 1, b = 0;
        while (data.Length > 0)
        {
            int n = Math.Min(NMax, data.Length);
            foreach (var v in data[..n])
            {
                a += v;
                b += a;
            }
            a %= AdlerMod;
            b %= AdlerMod;
            data = data[n..];
        }
        return (b << 16) | a;
    }

    /// <summary>앞 조각의 Adler-32와 뒤 조각(길이 <paramref name="length2"/>)의 Adler-32로 이어 붙인 데이터의 값(zlib adler32_combine).</summary>
    private static uint Adler32Combine(uint adler1, uint adler2, long length2)
    {
        ulong rem = (ulong)(length2 % AdlerMod);
        ulong sum1 = adler1 & 0xffff;
        ulong sum2 = rem * sum1 % AdlerMod;
        sum1 += (adler2 & 0xffff) + AdlerMod - 1;
        sum2 += ((adler1 >> 16) & 0xffff) + ((adler2 >> 16) & 0xffff) + AdlerMod - rem;
        if (sum1 >= AdlerMod) sum1 -= AdlerMod;
        if (sum1 >= AdlerMod) sum1 -= AdlerMod;
        if (sum2 >= (ulong)AdlerMod << 1) sum2 -= (ulong)AdlerMod << 1;
        if (sum2 >= AdlerMod) sum2 -= AdlerMod;
        return (uint)(sum1 | (sum2 << 16));
    }

    // ───────────── 쓰기 도우미 ─────────────

    private static long ApplyFilter(byte filter, byte[] cur, byte[] prev, byte[] output, int bpp)
    {
        long score = 0;
        for (int i = 0; i < cur.Length; i++)
        {
            int a = i >= bpp ? cur[i - bpp] : 0;
            int b = prev[i];
            int c = i >= bpp ? prev[i - bpp] : 0;
            int v = filter switch
            {
                0 => cur[i],
                1 => cur[i] - a,
                2 => cur[i] - b,
                3 => cur[i] - ((a + b) >> 1),
                _ => cur[i] - Paeth(a, b, c),
            };
            var bv = (byte)v;
            output[i] = bv;
            score += (sbyte)bv < 0 ? -(sbyte)bv : bv;
        }
        return score;
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> buf = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(buf, data.Length);
        Encoding.ASCII.GetBytes(type, buf[4..]);
        s.Write(buf);
        s.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, buf[4..8]), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
        s.Write(buf[..4]);
    }

    // ───────────── 읽기 ─────────────

    internal sealed class PngData
    {
        public int Width, Height, BitDepth, ColorType, Interlace;
        public byte[]? Palette;      // RGB 3바이트씩
        public byte[]? PaletteAlpha; // tRNS(팔레트)
        public ushort[]? TransparentKey; // tRNS(회색 1개 / RGB 3개, 16비트 원값)
        public MemoryStream Idat = new();
    }

    private static PngData ReadChunks(Stream stream, bool readData)
    {
        Span<byte> sig = stackalloc byte[8];
        ReadExactly(stream, sig);
        if (!sig.SequenceEqual(Signature)) throw new InvalidDataException(CoreStrings.Png_InvalidFile);

        var d = new PngData();
        bool sawHeader = false;
        Span<byte> head = stackalloc byte[8];
        while (true)
        {
            if (!TryReadExactly(stream, head)) break; // IEND 없이 끝난 파일도 허용
            int length = BinaryPrimitives.ReadInt32BigEndian(head);
            if (length < 0) throw new InvalidDataException(CoreStrings.Png_InvalidChunkLength);
            string type = Encoding.ASCII.GetString(head[4..8]);
            if (type == "IEND") break;

            bool needed = type is "IHDR" or "PLTE" or "tRNS" || (type == "IDAT" && readData);
            if (!needed)
            {
                Skip(stream, length + 4L);
                continue;
            }
            var data = new byte[length];
            ReadExactly(stream, data);
            Skip(stream, 4); // CRC

            switch (type)
            {
                case "IHDR":
                    if (length < 13) throw new InvalidDataException(CoreStrings.Png_InvalidHeader);
                    d.Width = BinaryPrimitives.ReadInt32BigEndian(data);
                    d.Height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4));
                    d.BitDepth = data[8];
                    d.ColorType = data[9];
                    d.Interlace = data[12];
                    sawHeader = true;
                    if (!readData) return d;
                    break;
                case "PLTE":
                    d.Palette = data;
                    break;
                case "tRNS":
                    ReadTransparency(d, data);
                    break;
                case "IDAT":
                    d.Idat.Write(data);
                    break;
            }
        }
        if (!sawHeader) throw new InvalidDataException(CoreStrings.Png_HeaderMissing);
        return d;
    }

    /// <summary>tRNS 내용을 색 형식에 맞게 담는다(IHDR 다음에).</summary>
    internal static void ReadTransparency(PngData d, byte[] data)
    {
        if (d.ColorType == 3) d.PaletteAlpha = data;
        else if (d.ColorType == 0 && data.Length >= 2)
            d.TransparentKey = [BinaryPrimitives.ReadUInt16BigEndian(data)];
        else if (d.ColorType == 2 && data.Length >= 6)
            d.TransparentKey =
            [
                BinaryPrimitives.ReadUInt16BigEndian(data),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(2)),
                BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4)),
            ];
    }

    internal static PixelImage Decode(PngData d)
    {
        int channels = d.ColorType switch
        {
            0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4,
            _ => throw new InvalidDataException(CoreStrings.FormatPng_UnsupportedColorType(colorType: d.ColorType)),
        };
        if (d.BitDepth is not (1 or 2 or 4 or 8 or 16))
            throw new InvalidDataException(CoreStrings.FormatPng_UnsupportedBitDepth(bitDepth: d.BitDepth));
        if (d.ColorType == 3 && d.Palette is null)
            throw new InvalidDataException(CoreStrings.Png_PaletteMissing);
        if (d.Width < 1 || d.Height < 1 || d.Width > PixelImage.MaxSize || d.Height > PixelImage.MaxSize)
            throw new InvalidDataException(CoreStrings.FormatPng_ImageTooLarge(width: d.Width, height: d.Height));

        d.Idat.Position = 0;
        using var z = new ZLibStream(d.Idat, CompressionMode.Decompress);
        var image = new PixelImage(d.Width, d.Height);
        int bitsPerPixel = channels * d.BitDepth;
        int bpp = Math.Max(1, bitsPerPixel / 8);

        if (d.Interlace == 0)
        {
            DecodePass(z, d, image, 0, 0, 1, 1, d.Width, d.Height, bitsPerPixel, bpp);
        }
        else
        {
            ReadOnlySpan<(int xs, int ys, int dx, int dy)> passes =
                [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];
            foreach (var (xs, ys, dx, dy) in passes)
            {
                int pw = (d.Width - xs + dx - 1) / dx;
                int ph = (d.Height - ys + dy - 1) / dy;
                if (pw <= 0 || ph <= 0) continue;
                DecodePass(z, d, image, xs, ys, dx, dy, pw, ph, bitsPerPixel, bpp);
            }
        }
        return image;
    }

    private static void DecodePass(Stream z, PngData d, PixelImage image, int xs, int ys, int dx, int dy,
        int pw, int ph, int bitsPerPixel, int bpp)
    {
        int stride = (pw * bitsPerPixel + 7) / 8;
        var prev = new byte[stride];
        var cur = new byte[stride];
        Span<byte> filterByte = stackalloc byte[1];
        for (int row = 0; row < ph; row++)
        {
            ReadExactly(z, filterByte);
            ReadExactly(z, cur);
            Unfilter(filterByte[0], cur, prev, bpp);
            int y = ys + row * dy;
            for (int col = 0; col < pw; col++)
                image.Pixels[y * image.Width + xs + col * dx] = ReadPixel(d, cur, col);
            (prev, cur) = (cur, prev);
        }
    }

    private static void Unfilter(byte filter, byte[] cur, byte[] prev, int bpp)
    {
        switch (filter)
        {
            case 0: break;
            case 1:
                for (int i = bpp; i < cur.Length; i++) cur[i] += cur[i - bpp];
                break;
            case 2:
                for (int i = 0; i < cur.Length; i++) cur[i] += prev[i];
                break;
            case 3:
                for (int i = 0; i < cur.Length; i++)
                    cur[i] += (byte)(((i >= bpp ? cur[i - bpp] : 0) + prev[i]) >> 1);
                break;
            case 4:
                for (int i = 0; i < cur.Length; i++)
                    cur[i] += (byte)Paeth(i >= bpp ? cur[i - bpp] : 0, prev[i], i >= bpp ? prev[i - bpp] : 0);
                break;
            default:
                throw new InvalidDataException(CoreStrings.FormatPng_InvalidFilter(filter: filter));
        }
    }

    private static Color32 ReadPixel(PngData d, byte[] row, int col)
    {
        int depth = d.BitDepth;
        switch (d.ColorType)
        {
            case 0:
            {
                int raw = Sample(row, col, depth);
                byte g = Scale(raw, depth);
                byte a = d.TransparentKey is { } k && k[0] == raw ? (byte)0 : (byte)255;
                return new Color32(g, g, g, a);
            }
            case 2:
            {
                int r = Sample(row, col * 3, depth), g = Sample(row, col * 3 + 1, depth), b = Sample(row, col * 3 + 2, depth);
                byte a = d.TransparentKey is { Length: 3 } k && k[0] == r && k[1] == g && k[2] == b ? (byte)0 : (byte)255;
                return new Color32(Scale(r, depth), Scale(g, depth), Scale(b, depth), a);
            }
            case 3:
            {
                int idx = Sample(row, col, depth);
                var pal = d.Palette!;
                if (idx * 3 + 2 >= pal.Length) return Color32.Transparent;
                byte a = d.PaletteAlpha is { } pa && idx < pa.Length ? pa[idx] : (byte)255;
                return new Color32(pal[idx * 3], pal[idx * 3 + 1], pal[idx * 3 + 2], a);
            }
            case 4:
            {
                byte g = Scale(Sample(row, col * 2, depth), depth);
                return new Color32(g, g, g, Scale(Sample(row, col * 2 + 1, depth), depth));
            }
            default:
                return new Color32(
                    Scale(Sample(row, col * 4, depth), depth),
                    Scale(Sample(row, col * 4 + 1, depth), depth),
                    Scale(Sample(row, col * 4 + 2, depth), depth),
                    Scale(Sample(row, col * 4 + 3, depth), depth));
        }
    }

    /// <summary>행에서 n번째 표본(채널 값)을 원래 비트 깊이 그대로 꺼낸다.</summary>
    private static int Sample(byte[] row, int index, int depth)
    {
        switch (depth)
        {
            case 8: return row[index];
            case 16: return (row[index * 2] << 8) | row[index * 2 + 1];
            default:
                int bit = index * depth;
                int shift = 8 - depth - (bit & 7);
                return (row[bit >> 3] >> shift) & ((1 << depth) - 1);
        }
    }

    private static byte Scale(int value, int depth) => depth switch
    {
        8 => (byte)value,
        16 => (byte)(value >> 8),
        _ => (byte)(value * 255 / ((1 << depth) - 1)),
    };

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void ReadExactly(Stream s, Span<byte> buffer)
    {
        if (!TryReadExactly(s, buffer)) throw new InvalidDataException(CoreStrings.Png_Truncated);
    }

    private static bool TryReadExactly(Stream s, Span<byte> buffer)
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

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
