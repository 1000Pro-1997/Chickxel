using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

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
/// </summary>
public readonly record struct TransformParams(double CenterX, double CenterY, double Angle, double ScaleX, double ScaleY)
{
    /// <summary>원본 그대로 두는 값(중심 = <paramref name="source"/> 중심, 0°, 100%).</summary>
    public static TransformParams Identity(Rectangle source)
        => new(source.X + source.Width / 2.0, source.Y + source.Height / 2.0, 0, 1, 1);

    /// <summary>각도가 90°의 배수인지(축에 맞는 변형: 회전해도 픽셀 격자가 그대로 맞는다).</summary>
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

/// <summary>변형 결과: 떠 있는 픽셀(크기 = 영역 사각형 크기, 영역 밖은 투명)과 그 영역(캔버스 좌표).</summary>
public readonly record struct TransformResult(PixelImage Image, PixelMask Region);

/// <summary>
/// 자유 변형(회전·크기·옮기기): 떼어 낸 원본 픽셀과 모양을 갖고 있다가 <see cref="TransformParams"/>마다 새로 그린다.
/// 늘 원본에서 다시 그리므로(누적하지 않음) 원래 값이면 원본과 똑같고, 정수 픽셀 옮기기는 무손실이다.
/// <para>
/// 대상 픽셀마다 그 중심을 원본으로 되돌려(역변환) 떨어지는 원본 칸을 집는다. RotSprite는 원본을 Scale2x로 세 번 키운
/// 8배 그림(처음 쓸 때 한 번 만듦)에서 집는다. 모양 밖 칸은 집지 않으므로 결과에서도 투명하고 영역에서 빠진다.
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
    public FreeTransform(PixelImage image, PixelMask region)
    {
        if (region.Bounds.Width != image.Width || region.Bounds.Height != image.Height)
            throw new ArgumentException(Gpixel.Core.Localization.DomainStrings.Transform_RegionSizeMismatch, nameof(image));
        _image = image.Clone();
        _region = region;
    }

    /// <summary>원본 영역을 감싸는 사각형(캔버스 좌표).</summary>
    public Rectangle SourceBounds => _region.Bounds;

    /// <summary>원본 그대로 두는 값.</summary>
    public TransformParams Identity => TransformParams.Identity(SourceBounds);

    /// <summary>원본 중심(연속 좌표).</summary>
    public (double X, double Y) SourceCenter => (SourceBounds.X + SourceBounds.Width / 2.0, SourceBounds.Y + SourceBounds.Height / 2.0);

    /// <summary>원본 좌표(연속) → 변형한 캔버스 좌표.</summary>
    public (double X, double Y) Map(TransformParams p, double x, double y)
    {
        var (sx0, sy0) = SourceCenter;
        double lx = (x - sx0) * p.ScaleX, ly = (y - sy0) * p.ScaleY;
        var (sin, cos) = p.SinCos();
        return (p.CenterX + lx * cos - ly * sin, p.CenterY + lx * sin + ly * cos);
    }

    /// <summary>캔버스 좌표 → 원본 좌표(연속, <see cref="Map"/>의 반대). 배율이 0이면 NaN.</summary>
    public (double X, double Y) Unmap(TransformParams p, double x, double y)
    {
        var (sx0, sy0) = SourceCenter;
        double dx = x - p.CenterX, dy = y - p.CenterY;
        var (sin, cos) = p.SinCos();
        double lx = dx * cos + dy * sin, ly = -dx * sin + dy * cos;
        return (sx0 + lx / p.ScaleX, sy0 + ly / p.ScaleY);
    }

    /// <summary>
    /// 다른 원본 <paramref name="other"/>을 이 변형의 값 <paramref name="p"/>와 똑같은 행렬(같은 각도·배율, 같은 자리로 옮김)로 그리는 값.
    /// <paramref name="other"/>의 좌표 + (<paramref name="dx"/>, <paramref name="dy"/>) = 이 변형의 좌표(다른 레이어·부위의 셀을 기준 공간에 맞출 때).
    /// 원본 중심이 달라도 결과는 같은 자리를 기준으로 돌고 커진다. <paramref name="other"/>가 자신이고 차이가 0이면 <paramref name="p"/> 그대로.
    /// </summary>
    public TransformParams ParamsFor(FreeTransform other, TransformParams p, double dx = 0, double dy = 0)
    {
        var (ox, oy) = other.SourceCenter;
        var (cx, cy) = Map(p, ox + dx, oy + dy);
        return p with { CenterX = cx - dx, CenterY = cy - dy };
    }

    /// <summary>변형한 원본 사각형의 네 꼭짓점(캔버스 좌표): 원본의 왼쪽 위, 오른쪽 위, 오른쪽 아래, 왼쪽 아래 순.</summary>
    public (double X, double Y)[] Corners(TransformParams p)
    {
        var r = SourceBounds;
        return [Map(p, r.Left, r.Top), Map(p, r.Right, r.Top), Map(p, r.Right, r.Bottom), Map(p, r.Left, r.Bottom)];
    }

    /// <summary>
    /// 변형한 픽셀과 영역. 영역은 들어간 칸을 꼭 맞게 감싸고(모두 차면 비트 없는 사각형), 들어간 칸이 하나도 없으면
    /// 변형 중심 칸 하나짜리 빈 영역(비트 모두 꺼짐)과 투명 1×1 그림.
    /// </summary>
    public TransformResult Render(TransformParams p, ResampleMode mode)
    {
        var src = SourceBounds;
        int w = src.Width, h = src.Height;
        bool rotSprite = mode == ResampleMode.RotSprite && !p.IsAxisAligned && (long)w * h <= MaxRotSpriteArea;
        if (rotSprite) EnsureUpscaled();

        // 변형한 사각형을 감싸는 대상 픽셀 범위
        var corners = Corners(p);
        double minX = corners.Min(c => c.X), maxX = corners.Max(c => c.X);
        double minY = corners.Min(c => c.Y), maxY = corners.Max(c => c.Y);
        if (!double.IsFinite(minX + maxX + minY + maxY)) return EmptyResult(p);
        int left = (int)Math.Floor(minX), top = (int)Math.Floor(minY);
        int right = (int)Math.Ceiling(maxX), bottom = (int)Math.Ceiling(maxY);
        int bw = right - left, bh = bottom - top;
        if (bw <= 0 || bh <= 0 || (long)bw * bh > (long)PixelImage.MaxSize * PixelImage.MaxSize / 4) return EmptyResult(p);

        var colors = new Color32[bw * bh];
        var bits = new bool[bw * bh];
        int minOx = int.MaxValue, minOy = int.MaxValue, maxOx = -1, maxOy = -1;
        var (sx0, sy0) = SourceCenter;
        var (sin, cos) = p.SinCos();
        double isx = 1 / p.ScaleX, isy = 1 / p.ScaleY;
        int w8 = w * 8, h8 = h * 8;
        for (int oy = 0; oy < bh; oy++)
        {
            double dy = top + oy + 0.5 - p.CenterY;
            for (int ox = 0; ox < bw; ox++)
            {
                double dx = left + ox + 0.5 - p.CenterX;
                // 원본 사각형 왼쪽 위 기준 연속 좌표
                double lx = sx0 + (dx * cos + dy * sin) * isx - src.X;
                double ly = sy0 + (-dx * sin + dy * cos) * isy - src.Y;
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
