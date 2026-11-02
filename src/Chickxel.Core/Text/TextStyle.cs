using Chickxel.Core.Model;

namespace Chickxel.Core.Text;

/// <summary>
/// 글씨 모양: 글씨 도구의 기본 설정(앱)과 글씨 레이어(<see cref="TextEffect"/>)가 같이 쓰는 글꼴·크기·굵게·기울임·부드럽게·간격·비율·맞춤.
/// 범위는 <see cref="TextEffect"/>가 자른다(<see cref="ApplyTo"/>).
/// </summary>
public sealed record TextStyle
{
    public string FontName { get; init; } = FontLibrary.DefaultName;

    /// <summary>px, 0 = 도트 글꼴은 원래 크기, 벡터 글꼴은 12(0~<see cref="TextEffect.MaxSize"/>).</summary>
    public int Size { get; init; }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    /// <summary>벡터 글꼴만: 가장자리 부드럽게.</summary>
    public bool Smooth { get; init; }

    /// <summary>px(<see cref="TextEffect.MinLetterSpacing"/>~<see cref="TextEffect.MaxLetterSpacing"/>).</summary>
    public int LetterSpacing { get; init; }

    /// <summary>px(<see cref="TextEffect.MinLineSpacing"/>~<see cref="TextEffect.MaxLineSpacing"/>).</summary>
    public int LineSpacing { get; init; }

    /// <summary>%(<see cref="TextEffect.MinScale"/>~<see cref="TextEffect.MaxScale"/>).</summary>
    public int ScaleX { get; init; } = 100;

    public int ScaleY { get; init; } = 100;

    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>글씨 효과의 모양.</summary>
    public static TextStyle From(TextEffect e) => new()
    {
        FontName = e.FontName,
        Size = e.Size,
        Bold = e.Bold,
        Italic = e.Italic,
        Smooth = e.Smooth,
        LetterSpacing = e.LetterSpacing,
        LineSpacing = e.LineSpacing,
        ScaleX = e.ScaleX,
        ScaleY = e.ScaleY,
        Align = e.Align,
    };

    /// <summary><paramref name="e"/>에 이 모양을 넣은 효과(나머지 설정 — 글·색·자리·상자·타자기 — 는 그대로).</summary>
    public TextEffect ApplyTo(TextEffect e) => e with
    {
        FontName = FontName,
        Size = Size,
        Bold = Bold,
        Italic = Italic,
        Smooth = Smooth,
        LetterSpacing = LetterSpacing,
        LineSpacing = LineSpacing,
        ScaleX = ScaleX,
        ScaleY = ScaleY,
        Align = Align,
    };
}
