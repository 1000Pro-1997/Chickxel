using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>
/// 픽셀 영역: <see cref="Bounds"/> 사각형 안에서 칸마다 들어가는지(비트). 비트가 없으면 사각형 전체.
/// 선택 영역(사각·원·자유·스마트 선택)과 그리기 제한(<see cref="Raster.Plot"/> 등의 clip)에 쓴다.
/// <see cref="Rectangle"/>은 그대로 사각형 영역으로 바뀐다. 기본값은 빈 영역이다.
/// </summary>
public readonly struct PixelMask
{
    private readonly bool[]? _bits;

    /// <param name="bounds">영역을 감싸는 사각형(이미지 좌표).</param>
    /// <param name="bits">행 우선 <c>Width×Height</c> 칸(null = 사각형 전체). 복사하지 않고 그대로 쓴다.</param>
    public PixelMask(Rectangle bounds, bool[]? bits = null)
    {
        if (bounds.Width < 0 || bounds.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(bounds), Chickxel.Core.Localization.DomainStrings.Mask_NegativeSize);
        if (bits is not null && bits.Length != bounds.Width * bounds.Height)
            throw new ArgumentException(Chickxel.Core.Localization.DomainStrings.Mask_BitSizeMismatch, nameof(bits));
        Bounds = bounds;
        _bits = bits;
    }

    /// <summary>영역을 감싸는 사각형. 비었으면 크기 0.</summary>
    public Rectangle Bounds { get; }

    /// <summary>사각형 전체인지(칸 비트 없음).</summary>
    public bool IsRectangle => _bits is null;

    /// <summary>행 우선 칸 비트(사각형 전체면 null). 같은 어셈블리의 선택 영역이 그대로 넘겨 쓴다.</summary>
    internal bool[]? Bits => _bits;

    /// <summary>들어간 칸 수.</summary>
    public int Count
    {
        get
        {
            if (_bits is null) return Bounds.Width * Bounds.Height;
            int n = 0;
            foreach (bool b in _bits) if (b) n++;
            return n;
        }
    }

    public bool IsEmpty => Count == 0;

    public bool Contains(int x, int y)
        => Bounds.Contains(x, y) && (_bits is null || _bits[(y - Bounds.Y) * Bounds.Width + (x - Bounds.X)]);

    public bool Contains(Point p) => Contains(p.X, p.Y);

    /// <summary>(dx,dy)만큼 옮긴 같은 모양.</summary>
    public PixelMask Offset(int dx, int dy)
        => new(new Rectangle(Bounds.X + dx, Bounds.Y + dy, Bounds.Width, Bounds.Height), _bits);

    /// <summary>
    /// <paramref name="limit"/> 안으로 자르고 들어간 칸을 꼭 맞게 감싸도록 줄인다.
    /// 감싼 사각형의 모든 칸이 들어가면 비트 없는 사각형, 하나도 없으면 빈 영역.
    /// </summary>
    public PixelMask Trim(Rectangle limit)
    {
        var area = Rectangle.Intersect(Bounds, limit);
        if (area.Width <= 0 || area.Height <= 0) return default;
        if (_bits is null) return new PixelMask(area);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                if (!Contains(x, y)) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        if (maxX < minX) return default;
        var r = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        var bits = new bool[r.Width * r.Height];
        bool all = true;
        for (int y = 0; y < r.Height; y++)
            for (int x = 0; x < r.Width; x++)
            {
                bool on = Contains(r.X + x, r.Y + y);
                bits[y * r.Width + x] = on;
                all &= on;
            }
        return all ? new PixelMask(r) : new PixelMask(r, bits);
    }

    /// <summary>점들이 들어간 영역(중복 무시). 점이 없으면 빈 영역.</summary>
    public static PixelMask FromPoints(IEnumerable<Point> points)
    {
        var list = points as IReadOnlyCollection<Point> ?? points.ToList();
        if (list.Count == 0) return default;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in list)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        var r = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        var bits = new bool[r.Width * r.Height];
        foreach (var p in list) bits[(p.Y - r.Y) * r.Width + (p.X - r.X)] = true;
        return new PixelMask(r, bits);
    }

    /// <summary>사각형에 내접하는 채운 타원(원 선택, <see cref="Raster.EllipseFill"/>과 같은 모양).</summary>
    public static PixelMask Ellipse(Rectangle r) => FromPoints(Raster.EllipseFill(r));

    /// <summary>
    /// 닫힌 다각형(자유 선택): 꼭짓점은 픽셀 중심이고 마지막 점과 첫 점이 이어진다.
    /// 중심이 다각형 안(짝홀 규칙)인 픽셀과 변 위의 픽셀(브레젠험 선)이 들어간다 — 가는 올가미 선도 그린 칸은 선택된다.
    /// </summary>
    public static PixelMask Polygon(IReadOnlyList<Point> vertices)
    {
        if (vertices.Count == 0) return default;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var p in vertices)
        {
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }
        var r = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        var bits = new bool[r.Width * r.Height];
        int n = vertices.Count;
        var xs = new List<double>();
        for (int y = r.Top; y < r.Bottom; y++)
        {
            // 가로줄 y와 변의 교차점(아래 끝은 포함, 위 끝은 제외: 꼭짓점을 두 번 세지 않는다).
            xs.Clear();
            for (int i = 0; i < n; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % n];
                if (a.Y == b.Y) continue;
                if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
                    xs.Add(a.X + (y - a.Y) * (double)(b.X - a.X) / (b.Y - a.Y));
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int from = Math.Max(r.Left, (int)Math.Ceiling(xs[k]));
                int to = Math.Min(r.Right - 1, (int)Math.Floor(xs[k + 1]));
                for (int x = from; x <= to; x++) bits[(y - r.Y) * r.Width + (x - r.X)] = true;
            }
        }
        for (int i = 0; i < n; i++)
            foreach (var p in Raster.Line(vertices[i], vertices[(i + 1) % n]))
                bits[(p.Y - r.Y) * r.Width + (p.X - r.X)] = true;
        return new PixelMask(r, bits);
    }

    /// <summary>영역을 같은 크기 그림으로(들어간 칸은 불투명, 나머지는 투명). 뒤집기·회전을 그림과 똑같이 하려고 쓴다.</summary>
    internal PixelImage ToImage()
    {
        var img = new PixelImage(Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height));
        for (int i = 0; i < img.Pixels.Length; i++)
            if (_bits is null || _bits[i]) img.Pixels[i] = Color32.White;
        return img;
    }

    /// <summary><see cref="ToImage"/>의 반대: <paramref name="image"/>의 불투명 칸이 <paramref name="location"/>에 놓인 영역.</summary>
    internal static PixelMask FromImage(PixelImage image, Point location)
    {
        var bits = new bool[image.Pixels.Length];
        for (int i = 0; i < bits.Length; i++) bits[i] = image.Pixels[i].A != 0;
        return new PixelMask(new Rectangle(location, image.Size), bits);
    }

    public static implicit operator PixelMask(Rectangle r) => new(r);
}
