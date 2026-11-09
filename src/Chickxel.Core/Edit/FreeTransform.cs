using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>자유 변형의 리샘플링 방식.</summary>
public enum ResampleMode
{
    /// <summary>
    /// RotSprite: Scale2x(EPX)를 세 번 = 8배로 키운 그림에서 최근접으로 집는다. 섞지 않으므로 새 색이 생기지 않고,
    /// 회전한 선·모서리가 최근접보다 덜 깨진다. 축에 맞는 변형(0°·90°·180°·270°)은 최근접과 같다.
    /// </summary>
    RotSprite,
    /// <summary>최근접: 대상 픽셀 중심이 떨어지는 원본 픽셀을 그대로 쓴다.</summary>
    Nearest,
}

/// <summary>
/// 자유 변형 값. 원본(떼어 낸 선택 영역, <see cref="FreeTransform.SourceBounds"/>)의 중심이 캔버스의
/// (<see cref="CenterX"/>, <see cref="CenterY"/>)에 놓이고, 원본 중심을 기준으로 가로·세로 배율(음수 = 뒤집기) →
/// 시계 방향 <see cref="Angle"/>(도, 화면 y가 아래) 회전 순으로 바꾼다.
/// <para>
/// 좌표는 연속 좌표: 픽셀 (x,y)는 [x, x+1)×[y, y+1) 칸이고 중심은 (x+0.5, y+0.5). 원본 중심은
/// (X + W/2, Y + H/2)라 크기가 홀수면 반 픽셀 위치다.
/// </para>
/// <para>
/// <see cref="Distort"/>가 있으면 배율·회전 전에 원본 사각형 네 꼭짓점을 그만큼 옮긴 원근 대응(호모그래피)을 먼저 입힌다(자유 왜곡).
/// </para>
/// </summary>
public readonly record struct TransformParams(double CenterX, double CenterY, double Angle, double ScaleX, double ScaleY)
{
    /// <summary>원본 사각형 꼭짓점을 옮긴 양(배율·회전 전, 원본 사각형 가로·세로를 1로 둔 단위). 기본값 = 찌그러뜨리지 않음.</summary>
    public CornerOffsets Distort { get; init; }

    /// <summary>찌그러뜨렸는지(꼭짓점을 하나라도 옮김).</summary>
    public bool IsDistorted => !Distort.IsNone;

    /// <summary>원본 그대로 두는 값(중심 = <paramref name="source"/> 중심, 0°, 100%).</summary>
    public static TransformParams Identity(Rectangle source)
        => new(source.X + source.Width / 2.0, source.Y + source.Height / 2.0, 0, 1, 1);

    /// <summary>각도가 90°의 배수인지(축에 맞는 변형: 회전해도 픽셀 격자가 그대로 맞는다). 찌그러뜨림은 보지 않는다.</summary>
    public bool IsAxisAligned
    {
        get
        {
            double q = Angle / 90;
            return Math.Abs(q - Math.Round(q)) < 1e-9;
        }
    }

    /// <summary>회전의 (sin, cos). 90°의 배수는 정확한 0/±1을 써서 반올림 오차 없이 격자에 맞춘다.</summary>
    public (double Sin, double Cos) SinCos()
    {
        double q = Angle / 90;
        double r = Math.Round(q);
        if (Math.Abs(q - r) < 1e-9)
            return (((int)r % 4 + 4) % 4) switch
            {
                0 => (0, 1),
                1 => (1, 0),
                2 => (0, -1),
                _ => (-1, 0),
            };
        double rad = Angle * Math.PI / 180;
        return (Math.Sin(rad), Math.Cos(rad));
    }

    /// <summary>각도를 (-180, 180]으로 맞춘다.</summary>
    public static double NormalizeAngle(double degrees)
    {
        double a = degrees % 360;
        if (a <= -180) a += 360;
        else if (a > 180) a -= 360;
        return a;
    }
}

