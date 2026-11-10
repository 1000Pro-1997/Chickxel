using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>모양 펜 세트에 모양이 여럿일 때 도장마다 다음 모양을 고르는 방법.</summary>
public enum StampOrder
{
    /// <summary>무작위(같은 모양이 잇달아 나오지 않음).</summary>
    Random = 0,
    /// <summary>세트 차례로(끝 다음은 처음).</summary>
    Sequential = 1,
    /// <summary>선택된 것: 고른 모양만 늘 찍는다.</summary>
    Selected = 2,
}

/// <summary>모양 펜이 찍을 다음 모양 번호를 고른다. 무작위는 씨앗을 주면 늘 같은 차례(시험용).</summary>
public sealed class StampPicker
{
    private readonly Random _random;

    public StampPicker(int? seed = null) => _random = seed is { } s ? new Random(s) : new Random();

    /// <summary>세트를 고른 직후의 첫 모양: 무작위면 아무거나, 그 밖에는 0.</summary>
    public int First(int count, StampOrder order)
        => count <= 1 || order != StampOrder.Random ? 0 : _random.Next(count);

    /// <summary>
    /// <paramref name="current"/>를 찍은 뒤의 다음 모양. 차례로면 다음 번호(끝 다음은 0), 선택된 것이면 그대로,
    /// 무작위면 <paramref name="current"/>를 뺀 나머지에서 고른다(모양이 하나면 늘 0).
    /// </summary>
    public int Next(int current, int count, StampOrder order)
    {
        if (count <= 1) return 0;
        if (order == StampOrder.Selected) return Math.Clamp(current, 0, count - 1);
        if (order == StampOrder.Sequential) return ((current % count) + count + 1) % count;
        if ((uint)current >= (uint)count) return _random.Next(count);
        int pick = _random.Next(count - 1);
        return pick >= current ? pick + 1 : pick;
    }
}

/// <summary>
/// 모양 펜 끌기: 포인터가 지난 선(브레젠험)의 점마다 다음 도장을 찍을지 물어(마지막 도장 자리, 후보 자리) 찍을 자리를 낸다.
/// 첫 도장(누른 곳)은 부르는 쪽이 찍는다. 간격은 <see cref="Spacing"/>(곧은 거리), 겹치지 않게는 <see cref="ShapeStamp.Overlaps"/>로 묻는다.
/// </summary>
public sealed class StampTrail
{
    private Point _end, _last;

    /// <param name="start">첫 도장 자리(누른 곳).</param>
    public StampTrail(Point start) => _end = _last = start;

    /// <summary>마지막 도장에서 <paramref name="spacing"/>(px, 1 이상으로 맞춤)만큼 떨어지면 찍는 물음. 1이면 선의 모든 점(이어진 자국).</summary>
    public static Func<Point, Point, bool> Spacing(int spacing)
    {
        long s = Math.Max(1, spacing);
        return (last, p) =>
        {
            long dx = p.X - last.X, dy = p.Y - last.Y;
            return dx * dx + dy * dy >= s * s;
        };
    }

    /// <summary>
    /// 지금 끝에서 <paramref name="to"/>까지 늘이며 도장 자리를 차례로 낸다. <paramref name="due"/>(마지막 도장 자리, 후보 자리)는 점마다 다시 물으므로
    /// 앞 도장에 따라 답이 달라져도 된다 — 낸 자리에 곧바로 도장을 찍으며 돌아야 한다.
    /// </summary>
    public IEnumerable<Point> MoveTo(Point to, Func<Point, Point, bool> due)
    {
        if (to == _end) yield break;
        var line = Raster.Line(_end, to);
        _end = to;
        for (int i = 1; i < line.Count; i++)
        {
            var p = line[i];
            if (!due(_last, p)) continue;
            _last = p;
            yield return p;
        }
    }
}

/// <summary>모양 펜 도장 하나: 자리 잡기, 픽셀(알파 포함), 지금 색으로 물들이기, 지우기.</summary>
public static class ShapeStamp
{
    /// <summary>
    /// 도장 가운데가 <paramref name="center"/>일 때 모양의 왼쪽 위(정수 픽셀). 브러시와 같이 짝수 크기는 가운데의 오른쪽·아래로 한 칸 더 넓다.
    /// </summary>
    public static Point Origin(Point center, int width, int height)
        => new(center.X - (width - 1) / 2, center.Y - (height - 1) / 2);

    /// <summary>가운데 <paramref name="center"/>에 놓은 모양의 투명하지 않은 픽셀(캔버스 좌표)과 그 색.</summary>
    public static IEnumerable<(Point At, Color32 Color)> Pixels(PixelImage shape, Point center)
    {
        var o = Origin(center, shape.Width, shape.Height);
        var px = shape.Pixels;
        for (int y = 0; y < shape.Height; y++)
            for (int x = 0; x < shape.Width; x++)
            {
                var c = px[y * shape.Width + x];
                if (c.A != 0) yield return (new Point(o.X + x, o.Y + y), c);
            }
    }

