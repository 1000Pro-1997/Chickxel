using System.Drawing;
using System.IO.Compression;
using Chickxel.Core.Model;

namespace Chickxel.Core.Text;

/// <summary>
/// 글씨 효과의 도트 글꼴(어셈블리에 든 Galmuri BDF, gzip — <c>Text/Fonts</c>). 글꼴마다 처음 쓸 때 한 번 읽어 두고 계속 쓴다
/// (여러 스레드에서 같이 불러도 한 번만 읽음).
/// </summary>
public static class PixelFonts
{
    private static readonly Lazy<PixelFont>[] Fonts = [.. Enum.GetValues<TextFont>().Select(font => new Lazy<PixelFont>(() => Load(font)))];

    /// <summary>글꼴 <paramref name="font"/>(정의되지 않은 값이면 Galmuri11).</summary>
    public static PixelFont Get(TextFont font) => Fonts[(uint)font < (uint)Fonts.Length ? (int)font : (int)TextFont.Galmuri11].Value;

    /// <summary>어셈블리 리소스 이름(<c>Chickxel.Fonts.Galmuri11.bdf.gz</c>).</summary>
    private static string ResourceName(TextFont font)
        => $"Chickxel.Fonts.{(font == TextFont.Galmuri11Bold ? "Galmuri11-Bold" : font.ToString())}.bdf.gz";

    private static PixelFont Load(TextFont font)
    {
        var name = ResourceName(font);
        using var stream = typeof(PixelFonts).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Missing font resource: {name}");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return PixelFont.Parse(reader);
    }
}

/// <summary>
/// 도트 글꼴 하나(BDF 2.1에서 읽음): 글자마다 펜 이동 폭·잉크 사각형(BBX)·비트(줄마다 바이트 단위, 왼쪽 = 높은 비트)를 한 배열에 모아 둔다.
/// 줄 위쪽 끝에서 <see cref="Ascent"/>만큼 아래가 기준선이고, 글자 잉크의 위쪽 끝은 줄 위에서 <c>Ascent − (yoff + 높이)</c>, 왼쪽 끝은 펜 + xoff다.
/// 없는 글자는 U+FFFD(없으면 '?') 모양으로, 그것도 없으면 건너뛴다(폭 0). 탭은 빈칸 4개.
/// </summary>
public sealed class PixelFont
{
    /// <summary>탭 한 개 = 빈칸 몇 개.</summary>
    internal const int TabSpaces = 4;

    private readonly Dictionary<int, int> _index;
    private readonly Glyph[] _glyphs;
    private readonly byte[] _bits;
    private readonly int _fallback, _space;

    private PixelFont(int ascent, int descent, Dictionary<int, int> index, Glyph[] glyphs, byte[] bits)
    {
        Ascent = ascent;
        Descent = descent;
        _index = index;
        _glyphs = glyphs;
        _bits = bits;
        _fallback = index.TryGetValue(0xFFFD, out int replacement) ? replacement : index.TryGetValue('?', out int question) ? question : -1;
        _space = index.TryGetValue(' ', out int space) ? space : -1;
    }

    /// <summary>기준선 위 높이(px, 줄 위쪽 끝 → 기준선).</summary>
    public int Ascent { get; }

    /// <summary>기준선 아래 깊이(px).</summary>
    public int Descent { get; }

    /// <summary>줄 높이(px) = <see cref="Ascent"/> + <see cref="Descent"/>.</summary>
    public int LineHeight => Ascent + Descent;

    /// <summary>글꼴에 코드 포인트 <paramref name="codePoint"/>의 글자가 있는지(대신 그리는 글자 말고).</summary>
    public bool Contains(int codePoint) => _index.ContainsKey(codePoint);

    /// <summary>
    /// 여러 줄 글(<c>'\n'</c>으로 나눔)이 차지하는 크기: 너비 = 가장 넓은 줄(글자 폭 + <paramref name="letterSpacing"/>의 합 − 끝 글자 뒤 간격),
    /// 높이 = 줄 수 × <see cref="LineHeight"/> + (줄 수 − 1) × <paramref name="lineSpacing"/>(음수가 되면 0).
    /// </summary>
    public Size Measure(string text, int letterSpacing, int lineSpacing)
    {
        int width = 0, lines = 0;
        var rest = text.AsSpan();
        while (true)
        {
            int end = rest.IndexOf('\n');
            var line = end < 0 ? rest : rest[..end];
            width = Math.Max(width, LineWidth(line, letterSpacing));
            lines++;
            if (end < 0) break;
            rest = rest[(end + 1)..];
        }
        return new Size(width, Math.Max(0, lines * LineHeight + (lines - 1) * lineSpacing));
    }