/// <summary>
/// 상자 네 꼭짓점(왼쪽 위, 오른쪽 위, 오른쪽 아래, 왼쪽 아래)을 옮긴 양 — 상자 가로·세로를 1로 둔 단위, 배율·회전 전 상자 자체 공간.
/// 상자를 이 꼭짓점들의 사각형으로 보내는 원근 대응(호모그래피)이 된다. 모두 0이면 찌그러뜨리지 않음.
/// </summary>
public readonly record struct CornerOffsets(double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3)
{
    /// <summary>옮긴 꼭짓점이 없는지(모두 0).</summary>
    public bool IsNone => X0 == 0 && Y0 == 0 && X1 == 0 && Y1 == 0 && X2 == 0 && Y2 == 0 && X3 == 0 && Y3 == 0;
}

/// <summary>변형 결과: 떠 있는 픽셀(크기 = 영역 사각형 크기, 영역 밖은 투명)과 그 영역(캔버스 좌표).</summary>
public readonly record struct TransformResult(PixelImage Image, PixelMask Region);

/// <summary>
/// 자유 변형(회전·크기·옮기기·찌그러뜨리기): 떼어 낸 원본 픽셀과 모양을 갖고 있다가 <see cref="TransformParams"/>마다 새로 그린다.
/// 늘 원본에서 다시 그리므로(누적하지 않음) 원래 값이면 원본과 똑같고, 정수 픽셀 옮기기는 무손실이다.
/// <para>
/// 대상 픽셀마다 그 중심을 원본으로 되돌려(역변환) 떨어지는 원본 칸을 집는다. RotSprite는 원본을 Scale2x로 세 번 키운
/// 8배 그림(처음 쓸 때 한 번 만듦)에서 집는다(찌그러뜨리면 축에 맞는 각도여도 RotSprite). 모양 밖 칸은 집지 않으므로 결과에서도 투명하고 영역에서 빠진다.
/// </para>
/// </summary>
public sealed class FreeTransform
{
    /// <summary>RotSprite 8배 그림을 만드는 원본 크기 한도(넓이, 픽셀). 넘으면 메모리를 아끼려고 최근접으로 집는다.</summary>
    public const int MaxRotSpriteArea = 512 * 512;

    private readonly PixelImage _image;
    private readonly PixelMask _region;
    /// <summary>RotSprite 8배 그림: 칸마다 (모양 안인지, 색) 팔레트 번호. 0 = 모양 밖.</summary>
    private int[]? _up8;
    private Color32[]? _palette;

    /// <param name="image">떼어 낸 픽셀(크기 = <paramref name="region"/> 사각형 크기, 모양 밖은 무시).</param>
    /// <param name="region">원본 영역(캔버스 좌표).</param>
    public FreeTransform(PixelImage image, PixelMask region) : this(region, CheckSize(image, region).Clone())
    {
    }

    /// <summary>이 원본만 쓰는 그림을 복사하지 않고 담는다.</summary>
    private FreeTransform(PixelMask region, PixelImage image)
    {
        _image = image;
        _region = region;
    }

    /// <summary>
    /// 이미 따로 만든 그림을 복사하지 않고 원본으로 삼는다(만든 쪽은 더 고치지 않는다). 크기가 영역 사각형과 다르면 <see cref="ArgumentException"/>.
    /// </summary>
    public static FreeTransform Own(PixelImage image, PixelMask region) => new(region, CheckSize(image, region));

    private static PixelImage CheckSize(PixelImage image, PixelMask region)
    {
        if (region.Bounds.Width != image.Width || region.Bounds.Height != image.Height)
            throw new ArgumentException(Chickxel.Core.Localization.DomainStrings.Transform_RegionSizeMismatch, nameof(image));
        return image;
    }

    /// <summary>원본 영역을 감싸는 사각형(캔버스 좌표).</summary>
    public Rectangle SourceBounds => _region.Bounds;

    /// <summary>원본 사각형의 변형 상자(손잡이·값의 대응).</summary>
    public TransformBox Box => new(SourceBounds);

