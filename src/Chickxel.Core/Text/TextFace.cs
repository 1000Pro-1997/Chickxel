using Chickxel.Core.Model;
using SkiaSharp;

namespace Chickxel.Core.Text;

/// <summary>
/// 그려 둔 글자 하나: 펜 이동 폭(px), 잉크 사각형(펜·기준선 기준 — <see cref="Left"/>은 오른쪽 +, <see cref="Top"/>은 아래 +, 기준선 위 잉크는 음수),
/// 칸마다 알파(0~255, 줄마다 <see cref="Width"/>개). 잉크가 없으면 <see cref="Width"/> = 0.
/// </summary>
internal sealed class RasterGlyph(int advance, int left, int top, int width, int height, byte[] alpha)
{
    public int Advance { get; } = advance;
    public int Left { get; } = left;
    public int Top { get; } = top;
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Alpha { get; } = alpha;
    public bool HasInk => Width > 0 && Height > 0;
}

/// <summary>
/// 글꼴 + 글씨 모양(크기·굵게·기울임·부드럽게·가로세로 비율)으로 정해진 그리기 면: 줄 높이와 글자 그림(처음 쓸 때 그려 두고 계속 씀).
/// 같은 설정이면 같은 면을 나눠 쓴다(<see cref="For"/>, 여러 스레드에서 불러도 됨).
/// </summary>
internal abstract class TextFace
{
    private const int MaxCached = 64;
    private static readonly object CacheGate = new();
    private static readonly Dictionary<FaceKey, TextFace> Cache = [];

    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, RasterGlyph?> _glyphs = new();

    /// <summary>기준선 위 높이(px, 줄 위쪽 끝 → 기준선).</summary>
    public int Ascent { get; protected init; }

    /// <summary>기준선 아래 깊이(px).</summary>
    public int Descent { get; protected init; }

    public int LineHeight => Ascent + Descent;

    /// <summary>코드 포인트의 글자(없는 글자는 대신 그리는 글자). null = 그리지 않고 폭도 없음.</summary>
    public RasterGlyph? Glyph(int codePoint) => _glyphs.GetOrAdd(codePoint, Render);

    /// <summary>이 글꼴에 그 글자가 있는지(대신 그리는 글자 말고).</summary>
    public abstract bool Has(int codePoint);

    protected abstract RasterGlyph? Render(int codePoint);

    /// <summary>글씨 효과 <paramref name="e"/>를 그릴 면(없는 글꼴은 기본 글꼴).</summary>
    public static TextFace For(TextEffect e)
    {
        var source = FontLibrary.Source(e.FontName);
        bool vector = source is VectorSource;
        var key = new FaceKey(source.Key, e.Size, e.Bold, e.Italic, vector && e.Smooth, e.ScaleX, e.ScaleY);
        lock (CacheGate)
            if (Cache.TryGetValue(key, out var face)) return face;
        TextFace created = source switch
        {
            VectorSource v => new VectorFace(v.Typeface, e.Size == 0 ? VectorFace.DefaultSize : e.Size, e.Bold, e.Italic, e.Smooth, e.ScaleX, e.ScaleY),
            BitmapSource b => BitmapFace.Create(b, e.Size, e.Bold, e.Italic, e.ScaleX, e.ScaleY),
            _ => throw new InvalidOperationException(),
        };
        lock (CacheGate)
        {
            if (Cache.TryGetValue(key, out var face)) return face;
            if (Cache.Count >= MaxCached) Cache.Clear();
            Cache[key] = created;
        }
        return created;
    }

    /// <summary>글꼴 목록이 바뀌면(사용자 글꼴 더하기·지우기) 그려 둔 면을 버린다.</summary>
    internal static void ClearCache()
    {
        lock (CacheGate) Cache.Clear();
    }

    /// <summary>기울임: 기준선 바로 위 줄(<paramref name="up"/> = 0)은 그대로, 위로 4줄마다 오른쪽으로 1px(기준선 아래는 왼쪽).</summary>
    protected static int ItalicShift(int up) => (int)Math.Floor(up / 4.0);

    protected static int FloorDiv(long a, long b) => (int)Math.Floor(a / (double)b);

    /// <summary>
    /// 사용자 글꼴에 없는 글자를 대신 그릴 기본 글꼴(Galmuri11) 면: 정수 배 <c>round(<paramref name="pixels"/> / <paramref name="native"/>)</c>(최소 1), 같은 비율·굵게·기울임.
    /// </summary>
    protected static BitmapFace FallbackFor(double pixels, double native, bool bold, bool italic, int scaleX, int scaleY)
        => new(FontLibrary.Default.Font, Math.Max(1, (int)Math.Round(pixels / native)), bold, italic, scaleX, scaleY, null);

    private readonly record struct FaceKey(string Source, int Size, bool Bold, bool Italic, bool Smooth, int ScaleX, int ScaleY);
}

