using System.Globalization;

namespace Chickxel.Core.Model;

/// <summary>
/// 피벗 프리셋. 정수 값이 Unity <c>SpriteAlignment</c>(메타의 <c>alignment</c>)와 같다.
/// </summary>
public enum PivotPreset
{
    Center = 0,
    TopLeft = 1,
    TopCenter = 2,
    TopRight = 3,
    LeftCenter = 4,
    RightCenter = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8,
    Custom = 9,
}

/// <summary>
/// 스프라이트 피벗(포커스). Unity와 같이 칸 크기에 대한 0~1 정규화 좌표이며
/// <b>아래 왼쪽이 (0,0), 위 오른쪽이 (1,1)</b>이다(편집기 픽셀 좌표와 y 방향이 반대).
/// </summary>
public readonly record struct Pivot(float X, float Y)
{
    public static Pivot Center => new(0.5f, 0.5f);
    public static Pivot BottomCenter => new(0.5f, 0f);

    /// <summary>프리셋에 해당하는 피벗. <see cref="PivotPreset.Custom"/>이면 가운데.</summary>
    public static Pivot FromPreset(PivotPreset preset) => preset switch
    {
        PivotPreset.TopLeft => new(0f, 1f),
        PivotPreset.TopCenter => new(0.5f, 1f),
        PivotPreset.TopRight => new(1f, 1f),
        PivotPreset.LeftCenter => new(0f, 0.5f),
        PivotPreset.RightCenter => new(1f, 0.5f),
        PivotPreset.BottomLeft => new(0f, 0f),
        PivotPreset.BottomCenter => new(0.5f, 0f),
        PivotPreset.BottomRight => new(1f, 0f),
        _ => new(0.5f, 0.5f),
    };

    /// <summary>값이 정확히 프리셋 위치면 그 프리셋, 아니면 <see cref="PivotPreset.Custom"/>.</summary>
    public PivotPreset Preset
    {
        get
        {
            for (var p = PivotPreset.Center; p < PivotPreset.Custom; p++)
                if (FromPreset(p) == this) return p;
            return PivotPreset.Custom;
        }
    }

    /// <summary>0~1 범위로 자른 피벗.</summary>
    public Pivot Clamped() => new(Math.Clamp(X, 0f, 1f), Math.Clamp(Y, 0f, 1f));

    /// <summary>
    /// 칸 안에서 피벗이 가리키는 픽셀 좌표(칸 왼쪽 위 기준, y 아래 방향). 실수 좌표다.
    /// </summary>
    public (float X, float Y) ToLocalPixel(int width, int height) => (X * width, (1f - Y) * height);

    /// <summary>칸 안의 픽셀 좌표(왼쪽 위 기준)에서 피벗을 만든다.</summary>
    public static Pivot FromLocalPixel(float x, float y, int width, int height)
        => new Pivot(width == 0 ? 0.5f : x / width, height == 0 ? 0.5f : 1f - y / height).Clamped();

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");
}

/// <summary>9-slice 테두리(픽셀). 편집기 표기 순서 왼쪽·위·오른쪽·아래.</summary>
public readonly record struct SpriteBorder(int Left, int Top, int Right, int Bottom)
{
    public static SpriteBorder None => default;
    public bool IsNone => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}