    /// <summary>
    /// 원본 사각형을 <paramref name="bounds"/>로 바꾼 새 원본(여백): 좁히면 밖 픽셀을 버리고, 넓히면 <paramref name="extendEdges"/>면 가장자리 픽셀(과 모양)을
    /// 바깥으로 복사해 채우고 아니면 비워 둔다. 원래 픽셀은 같은 좌표에 그대로 있다.
    /// </summary>
    public FreeTransform Reframe(Rectangle bounds, bool extendEdges)
    {
        if (bounds == SourceBounds) return this;
        var (image, region) = Fit(_image, _region, bounds, extendEdges);
        return Own(image, region);
    }

    /// <summary>
    /// 모양 <paramref name="keep"/>(캔버스 좌표) 밖 칸을 원본에서 뺀 새 원본. 원본 사각형(<see cref="SourceBounds"/>)은 그대로라 같은 값으로 그리면 같은 자리에 놓인다.
    /// 그림은 복사하지 않고 함께 쓴다. 빠지는 칸이 없으면 이것.
    /// </summary>
    public FreeTransform Clip(PixelMask keep)
    {
        var b = SourceBounds;
        var bits = new bool[b.Width * b.Height];
        bool changed = false;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
            {
                bool inside = _region.Contains(b.X + x, b.Y + y);
                bool kept = inside && keep.Contains(b.X + x, b.Y + y);
                changed |= inside != kept;
                bits[y * b.Width + x] = kept;
            }
        return changed ? new FreeTransform(new PixelMask(b, bits), _image) : this;
    }

    /// <summary>
    /// <paramref name="image"/>(크기 = <paramref name="region"/> 사각형)를 <paramref name="bounds"/> 사각형에 맞춘 그림과 모양: 겹치는 칸은 그대로, 넓힌 칸은
    /// <paramref name="extendEdges"/>면 가장 가까운 가장자리 칸(색과 모양)을 복사하고 아니면 비운다. 빈 원본이면 모두 빈 칸.
    /// </summary>
    public static (PixelImage Image, PixelMask Region) Fit(PixelImage image, PixelMask region, Rectangle bounds, bool extendEdges)
    {
        var src = region.Bounds;
        var dst = new PixelImage(bounds.Width, bounds.Height);
        var bits = new bool[bounds.Width * bounds.Height];
        bool all = true;
        bool empty = src.Width <= 0 || src.Height <= 0;
        for (int y = 0; y < bounds.Height; y++)
            for (int x = 0; x < bounds.Width; x++)
            {
                int cx = bounds.X + x, cy = bounds.Y + y;
                if (!empty && extendEdges)
                {
                    cx = Math.Clamp(cx, src.Left, src.Right - 1);
                    cy = Math.Clamp(cy, src.Top, src.Bottom - 1);
                }
                bool inside = !empty && region.Contains(cx, cy);
                int i = y * bounds.Width + x;
                bits[i] = inside;
                all &= inside;
                if (inside) dst.Pixels[i] = image.Pixels[(cy - src.Y) * src.Width + (cx - src.X)];
            }
        return (dst, all ? new PixelMask(bounds) : new PixelMask(bounds, bits));
    }