/// <summary>
/// 도트 글꼴 면: 정수 배(<c>round(크기 / 원래 줄 높이)</c>, 최소 1) 다음 가로·세로 비율로 가장 가까운 칸 늘리기.
/// 굵게 = 원래 글자에서 오른쪽으로 1도트 넓힘(펜 이동도 1도트 더), 기울임 = 원래 도트 줄마다 밀기(<see cref="TextFace.ItalicShift"/>) 다음 늘리기.
/// 원래 칸 (u, v)(펜·기준선 기준)는 늘린 뒤 [⌊u·sx⌋, ⌊(u+1)·sx⌋) × [⌊v·sy⌋, ⌊(v+1)·sy⌋)를 채운다.
/// </summary>
internal sealed class BitmapFace : TextFace
{
    private readonly PixelFont _font;
    private readonly int _mulX, _mulY;
    private readonly bool _bold, _italic;
    private readonly BitmapFace? _fallback;

    /// <param name="fallback">이 글꼴에 없는 글자를 그릴 면(사용자 도트 글꼴만 — 기본 글꼴은 자기 대신 글자 U+FFFD).</param>
    internal BitmapFace(PixelFont font, int factor, bool bold, bool italic, int scaleX, int scaleY, BitmapFace? fallback)
    {
        _font = font;
        _mulX = factor * scaleX;
        _mulY = factor * scaleY;
        _bold = bold;
        _italic = italic;
        _fallback = fallback;
        Ascent = -Y(-font.Ascent);
        Descent = Y(font.Descent);
    }

    public static BitmapFace Create(BitmapSource source, int size, bool bold, bool italic, int scaleX, int scaleY)
    {
        var font = source.Font;
        int factor = size <= 0 ? 1 : Math.Max(1, (int)Math.Round(size / (double)Math.Max(1, font.LineHeight)));
        var fallback = source.Key.StartsWith("builtin:", StringComparison.Ordinal)
            ? null
            : FallbackFor(font.LineHeight * factor, FontLibrary.Default.Font.LineHeight, bold, italic, scaleX, scaleY);
        return new BitmapFace(font, factor, bold, italic, scaleX, scaleY, fallback);
    }

    private int X(int u) => FloorDiv((long)u * _mulX, 100);

    private int Y(int v) => FloorDiv((long)v * _mulY, 100);

    public override bool Has(int codePoint) => _font.Contains(codePoint);

    protected override RasterGlyph? Render(int codePoint)
    {
        if (!_font.Contains(codePoint) && _fallback is not null && _fallback.Has(codePoint)) return _fallback.Glyph(codePoint);
        int glyph = _font.Find(codePoint);
        if (glyph < 0) return _fallback?.Glyph(codePoint);
        ref readonly var g = ref _font[glyph];
        int w = g.Width + (_bold && g.Width > 0 ? 1 : 0), h = g.Height, advance = X(g.Advance + (_bold ? 1 : 0));
        if (w == 0 || h == 0) return new RasterGlyph(advance, 0, 0, 0, 0, []);

        // 원래 도트 줄 r(위에서 0)의 기준선 기준 위쪽 끝 v = r − (yoff + 높이), 기울임 밀기는 기준선 위 줄 수(up = yoff + 높이 − 1 − r)로
        int topV = -(g.YOffset + h);
        int minShift = _italic ? ItalicShift(g.YOffset) : 0, maxShift = _italic ? ItalicShift(g.YOffset + h - 1) : 0;
        int left = X(g.XOffset + Math.Min(minShift, maxShift)), right = X(g.XOffset + w + Math.Max(minShift, maxShift));
        int top = Y(topV), bottom = Y(-g.YOffset);
        int width = right - left, height = bottom - top;
        if (width <= 0 || height <= 0) return new RasterGlyph(advance, 0, 0, 0, 0, []);
        var alpha = new byte[width * height];
        for (int r = 0; r < h; r++)
        {
            int shift = _italic ? ItalicShift(g.YOffset + h - 1 - r) : 0;
            int y0 = Y(topV + r) - top, y1 = Y(topV + r + 1) - top;
            if (y1 <= y0) continue;
            for (int c = 0; c < w; c++)
            {
                bool ink = c < g.Width && _font.Ink(glyph, c, r) || _bold && c > 0 && _font.Ink(glyph, c - 1, r);
                if (!ink) continue;
                int x0 = X(g.XOffset + shift + c) - left, x1 = X(g.XOffset + shift + c + 1) - left;
                for (int y = y0; y < y1; y++)
                    Array.Fill(alpha, (byte)255, y * width + x0, x1 - x0);
            }
        }
        return new RasterGlyph(advance, left, top, width, height, alpha);
    }
}

/// <summary>
/// 벡터 글꼴 면(SkiaSharp): 크기 px(세로 비율을 곱함), 가로 비율은 글꼴 가로 늘이기, 굵게 = Skia 가짜 굵게.
/// 또렷하게(기본) = 덮인 정도 50% 이상만 찍음(1비트), 부드럽게 = 덮인 정도를 알파로. 펜 이동은 px 정수로 반올림해 도트 칸에 맞춘다.
/// 기울임은 그린 뒤 줄마다 민다. 글꼴에 없는 글자는 기본 글꼴(Galmuri11)을 크기에 맞는 정수 배로.
/// </summary>
internal sealed class VectorFace : TextFace
{
    public const int DefaultSize = 12;