    /// <summary>
    /// 가운데 <paramref name="ca"/>에 놓은 <paramref name="a"/>와 <paramref name="cb"/>에 놓은 <paramref name="b"/>의 투명하지 않은 픽셀이 한 칸이라도 겹치는지
    /// (투명한 둘레는 셈하지 않음). 최적화 간격 끌기가 겹치지 않는 첫 자리를 찾을 때 쓴다.
    /// </summary>
    public static bool Overlaps(PixelImage a, Point ca, PixelImage b, Point cb)
    {
        var oa = Origin(ca, a.Width, a.Height);
        var ob = Origin(cb, b.Width, b.Height);
        var both = Rectangle.Intersect(new Rectangle(oa, a.Size), new Rectangle(ob, b.Size));
        if (both.Width <= 0 || both.Height <= 0) return false;
        for (int y = both.Top; y < both.Bottom; y++)
        {
            int rowA = (y - oa.Y) * a.Width - oa.X, rowB = (y - ob.Y) * b.Width - ob.X;
            for (int x = both.Left; x < both.Right; x++)
                if (a.Pixels[rowA + x].A != 0 && b.Pixels[rowB + x].A != 0) return true;
        }
        return false;
    }

    /// <summary>
    /// 모양을 <paramref name="target"/>에 찍는다(알파 합성 — 투명한 칸은 아래를 그대로, 반투명은 섞음). <paramref name="clip"/> 밖·이미지 밖은 무시.
    /// <paramref name="erase"/>면 대신 모양의 불투명도만큼 아래를 지운다.
    /// </summary>
    public static void Stamp(PixelImage target, PixelImage shape, Point center, PixelMask? clip = null, bool erase = false)
    {
        foreach (var (at, c) in Pixels(shape, center))
        {
            if (!target.Contains(at.X, at.Y) || clip is { } m && !m.Contains(at)) continue;
            var below = target.Get(at.X, at.Y);
            target.Set(at.X, at.Y, erase ? Erase(below, c.A) : Color32.BlendOver(below, c));
        }
    }

    /// <summary>아래 색 <paramref name="below"/>를 불투명도 <paramref name="alpha"/>만큼 지운 색(255 = 투명).</summary>
    public static Color32 Erase(Color32 below, byte alpha)
    {
        if (alpha == 255 || below.A == 0) return Color32.Transparent;
        int a = (below.A * (255 - alpha) + 127) / 255;
        return a == 0 ? Color32.Transparent : below.WithAlpha((byte)a);
    }

    /// <summary>
    /// 모양을 <paramref name="color"/>로 물들인 사본. 색상은 모두 <paramref name="color"/>의 색상이 되고, 모양에서 가장 많은 색(기준)이 <paramref name="color"/>가 되며
    /// 다른 픽셀은 기준과의 채도 비·명도 비를 지켜 명암(테두리·밝은 곳)이 남는다(색상 차는 버림 — 줄기 같은 다른 색 부분이 엉뚱한 색이 되지 않게).
    /// 무채색 기준(회색 모양)은 채도를 <paramref name="color"/>에서 그대로 쓴다. 알파 = 픽셀 알파 × <paramref name="color"/> 알파.
    /// </summary>
    public static PixelImage Tint(PixelImage shape, Color32 color)
    {
        var result = new PixelImage(shape.Width, shape.Height);
        var reference = MostCommon(shape);
        if (reference is not { } r) return result;
        var (_, s0, v0) = r.ToHsv();
        var (th, ts, tv) = color.ToHsv();
        var src = shape.Pixels;
        var dst = result.Pixels;
        for (int i = 0; i < src.Length; i++)
        {
            var c = src[i];
            if (c.A == 0) continue;
            var (_, s, v) = c.ToHsv();
            float sat = s0 > 0 ? ts * s / s0 : ts;
            float val = v0 > 0 ? tv * v / v0 : v;
            dst[i] = Color32.FromHsv(th, sat, val, (byte)((c.A * color.A + 127) / 255));
        }
        return result;
    }

    /// <summary>투명하지 않은 픽셀 중 가장 많은 색(알파 무시, 같으면 먼저 그 수에 닿은 색). 모두 투명이면 null.</summary>
    private static Color32? MostCommon(PixelImage shape)
    {
        var counts = new Dictionary<uint, int>();
        uint best = 0;
        int bestCount = 0;
        foreach (var c in shape.Pixels)
        {
            if (c.A == 0) continue;
            uint rgb = c.Argb | 0xFF000000u;
            int n = counts.TryGetValue(rgb, out var k) ? k + 1 : 1;
            counts[rgb] = n;
            if (n > bestCount) { best = rgb; bestCount = n; }
        }
        return bestCount == 0 ? null : new Color32(best);
    }
}