    /// <summary>
    /// 변형한 픽셀과 영역. 영역은 들어간 칸을 꼭 맞게 감싸고(모두 차면 비트 없는 사각형), 들어간 칸이 하나도 없으면
    /// 변형 중심 칸 하나짜리 빈 영역(비트 모두 꺼짐)과 투명 1×1 그림.
    /// </summary>
    public TransformResult Render(TransformParams p, ResampleMode mode)
    {
        var box = Box;
        // 변형한 사각형을 감싸는 대상 픽셀 범위
        var corners = box.Corners(p);
        double minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X);
        double minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y);
        if (!double.IsFinite(minX + maxX + minY + maxY)) return EmptyResult(p);
        int left = (int)Math.Floor(minX), top = (int)Math.Floor(minY);
        int right = (int)Math.Ceiling(maxX), bottom = (int)Math.Ceiling(maxY);
        return RenderRange(box, p, mode, new Rectangle(left, top, right - left, bottom - top));
    }

    /// <summary>
    /// 원본 사각형이 아닌 기준 상자 <paramref name="box"/>의 값 <paramref name="p"/>가 정하는 대응으로 원본 전체를 그린다(영역·결과는 <see cref="Render(TransformParams, ResampleMode)"/>와 같음).
    /// <see cref="TransformBox.ParamsFor"/>로 원본 상자의 값을 따로 구하지 않으므로 원본이 원근의 지평선을 넘어도 같은 대응 그대로이고 지평선 너머 칸만 빠진다.
    /// 대상은 <paramref name="clip"/> 안만 계산하고, 범위가 넓으면 띠로 나눠 계산해 합친다(지평선 가까이에서 끝없이 늘어난 그림도 clip 안은 남음).
    /// </summary>
    public TransformResult Render(TransformBox box, TransformParams p, ResampleMode mode, Rectangle clip)
    {
        var src = SourceBounds;
        var range = clip;
        // 원본 네 꼭짓점이 모두 지평선 이쪽이면 옮긴 볼록 사각형을 감싸는 범위로 줄인다(아니면 옮긴 그림이 끝없이 뻗으므로 clip 전체).
        (double X, double Y)[] corners = [(src.Left, src.Top), (src.Right, src.Top), (src.Right, src.Bottom), (src.Left, src.Bottom)];
        if (corners.All(c => box.InFront(p, c.X, c.Y)))
        {
            for (int i = 0; i < corners.Length; i++) corners[i] = box.Map(p, corners[i].X, corners[i].Y);
            double minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X);
            double minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y);
            if (!double.IsFinite(minX + maxX + minY + maxY)) return EmptyResult(p);
            // 클립 안으로 먼저 줄여 int로 넘치지 않게 한다.
            double l = Math.Max(Math.Floor(minX), clip.Left), t = Math.Max(Math.Floor(minY), clip.Top);
            double r = Math.Min(Math.Ceiling(maxX), clip.Right), b = Math.Min(Math.Ceiling(maxY), clip.Bottom);
            if (r <= l || b <= t) return EmptyResult(p);
            range = Rectangle.FromLTRB((int)l, (int)t, (int)r, (int)b);
        }
        if (range.Width <= 0 || range.Height <= 0) return EmptyResult(p);
        int rows = (int)Math.Clamp(BandArea / range.Width, 1, range.Height);
        if (rows == range.Height) return RenderRange(box, p, mode, range);

        var bands = new List<TransformResult>();
        for (int y = range.Top; y < range.Bottom; y += rows)
        {
            var band = RenderRange(box, p, mode, Rectangle.FromLTRB(range.Left, y, range.Right, Math.Min(y + rows, range.Bottom)));
            if (!band.Region.IsEmpty) bands.Add(band);
        }
        if (bands.Count == 0) return EmptyResult(p);
        if (bands.Count == 1) return bands[0];
        var union = bands.Select(bd => bd.Region.Bounds).Aggregate(Rectangle.Union);
        var image = new PixelImage(union.Width, union.Height);
        var bits = new bool[union.Width * union.Height];
        foreach (var (bandImage, region) in bands)
        {
            var rb = region.Bounds;
            for (int y = 0; y < rb.Height; y++)
                for (int x = 0; x < rb.Width; x++)
                {
                    if (!region.Contains(rb.X + x, rb.Y + y)) continue;
                    int i = (rb.Y + y - union.Y) * union.Width + rb.X + x - union.X;
                    image.Pixels[i] = bandImage.Pixels[y * rb.Width + x];
                    bits[i] = true;
                }
        }
        return new TransformResult(image, bits.All(v => v) ? new PixelMask(union) : new PixelMask(union, bits));
    }

    /// <summary>넓은 대상 범위를 나눠 계산하는 띠 하나의 넓이 한도(픽셀) — 계산용 임시 배열 크기를 묶는다.</summary>
    private const long BandArea = 1 << 22;

    /// <summary>
    /// 대상 픽셀 범위 <paramref name="range"/>의 칸마다 그 중심을 기준 상자 <paramref name="box"/>의 값 <paramref name="p"/>로 원본에 되돌려 집는다.
    /// 집은 칸을 꼭 맞게 감싸도록 줄인 결과(없거나 범위가 너무 크면 빈 결과).
    /// </summary>
    private TransformResult RenderRange(TransformBox box, TransformParams p, ResampleMode mode, Rectangle range)
    {
        var src = SourceBounds;
        int w = src.Width, h = src.Height;
        bool distorted = p.IsDistorted;
        bool rotSprite = mode == ResampleMode.RotSprite && (distorted || !p.IsAxisAligned) && (long)w * h <= MaxRotSpriteArea;

        int left = range.X, top = range.Y, bw = range.Width, bh = range.Height;
        if (bw <= 0 || bh <= 0 || (long)bw * bh > (long)PixelImage.MaxSize * PixelImage.MaxSize / 4) return EmptyResult(p);
        if (rotSprite) EnsureUpscaled();

        var colors = new Color32[bw * bh];
        var bits = new bool[bw * bh];
        int minOx = int.MaxValue, minOy = int.MaxValue, maxOx = -1, maxOy = -1;
        var (sx0, sy0) = box.Center;
        var basis = box.Bounds;
        // 기준 상자 왼쪽 위 기준 좌표 → 원본 사각형 왼쪽 위 기준 좌표로 옮기는 양(기준 상자가 원본이면 0)
        double shiftX = basis.X - src.X, shiftY = basis.Y - src.Y;
        var (sin, cos) = p.SinCos();
        double isx = 1 / p.ScaleX, isy = 1 / p.ScaleY;
        int w8 = w * 8, h8 = h * 8;
        // 찌그러뜨림: 기준 상자 왼쪽 위 기준 좌표에 바로 쓰도록 단위 상자 크기를 앞뒤로 곱한 역 호모그래피(한 번만 구함).
        var k = distorted ? Homography.Of(p.Distort).Inverse() : default;
        if (distorted)
        {
            double kw = basis.Width, kh = basis.Height;
            k = new Homography(k.M00, k.M01 * kw / kh, k.M02 * kw, k.M10 * kh / kw, k.M11, k.M12 * kh, k.M20 / kw, k.M21 / kh, k.M22);
        }
        for (int oy = 0; oy < bh; oy++)
        {
            double dy = top + oy + 0.5 - p.CenterY;
            for (int ox = 0; ox < bw; ox++)
            {
                double dx = left + ox + 0.5 - p.CenterX;
                // 기준 상자 왼쪽 위 기준 연속 좌표
                double lx = sx0 + (dx * cos + dy * sin) * isx - basis.X;
                double ly = sy0 + (-dx * sin + dy * cos) * isy - basis.Y;
                if (distorted)
                {
                    var (hx, hy, hw) = k.ApplyHomogeneous(lx, ly);
                    if (!(hw > 0)) continue;   // 원근의 지평선 너머: 원본이 없다
                    lx = hx / hw;
                    ly = hy / hw;
                }
                // 원본 사각형 왼쪽 위 기준으로
                lx += shiftX;
                ly += shiftY;
                Color32 c;
                if (rotSprite)
                {
                    double ux = Math.Floor(lx * 8), uy = Math.Floor(ly * 8);
                    if (!(ux >= 0 && uy >= 0 && ux < w8 && uy < h8)) continue;
                    int key = _up8![(int)uy * w8 + (int)ux];
                    if (key == 0) continue;
                    c = _palette![key];
                }
                else
                {
                    double fx = Math.Floor(lx), fy = Math.Floor(ly);
                    if (!(fx >= 0 && fy >= 0 && fx < w && fy < h)) continue;
                    int ix = (int)fx, iy = (int)fy;
                    if (!_region.Contains(src.X + ix, src.Y + iy)) continue;
                    c = _image.Pixels[iy * w + ix];
                }
                int i = oy * bw + ox;
                colors[i] = c;
                bits[i] = true;
                if (ox < minOx) minOx = ox;
                if (ox > maxOx) maxOx = ox;
                if (oy < minOy) minOy = oy;
                if (oy > maxOy) maxOy = oy;
            }
        }
        if (maxOx < 0) return EmptyResult(p);

        // 들어간 칸을 꼭 맞게 감싸도록 줄인다.
        int tw = maxOx - minOx + 1, th = maxOy - minOy + 1;
        var image = new PixelImage(tw, th);
        var tight = new bool[tw * th];
        bool all = true;
        for (int y = 0; y < th; y++)
            for (int x = 0; x < tw; x++)
            {
                int s = (minOy + y) * bw + minOx + x;
                image.Pixels[y * tw + x] = colors[s];
                tight[y * tw + x] = bits[s];
                all &= bits[s];
            }
        var rect = new Rectangle(left + minOx, top + minOy, tw, th);
        return new TransformResult(image, all ? new PixelMask(rect) : new PixelMask(rect, tight));
    }

    private static TransformResult EmptyResult(TransformParams p)
    {
        int x = double.IsFinite(p.CenterX) ? (int)Math.Floor(p.CenterX) : 0;
        int y = double.IsFinite(p.CenterY) ? (int)Math.Floor(p.CenterY) : 0;
        return new TransformResult(new PixelImage(1, 1), new PixelMask(new Rectangle(x, y, 1, 1), [false]));
    }

    /// <summary>RotSprite 8배 그림을 만든다: 칸마다 (모양 안, 색)을 팔레트 번호로 바꿔 Scale2x를 세 번.</summary>
    private void EnsureUpscaled()
    {
        if (_up8 is not null) return;
        var src = SourceBounds;
        int w = src.Width, h = src.Height;
        var keys = new int[w * h];
        var palette = new List<Color32> { Color32.Transparent };   // 0 = 모양 밖
        var index = new Dictionary<Color32, int>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!_region.Contains(src.X + x, src.Y + y)) continue;
                var c = _image.Pixels[y * w + x];
                if (!index.TryGetValue(c, out int k))
                {
                    k = palette.Count;
                    palette.Add(c);
                    index[c] = k;
                }
                keys[y * w + x] = k;
            }
        for (int i = 0; i < 3; i++)
        {
            keys = Scale2x(keys, w, h);
            w *= 2;
            h *= 2;
        }
        _up8 = keys;
        _palette = palette.ToArray();
    }

    /// <summary>Scale2x(EPX)로 2배 키운다. 새 색은 생기지 않는다(이웃 색 중 하나를 고름). 가장자리 밖은 가장자리 칸과 같다고 본다.</summary>
    public static PixelImage Scale2x(PixelImage src)
    {
        var index = new Dictionary<Color32, int>();
        var palette = new List<Color32>();
        var keys = new int[src.Pixels.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            var c = src.Pixels[i];
            if (!index.TryGetValue(c, out int k))
            {
                k = palette.Count;
                palette.Add(c);
                index[c] = k;
            }
            keys[i] = k;
        }
        var up = Scale2x(keys, src.Width, src.Height);
        var dst = new PixelImage(src.Width * 2, src.Height * 2);
        for (int i = 0; i < up.Length; i++) dst.Pixels[i] = palette[up[i]];
        return dst;
    }

    /// <summary>
    /// Scale2x 한 번(값이 같은지만 봄). 칸 E의 위 B·왼쪽 D·오른쪽 F·아래 H에 대해 B≠H이고 D≠F이면
    /// E0 = D==B ? D : E, E1 = B==F ? F : E, E2 = D==H ? D : E, E3 = H==F ? F : E.
    /// </summary>
    private static int[] Scale2x(int[] src, int w, int h)
    {
        int w2 = w * 2;
        var dst = new int[w2 * h * 2];
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            int up = y > 0 ? row - w : row, down = y < h - 1 ? row + w : row;
            for (int x = 0; x < w; x++)
            {
                int e = src[row + x];
                int b = src[up + x], hh = src[down + x];
                int d = src[row + (x > 0 ? x - 1 : x)], f = src[row + (x < w - 1 ? x + 1 : x)];
                int e0 = e, e1 = e, e2 = e, e3 = e;
                if (b != hh && d != f)
                {
                    if (d == b) e0 = d;
                    if (b == f) e1 = f;
                    if (d == hh) e2 = d;
                    if (hh == f) e3 = f;
                }
                int o = y * 2 * w2 + x * 2;
                dst[o] = e0;
                dst[o + 1] = e1;
                dst[o + w2] = e2;
                dst[o + w2 + 1] = e3;
            }
        }
        return dst;
    }
}