    /// <summary>대신 그리는 기본 글꼴(Galmuri11)이 원래 크기(1배)인 벡터 글꼴 크기(px).</summary>
    private const double FallbackNativeSize = 12;

    private readonly SKTypeface _typeface;
    private readonly SKFont _font;
    private readonly bool _italic, _smooth;
    private readonly int _pad;
    private readonly BitmapFace _fallback;
    private readonly object _gate = new();

    public VectorFace(SKTypeface typeface, int size, bool bold, bool italic, bool smooth, int scaleX, int scaleY)
    {
        _typeface = typeface;
        _font = new SKFont(typeface, size * scaleY / 100f)
        {
            ScaleX = scaleX / (float)scaleY,
            Embolden = bold,
            Edging = SKFontEdging.Antialias,
            Subpixel = false,
            Hinting = SKFontHinting.None,
            BaselineSnap = true,
        };
        _italic = italic;
        _smooth = smooth;
        _pad = 2 + (bold ? (int)Math.Ceiling(size / 16.0) : 0);
        var metrics = _font.Metrics;
        int ascent = (int)Math.Ceiling(-metrics.Ascent - 0.01), descent = (int)Math.Ceiling(metrics.Descent - 0.01);
        if (ascent + descent <= 0) (ascent, descent) = ((int)Math.Ceiling(size * scaleY / 100.0 * 0.8), (int)Math.Ceiling(size * scaleY / 100.0 * 0.2));
        Ascent = Math.Max(0, ascent);
        Descent = Math.Max(0, descent);
        // Galmuri11은 12px 글꼴 크기쯤에서 원래 크기
        _fallback = FallbackFor(size, FallbackNativeSize, bold, italic, scaleX, scaleY);
    }

    public override bool Has(int codePoint) => _typeface.GetGlyph(codePoint) != 0;

    protected override RasterGlyph? Render(int codePoint)
    {
        ushort id = _typeface.GetGlyph(codePoint);
        if (id == 0 && _fallback.Has(codePoint)) return _fallback.Glyph(codePoint);
        lock (_gate) return Draw(codePoint, id);
    }

    private RasterGlyph Draw(int codePoint, ushort id)
    {
        Span<float> widths = stackalloc float[1];
        Span<SKRect> bounds = stackalloc SKRect[1];
        _font.GetGlyphWidths([id], widths, bounds, null);
        int advance = (int)Math.Round(widths[0]);
        var b = bounds[0];
        if (b.IsEmpty) return new RasterGlyph(advance, 0, 0, 0, 0, []);

        // 펜 (ox, oy)에 그림: 잉크 사각형 + 둘레 여유(굵게 퍼짐)
        int ox = _pad - (int)Math.Floor(b.Left), oy = _pad - (int)Math.Floor(b.Top);
        int w = (int)Math.Ceiling(b.Right) + ox + _pad, h = (int)Math.Ceiling(b.Bottom) + oy + _pad;
        if (w <= 0 || h <= 0 || (long)w * h > 4096 * 4096) return new RasterGlyph(advance, 0, 0, 0, 0, []);
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Alpha8, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { Color = SKColors.White, IsAntialias = true })
        {
            canvas.Clear(SKColors.Transparent);
            canvas.DrawText(char.ConvertFromUtf32(codePoint), ox, oy, _font, paint);
        }
        var pixels = bitmap.GetPixelSpan();
        int stride = bitmap.RowBytes;

        // 기울임 밀기를 넣어 잉크 사각형 찾기(펜 기준 좌표: x − ox + 밀기, y − oy)
        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
        for (int y = 0; y < h; y++)
        {
            int shift = _italic ? ItalicShift(oy - y - 1) : 0;
            for (int x = 0; x < w; x++)
            {
                if (Coverage(pixels[y * stride + x]) == 0) continue;
                minX = Math.Min(minX, x + shift);
                maxX = Math.Max(maxX, x + shift);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }
        if (maxX < minX) return new RasterGlyph(advance, 0, 0, 0, 0, []);
        int width = maxX - minX + 1, height = maxY - minY + 1;
        var alpha = new byte[width * height];
        for (int y = minY; y <= maxY; y++)
        {
            int shift = _italic ? ItalicShift(oy - y - 1) : 0;
            for (int x = 0; x < w; x++)
            {
                byte a = Coverage(pixels[y * stride + x]);
                if (a != 0) alpha[(y - minY) * width + x + shift - minX] = a;
            }
        }
        return new RasterGlyph(advance, minX - ox, minY - oy, width, height, alpha);
    }

    /// <summary>덮인 정도 → 알파: 부드럽게면 그대로, 아니면 50% 이상만 255.</summary>
    private byte Coverage(byte a) => _smooth ? a : a >= 128 ? (byte)255 : (byte)0;
}
