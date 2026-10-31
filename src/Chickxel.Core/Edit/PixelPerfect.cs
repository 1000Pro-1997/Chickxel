using System.Drawing;

namespace Chickxel.Core.Edit;

/// <summary>
/// 픽셀 퍼펙트 연필: 1픽셀 선에서 계단 모서리에 생기는 'L'자 겹 픽셀을 없앤다.
/// 연속한 세 점 a-b-c에서 b가 a·c와 각각 상하좌우로 붙어 있고 a와 c가 대각선이면 b를 뺀다.
/// </summary>
public static class PixelPerfect
{
    /// <summary>완성된 경로를 한 번에 거른다(연속 중복 점도 제거).</summary>
    public static List<Point> Filter(IEnumerable<Point> path)
    {
        var stroke = new PixelPerfectStroke();
        foreach (var p in path) stroke.Add(p);
        return stroke.Points.ToList();
    }
}

/// <summary>
/// 그리는 중에 점을 하나씩 받아 픽셀 퍼펙트를 적용한다. 마우스 사이 구간은 <see cref="Raster.Line"/>으로
/// 채운 뒤 점마다 <see cref="Add"/>를 부른다.
/// </summary>
public sealed class PixelPerfectStroke
{
    private readonly List<Point> _points = new();

    /// <summary>현재까지 남은(칠해야 할) 점들.</summary>
    public IReadOnlyList<Point> Points => _points;

    /// <summary>
    /// 점을 추가한다. 결과의 <c>Draw</c>는 새로 칠할 점(같은 점이 반복되면 null),
    /// <c>Restore</c>는 L자 모서리라서 빠진 점(스트로크 시작 전 픽셀로 되돌려야 함, 없으면 null).
    /// </summary>
    public (Point? Draw, Point? Restore) Add(Point p)
    {
        if (_points.Count > 0 && _points[^1] == p) return (null, null);
        _points.Add(p);
        if (_points.Count < 3) return (p, null);
        var a = _points[^3];
        var b = _points[^2];
        var c = _points[^1];
        bool abAdjacent = (a.X == b.X || a.Y == b.Y) && Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) == 1;
        bool bcAdjacent = (b.X == c.X || b.Y == c.Y) && Math.Abs(b.X - c.X) + Math.Abs(b.Y - c.Y) == 1;
        bool acDiagonal = Math.Abs(a.X - c.X) == 1 && Math.Abs(a.Y - c.Y) == 1;
        if (abAdjacent && bcAdjacent && acDiagonal && !ContainsBefore(b, _points.Count - 2))
        {
            _points.RemoveAt(_points.Count - 2);
            return (p, b);
        }
        return (p, null);
    }

    /// <summary>점 b가 스트로크 앞부분(지울 수 없는 곳)에 이미 칠해졌는지.</summary>
    private bool ContainsBefore(Point b, int index)
    {
        for (int i = 0; i < index; i++)
            if (_points[i] == b) return true;
        return false;
    }

    public void Reset() => _points.Clear();
}
