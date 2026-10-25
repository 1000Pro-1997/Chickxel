using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>움직이는 그림(GIF·APNG·WebP·영상)을 푼 결과: 캔버스 크기, 프레임마다 완성된 그림(모두 캔버스 크기, 직선 알파)과 길이(ms).</summary>
public sealed record DecodedAnimation(int Width, int Height, IReadOnlyList<PixelImage> Images, IReadOnlyList<int> DurationsMs)
{
    public int FrameCount => Images.Count;
}

/// <summary>프레임을 그린 뒤 다음 프레임 전에 캔버스를 어떻게 할지(GIF 처리 방법·APNG dispose_op).</summary>
public enum FrameDisposal
{
    /// <summary>그대로 둔다(GIF 0·1, APNG NONE).</summary>
    None,
    /// <summary>프레임 칸을 투명으로 지운다(GIF 2, APNG BACKGROUND).</summary>
    Background,
    /// <summary>프레임을 그리기 전 캔버스로 되돌린다(GIF 3, APNG PREVIOUS).</summary>
    Previous,
}

/// <summary>프레임 픽셀을 캔버스에 놓는 방법(APNG blend_op — GIF는 투명 색을 알파 0으로 바꿔 <see cref="Over"/>).</summary>
public enum FrameBlend
{
    /// <summary>칸 안을 프레임 픽셀로 바꾼다(알파 포함).</summary>
    Source,
    /// <summary>캔버스 위에 알파 합성한다.</summary>
    Over,
}

/// <summary>파일에 적힌 프레임 길이를 프로젝트 프레임 길이로 바꾼다.</summary>
public static class FrameDurations
{
    /// <summary>이보다 짧거나 같은 길이(0 포함)는 브라우저처럼 <see cref="Frame.DefaultDurationMs"/>로 본다.</summary>
    public const int MinMs = 10;

    /// <summary>
    /// 파일 프레임 길이(ms) → 프로젝트 길이. 0~<see cref="MinMs"/>ms는 브라우저(Chrome·Firefox)와 같이 100ms로 본다
    /// (GIF 지연 0·1(1/100초)은 "가능한 빨리"가 아니라 보통 속도로 재생됨).
    /// </summary>
    public static int FromFile(int ms) => ms <= MinMs ? Frame.DefaultDurationMs : ms;

    /// <summary>초당 프레임 수 → 프레임 길이(ms, 반올림, 최소 <see cref="MinMs"/>).</summary>
    public static int FromFps(double fps) => fps <= 0 ? Frame.DefaultDurationMs : Math.Max(MinMs, (int)Math.Round(1000.0 / fps));
}

/// <summary>
/// 움직이는 그림의 프레임 합성: 캔버스(처음엔 투명)에 부분 프레임을 놓고 완성된 그림을 돌려준 뒤, 처리 방법대로 다음 프레임 바탕을 만든다.
/// GIF·APNG 읽기가 같이 쓴다.
/// </summary>
public sealed class FrameCompositor
{
    private readonly Color32[] _canvas;
    private Color32[]? _saved;

    public FrameCompositor(int width, int height)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
            throw new InvalidDataException(Localization.DomainStrings.FormatImage_InvalidSize(max: PixelImage.MaxSize, width: width, height: height));
        Width = width;
        Height = height;
        _canvas = new Color32[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>
    /// 프레임 하나를 그린다: <paramref name="pixels"/>(<paramref name="rect"/> 크기, 행 순서)를 캔버스의 그 칸에 놓고(캔버스 밖은 잘림)
    /// 완성된 그림을 돌려준 다음 <paramref name="disposal"/>을 적용한다.
    /// </summary>
    public PixelImage Compose(Rectangle rect, ReadOnlySpan<Color32> pixels, FrameBlend blend, FrameDisposal disposal)
    {
        if (pixels.Length < rect.Width * rect.Height) throw new ArgumentException(null, nameof(pixels));
        if (disposal == FrameDisposal.Previous) _canvas.CopyTo(_saved ??= new Color32[_canvas.Length], 0);
        var clip = Rectangle.Intersect(rect, new Rectangle(0, 0, Width, Height));
        for (int y = clip.Top; y < clip.Bottom; y++)
        {
            var src = pixels.Slice((y - rect.Y) * rect.Width + (clip.X - rect.X), clip.Width);
            var dst = _canvas.AsSpan(y * Width + clip.X, clip.Width);
            if (blend == FrameBlend.Source) src.CopyTo(dst);
            else
                for (int x = 0; x < src.Length; x++) dst[x] = Over(dst[x], src[x]);
        }
        var frame = new PixelImage(Width, Height, (Color32[])_canvas.Clone());
        switch (disposal)
        {
            case FrameDisposal.Background:
                for (int y = clip.Top; y < clip.Bottom; y++) _canvas.AsSpan(y * Width + clip.X, clip.Width).Clear();
                break;
            case FrameDisposal.Previous:
                _saved!.CopyTo(_canvas, 0);
                break;
        }
        return frame;
    }

    /// <summary>직선 알파 "위에 덮기"(src over dst).</summary>
    public static Color32 Over(Color32 dst, Color32 src)
    {
        int sa = src.A;
        if (sa == 255 || dst.A == 0) return src.A == 0 ? dst : src;
        if (sa == 0) return dst;
        int da = dst.A * (255 - sa) / 255;
        int a = sa + da;
        return new Color32(
            (byte)((src.R * sa + dst.R * da + a / 2) / a),
            (byte)((src.G * sa + dst.G * da + a / 2) / a),
            (byte)((src.B * sa + dst.B * da + a / 2) / a),
            (byte)a);
    }
}
