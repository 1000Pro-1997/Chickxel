using System.Drawing;

namespace Gpixel.Core.Model;

/// <summary>
/// 스프라이트 시트의 잘린 칸 하나. <see cref="Rect"/>는 이미지 기준 <b>왼쪽 위 원점</b> 픽셀 좌표다
/// (Unity 메타로 쓸 때 아래 왼쪽 원점으로 변환).
/// </summary>
public sealed class Slice
{
    public string Name { get; set; }
    public Rectangle Rect { get; set; }
    /// <summary>칸 기준 0~1 피벗(아래 왼쪽 원점).</summary>
    public Pivot Pivot { get; set; }
    public SpriteBorder Border { get; set; }

    public Slice(string name, Rectangle rect, Pivot pivot, SpriteBorder border = default)
    {
        Name = name;
        Rect = rect;
        Pivot = pivot;
        Border = border;
    }

    /// <summary>Unity <c>alignment</c> 값(프리셋 위치면 해당 번호, 아니면 Custom=9).</summary>
    public PivotPreset Alignment => Pivot.Preset;

    /// <summary>피벗이 가리키는 이미지 픽셀 좌표(왼쪽 위 원점, 실수).</summary>
    public (float X, float Y) PivotPixel
    {
        get
        {
            var (lx, ly) = Pivot.ToLocalPixel(Rect.Width, Rect.Height);
            return (Rect.X + lx, Rect.Y + ly);
        }
    }

    public Slice Clone() => new(Name, Rect, Pivot, Border);

    public override string ToString() => $"{Name} [{Rect.X},{Rect.Y} {Rect.Width}x{Rect.Height}]";
}
