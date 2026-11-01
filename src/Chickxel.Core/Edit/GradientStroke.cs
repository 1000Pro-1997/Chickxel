using System.Drawing;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>그라데이션 연필이 획을 따라 그라데이션 위치를 정하는 방법.</summary>
public enum GradientStrokeMode
{
    /// <summary>획 전체: 시작(0) → 끝(1)을 획 길이(그린 길을 따라 잰 거리)로 나눈다. 손을 떼면 끝 색이 정해진다.</summary>
    WholeStroke = 0,
    /// <summary>반복: 정한 간격(px)마다 그라데이션이 처음부터 다시 돈다.</summary>
    Repeat = 1,
}

/// <summary>
/// 그라데이션 연필 한 획. 포인터가 지난 점들을 꺾은선으로 잇고(<see cref="LineTo"/>), 그 사이 브러시 중심(브레젠험 선 점)마다
/// 획 시작에서 꺾은선을 따라 잰 거리(호 길이, 곧은 거리가 아님)를 매긴다. 칠한 픽셀마다 그 픽셀을 마지막으로 덮은 중심의 거리를
/// 기억하고(<see cref="Record"/>), <see cref="Render"/>가 거리 → 위치 t(0~1) → 그라데이션 색으로 칠한다.
/// 색은 연필처럼 픽셀을 그대로 바꾼다(알파 포함 — 투명한 정지점 쪽은 아래를 지운다).
/// </summary>
public sealed class GradientStroke
{
    public const int MinInterval = 2;
    public const int MaxInterval = 512;
    public const int DefaultInterval = 16;

    private readonly Dictionary<Point, double> _pixels = new();
    private Point _end;

    /// <param name="start">첫 브러시 중심(거리 0). 이 점의 도장은 부르는 쪽이 거리 0으로 기록한다.</param>
    public GradientStroke(Point start) => _end = start;

    /// <summary>지금까지 그린 길이(px, 꺾은선을 따라).</summary>
    public double Length { get; private set; }

    /// <summary>칠한 픽셀과 그 픽셀의 획 위 거리.</summary>
    public IReadOnlyDictionary<Point, double> Pixels => _pixels;

    /// <summary>
    /// 지금 끝에서 <paramref name="to"/>까지 꺾은선을 늘이고, 그 구간의 브러시 중심(브레젠험 선, 지금 끝 점은 뺌)과 거리를 돌려준다.
    /// 중심의 거리 = 구간 앞까지의 길이 + 중심을 구간에 투영한 거리(구간 안으로 자름)라서 계단 모양 브레젠험 점에서도 길이가 부풀지 않는다.
    /// </summary>
    public List<(Point Center, double Distance)> LineTo(Point to)
    {
        var centers = new List<(Point, double)>();
        if (to == _end) return centers;
        var from = _end;
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        var line = Raster.Line(from, to);
        for (int i = 1; i < line.Count; i++)
        {
            var p = line[i];
            double along = ((p.X - from.X) * dx + (p.Y - from.Y) * dy) / length;
            centers.Add((p, Length + Math.Clamp(along, 0, length)));
        }
        Length += length;
        _end = to;
        return centers;
    }

    /// <summary>한 도장이 덮은 픽셀들(대칭 점 포함)에 거리 <paramref name="distance"/>를 기록한다(나중 도장이 덮어씀).</summary>
    public void Record(IEnumerable<Point> cells, double distance)
    {
        foreach (var c in cells) _pixels[c] = distance;
    }

    /// <summary>픽셀 퍼펙트로 빠진 픽셀을 기록에서 뺀다(부르는 쪽이 그림을 획 전으로 되돌린다).</summary>
    public void Forget(IEnumerable<Point> cells)
    {
        foreach (var c in cells) _pixels.Remove(c);
    }

    /// <summary>획 전체 모드의 위치: 거리 / 획 길이(0~1). 길이가 0(움직이지 않은 클릭)이면 0 = 그라데이션 처음 색.</summary>
    public static double WholeT(double distance, double length)
        => length <= 0 ? 0 : Math.Clamp(distance / length, 0, 1);

    /// <summary>반복 모드의 위치: (거리 / 간격)의 소수 부분. 간격의 배수 거리에서 다시 0(처음 색)이 된다.</summary>
    public static double RepeatT(double distance, int interval)
    {
        double u = Math.Max(0, distance) / Math.Clamp(interval, MinInterval, MaxInterval);
        return u - Math.Floor(u);
    }

    /// <summary>지금 획 길이 기준으로 거리 <paramref name="distance"/>의 위치 t.</summary>
    public double T(double distance, GradientStrokeMode mode, int interval)
        => mode == GradientStrokeMode.Repeat ? RepeatT(distance, interval) : WholeT(distance, Length);

    /// <summary>
    /// 기록한 픽셀을 모두 <paramref name="gradient"/>의 색(색·불투명도 정지점, 보간, 색 공간, 부드럽게일 때 디더링)으로 칠한다.
    /// 획 전체 모드는 길이가 늘 때마다 다시 불러 지금 길이로 다시 칠한다. 이미지 밖 픽셀은 건너뛴다.
    /// </summary>
    public void Render(PixelImage image, GradientPreset gradient, GradientStrokeMode mode, int interval)
    {
        var stops = Gradient.Sorted(gradient.Stops);
        var alphaStops = Gradient.SortedAlpha(gradient.AlphaStops);
        var dither = gradient.Interpolation == GradientInterpolation.Smooth ? gradient.Dither : GradientDither.None;
        foreach (var (p, distance) in _pixels)
        {
            if (!image.Contains(p.X, p.Y)) continue;
            double t = T(distance, mode, interval);
            image.Set(p.X, p.Y, Gradient.Sample(stops, alphaStops, t, gradient.Interpolation, dither, p.X, p.Y, gradient.ColorSpace));
        }
    }
}
