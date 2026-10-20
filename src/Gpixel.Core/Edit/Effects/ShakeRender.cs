using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>흔들기(<see cref="ShakeEffect"/>)·둥실둥실(<see cref="BobEffect"/>) 그리기: 그림 전체를 정수 픽셀만큼 옮긴다.</summary>
internal static class ShakeRender
{
    public static void Apply(PixelImage image, ShakeEffect e)
    {
        var (dx, dy) = e.Offset(e.Progress);
        Shift(image, dx, dy);
    }

    public static void Apply(PixelImage image, BobEffect e)
    {
        var (dx, dy) = e.Offset(e.Progress);
        Shift(image, dx, dy);
    }

    /// <summary>그림을 (<paramref name="dx"/>, <paramref name="dy"/>)px 옮긴다(밖으로 나간 부분은 잘리고 빈 자리는 투명).</summary>
    private static void Shift(PixelImage image, int dx, int dy)
    {
        if (dx == 0 && dy == 0) return;
        var src = image.Clone();
        image.Clear();
        image.Blit(src, dx, dy);
    }
}
