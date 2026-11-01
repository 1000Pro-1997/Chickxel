namespace Chickxel.Core.Edit;

/// <summary>
/// 대칭 그리기의 축(<see cref="Raster.Mirror(System.Drawing.Point, MirrorAxes, bool, bool)"/>). 값은 픽셀 모서리 좌표의 두 배라
/// 픽셀 경계 축(짝수)과 픽셀 가운데 축(홀수)을 모두 정수로 정확히 나타낸다. 좌우 대칭은 x = <see cref="X2"/>/2인 세로 선,
/// 상하 대칭은 y = <see cref="Y2"/>/2인 가로 선을 기준으로 뒤집는다.
/// </summary>
public readonly record struct MirrorAxes(int X2, int Y2)
{
    /// <summary>캔버스 가운데 축(가로 <paramref name="width"/>·세로 <paramref name="height"/>).</summary>
    public static MirrorAxes Center(int width, int height) => new(width, height);

    /// <summary>
    /// 픽셀 모서리 좌표(<paramref name="x"/>, <paramref name="y"/>, 왼쪽 위 기준)를 지나는 축. 반 픽셀 단위로 맞춘다
    /// (피벗 도구가 반 픽셀 단위로 놓는 것과 같음).
    /// </summary>
    public static MirrorAxes Through(double x, double y)
        => new((int)Math.Round(x * 2, MidpointRounding.AwayFromZero), (int)Math.Round(y * 2, MidpointRounding.AwayFromZero));

    /// <summary>세로 축의 픽셀 모서리 x 좌표.</summary>
    public double X => X2 / 2.0;

    /// <summary>가로 축의 픽셀 모서리 y 좌표.</summary>
    public double Y => Y2 / 2.0;

    /// <summary><paramref name="p"/>를 세로 축(<paramref name="flipX"/>)·가로 축(<paramref name="flipY"/>)으로 뒤집은 픽셀.</summary>
    public System.Drawing.Point Flip(System.Drawing.Point p, bool flipX, bool flipY)
        => new(flipX ? X2 - 1 - p.X : p.X, flipY ? Y2 - 1 - p.Y : p.Y);
}
