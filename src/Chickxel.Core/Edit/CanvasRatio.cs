using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Edit;

/// <summary>특수 비율로 맞출 때 그대로 두는 변.</summary>
public enum CanvasRatioBasis
{
    /// <summary>캔버스 가로를 그대로 두고 세로를 비율로 정한다.</summary>
    KeepWidth,
    /// <summary>캔버스 세로를 그대로 두고 가로를 비율로 정한다.</summary>
    KeepHeight,
}

/// <summary>특수 비율로 맞추는 방식.</summary>
public enum CanvasRatioFit
{
    /// <summary>캔버스만 바꾼다: 그림은 그대로, 바뀌는 변은 빈칸이 붙거나 잘린다(잘린 픽셀은 캔버스 밖 그림으로 남음).</summary>
    Canvas,
    /// <summary>그림을 새 캔버스에 꼭 맞게 늘이거나 줄인다.</summary>
    Stretch,
}

/// <summary>
/// 캔버스 크기 모드의 특수 비율(1:1·16:9 등): 지금 캔버스 크기에서 한 변을 그대로 두고 다른 변을 비율로 정해, 새 캔버스 사각형(원래 프레임 좌표)과
/// 그림 사각형(새 캔버스 좌표)을 계산한다. 값은 캔버스 크기 모드의 <c>SetCanvasResize</c>에 그대로 넘긴다(적용은 그 모드가 한다).
/// </summary>
public static class CanvasRatio
{
    /// <summary>비율 한 항의 가장 큰 값.</summary>
    public const int MaxTerm = 100;

    /// <summary>빠른 고르기 비율(가로:세로).</summary>
    public static readonly IReadOnlyList<(int Width, int Height)> Presets = [(1, 1), (4, 3), (3, 4), (3, 2), (2, 3), (16, 9), (9, 16)];

    /// <summary>
    /// 새 캔버스 크기: <paramref name="basis"/> 쪽 변은 그대로, 다른 변은 비율로(가장 가까운 픽셀, .5는 올림). 비율 항이 1~<see cref="MaxTerm"/> 밖이거나
    /// 결과가 1~<see cref="PixelImage.MaxSize"/> 밖이면 null.
    /// </summary>
    public static Size? TargetSize(Size canvas, int ratioWidth, int ratioHeight, CanvasRatioBasis basis)
    {
        if (ratioWidth < 1 || ratioWidth > MaxTerm || ratioHeight < 1 || ratioHeight > MaxTerm) return null;
        if (canvas.Width < 1 || canvas.Height < 1) return null;
        long other = basis == CanvasRatioBasis.KeepWidth
            ? Round((long)canvas.Width * ratioHeight, ratioWidth)
            : Round((long)canvas.Height * ratioWidth, ratioHeight);
        if (other < 1 || other > PixelImage.MaxSize) return null;
        return basis == CanvasRatioBasis.KeepWidth ? new Size(canvas.Width, (int)other) : new Size((int)other, canvas.Height);
    }

    /// <summary>
    /// 새 캔버스(원래 프레임 좌표)와 그림 사각형(새 캔버스 좌표). <paramref name="canvas"/>·<paramref name="image"/>는 지금 캔버스 크기 모드 값.
    /// <para><see cref="CanvasRatioFit.Canvas"/>: 그림 크기·화면 위치는 그대로. 바뀌는 변에서 그림은 <paramref name="anchor"/> 자리로 놓이고(다른 변은 그대로),
    /// 기준점이 없으면(그림이 9방향 어디에도 맞지 않음) 캔버스가 양쪽으로 같게 늘거나 준다(그림과 캔버스 사이 거리 유지).</para>
    /// <para><see cref="CanvasRatioFit.Stretch"/>: 그림이 새 캔버스를 꼭 채운다. 캔버스는 바뀌는 변에서 <paramref name="anchor"/>(없으면 가운데) 기준으로 늘거나 준다.</para>
    /// 크기를 정할 수 없으면 null(<see cref="TargetSize"/>).
    /// </summary>
    public static (Rectangle Canvas, Rectangle Image)? Plan(Rectangle canvas, Rectangle image, int ratioWidth, int ratioHeight,
        CanvasRatioBasis basis, CanvasRatioFit fit, Anchor? anchor)
    {
        if (TargetSize(canvas.Size, ratioWidth, ratioHeight, basis) is not { } size) return null;
        bool vertical = basis == CanvasRatioBasis.KeepWidth; // 바뀌는 변이 세로인지
        if (fit == CanvasRatioFit.Stretch)
        {
            var (ox, oy) = PixelImage.AnchorOffset(canvas.Width, canvas.Height, size.Width, size.Height, anchor ?? Anchor.Center);
            var moved = vertical ? canvas with { Y = canvas.Y - oy } : canvas with { X = canvas.X - ox };
            return (moved with { Size = size }, new Rectangle(Point.Empty, size));
        }
        int shift; // 그림이 새 캔버스 안에서 옮겨 가는 양(바뀌는 변 방향)
        if (anchor is { } a)
        {
            var (ix, iy) = PixelImage.AnchorOffset(image.Width, image.Height, size.Width, size.Height, a);
            shift = vertical ? iy - image.Y : ix - image.X;
        }
        else
        {
            var (ox, oy) = PixelImage.AnchorOffset(canvas.Width, canvas.Height, size.Width, size.Height, Anchor.Center);
            shift = vertical ? oy : ox;
        }
        // 그림의 화면(원래 프레임) 위치는 그대로: 그림이 옮겨 간 만큼 캔버스가 반대로 옮겨 간다.
        return vertical
            ? (new Rectangle(canvas.X, canvas.Y - shift, size.Width, size.Height), image with { Y = image.Y + shift })
            : (new Rectangle(canvas.X - shift, canvas.Y, size.Width, size.Height), image with { X = image.X + shift });
    }

    /// <summary>
    /// 그림 사각형이 9방향 기준점 중 하나에 꼭 맞으면 그 기준점(도구 설정의 기준점 단추가 눌리는 것과 같음), 아니면 null. 그림과 캔버스 크기가 같은 변이 있으면
    /// 여러 기준점이 함께 맞으므로, 맞는 것 가운데 <paramref name="preferred"/>(마지막으로 고른 기준점)를 먼저 고른다.
    /// </summary>
    public static Anchor? MatchAnchor(Size canvas, Rectangle image, Anchor? preferred = null)
    {
        bool Fits(Anchor a) => PixelImage.AnchorOffset(image.Width, image.Height, canvas.Width, canvas.Height, a) == (image.X, image.Y);
        if (preferred is { } p && Fits(p)) return p;
        foreach (var a in Enum.GetValues<Anchor>())
            if (Fits(a)) return a;
        return null;
    }

    /// <summary>양수 나눗셈을 가장 가까운 정수로(.5는 올림).</summary>
    private static long Round(long numerator, long denominator) => (2 * numerator + denominator) / (2 * denominator);
}