    /// <summary>줄 하나(줄바꿈 없음)의 너비: 놓이는 글자마다 폭 + 간격, 끝 글자 뒤 간격은 뺌. 놓이는 글자가 없으면 0.</summary>
    internal int LineWidth(ReadOnlySpan<char> line, int letterSpacing)
    {
        int pen = 0, items = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (rune.Value == '\t')
            {
                if (_space < 0) continue;
                pen += TabSpaces * (_glyphs[_space].Advance + letterSpacing);
                items += TabSpaces;
            }
            else if (Find(rune.Value) is int glyph and >= 0)
            {
                pen += _glyphs[glyph].Advance + letterSpacing;
                items++;
            }
        }
        return items == 0 ? 0 : pen - letterSpacing;
    }

    /// <summary>코드 포인트의 글자 번호(없으면 대신 그리는 글자, 그것도 없으면 −1). 탭은 부르는 쪽이 빈칸으로 바꾼다.</summary>
    internal int Find(int codePoint) => _index.TryGetValue(codePoint, out int glyph) ? glyph : _fallback;

    /// <summary>빈칸 글자 번호(없으면 −1).</summary>
    internal int Space => _space;

    internal ref readonly Glyph this[int glyph] => ref _glyphs[glyph];

    /// <summary>글자 <paramref name="glyph"/>의 잉크 사각형 안 (<paramref name="x"/>, <paramref name="y"/>)가 찍히는지.</summary>
    internal bool Ink(int glyph, int x, int y)
    {
        ref readonly var g = ref _glyphs[glyph];
        return (_bits[g.Offset + y * g.Stride + (x >> 3)] & (0x80 >> (x & 7))) != 0;
    }

    /// <summary>
    /// BDF 2.1 글꼴 읽기: 머리의 <c>FONT_ASCENT</c>·<c>FONT_DESCENT</c>, 글자마다 <c>ENCODING</c>·<c>DWIDTH</c>·<c>BBX</c>·<c>BITMAP</c>(16진 줄).
    /// 코드 포인트가 음수이거나 크기가 저장 범위를 넘는 글자, 같은 코드 포인트의 두 번째 글자는 버린다.
    /// </summary>
    internal static PixelFont Parse(TextReader reader)
    {
        int ascent = -1, descent = -1;
        var index = new Dictionary<int, int>();
        var glyphs = new List<Glyph>();
        var bits = new byte[1 << 16];
        int used = 0;
        int code = -1, advance = 0, w = 0, h = 0, xo = 0, yo = 0;
        Span<int> numbers = stackalloc int[4];
        while (reader.ReadLine() is { } text)
        {
            var line = text.AsSpan();
            if (line.StartsWith("FONT_ASCENT ")) ascent = Numbers(line[12..], numbers) > 0 ? numbers[0] : -1;
            else if (line.StartsWith("FONT_DESCENT ")) descent = Numbers(line[13..], numbers) > 0 ? numbers[0] : -1;
            else if (line.StartsWith("STARTCHAR")) code = -1;
            else if (line.StartsWith("ENCODING ")) code = Numbers(line[9..], numbers) > 0 ? numbers[0] : -1;
            else if (line.StartsWith("DWIDTH ")) advance = Numbers(line[7..], numbers) > 0 ? numbers[0] : 0;
            else if (line.StartsWith("BBX "))
            {
                if (Numbers(line[4..], numbers) < 4) throw new InvalidDataException($"Bad BDF line: {text}");
                (w, h, xo, yo) = (numbers[0], numbers[1], numbers[2], numbers[3]);
            }
            else if (line.SequenceEqual("BITMAP"))
            {
                int stride = (w + 7) >> 3, size = stride * h;
                bool keep = code >= 0 && !index.ContainsKey(code) && w is >= 0 and <= byte.MaxValue && h is >= 0 and <= byte.MaxValue
                            && xo is >= sbyte.MinValue and <= sbyte.MaxValue && yo is >= sbyte.MinValue and <= sbyte.MaxValue
                            && advance is >= short.MinValue and <= short.MaxValue;
                if (keep && used + size > bits.Length) Array.Resize(ref bits, Math.Max(bits.Length * 2, used + size));
                for (int row = 0; row < h; row++)
                {
                    var hex = (reader.ReadLine() ?? throw new InvalidDataException("Truncated BDF bitmap")).AsSpan().Trim();
                    if (!keep) continue;
                    for (int b = 0; b < stride && b * 2 + 1 < hex.Length; b++)
                        bits[used + row * stride + b] = (byte)(Nibble(hex[b * 2]) << 4 | Nibble(hex[b * 2 + 1]));
                }
                if (!keep) continue;
                index[code] = glyphs.Count;
                glyphs.Add(new Glyph((short)advance, (byte)w, (byte)h, (sbyte)xo, (sbyte)yo, used));
                used += size;
            }
        }
        if (ascent < 0 || descent < 0) throw new InvalidDataException("BDF font without FONT_ASCENT/FONT_DESCENT");
        Array.Resize(ref bits, used);
        return new PixelFont(ascent, descent, index, [.. glyphs], bits);
    }

    /// <summary>공백으로 나뉜 정수들을 앞에서부터 <paramref name="into"/>에 채우고 읽은 개수를 돌려준다(숫자가 아닌 조각에서 멈춤).</summary>
    private static int Numbers(ReadOnlySpan<char> text, Span<int> into)
    {
        int count = 0, i = 0;
        while (count < into.Length)
        {
            while (i < text.Length && text[i] == ' ') i++;
            if (i >= text.Length) break;
            bool negative = text[i] == '-';
            if (negative) i++;
            int start = i, value = 0;
            while (i < text.Length && char.IsAsciiDigit(text[i]) && value < 100_000_000) value = value * 10 + (text[i++] - '0');
            if (i == start) break;
            into[count++] = negative ? -value : value;
        }
        return count;
    }

    private static int Nibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'F' => c - 'A' + 10,
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => 0,
    };
}

/// <summary>글자 하나: 펜 이동 폭, 잉크 사각형(너비·높이·기준점에서 x·y 어긋남 — y는 위로 +), 비트 배열 안 시작 위치.</summary>
internal readonly struct Glyph(short advance, byte width, byte height, sbyte xOffset, sbyte yOffset, int offset)
{
    public int Advance => advance;
    public int Width => width;
    public int Height => height;
    public int XOffset => xOffset;
    public int YOffset => yOffset;
    public int Offset => offset;

    /// <summary>비트 한 줄의 바이트 수.</summary>
    public int Stride => (width + 7) >> 3;
}
