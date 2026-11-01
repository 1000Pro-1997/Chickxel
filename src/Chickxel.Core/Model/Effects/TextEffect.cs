namespace Chickxel.Core.Model;

/// <summary>글씨 효과의 도트 글꼴(앱에 든 Galmuri BDF — <see cref="Text.PixelFonts"/>).</summary>
public enum TextFont
{
    Galmuri7,
    Galmuri9,
    Galmuri11,
    Galmuri11Bold,
    Galmuri14,
}

/// <summary>여러 줄 글씨의 줄 맞춤. 기준점(<see cref="TextEffect.X"/>)이 줄의 왼쪽 끝·가운데·오른쪽 끝이다.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// 글씨: 도트 글꼴로 글(<see cref="Text"/>)을 효과 원본 왼쪽 위 기준 (<see cref="X"/>, <see cref="Y"/>)에 한 색으로 그린다(그림 위에 덮음, 효과 원본 영역 밖은 잘림 — 여백 없음).
/// 빈 레이어에 붙이면 글씨 레이어처럼 쓰고(셀이 없어도 빈 그림 위에 그림 — <see cref="SpriteProject.HasContentEffect(int, int)"/>), 아래로 합치기·영구 적용으로 픽셀이 된다.
/// <see cref="Typewriter"/>면 효과 범위의 진행에 따라 앞 글자부터 하나씩 나타난다(첫 프레임은 빈 글, 마지막 프레임은 전부).
/// </summary>
public sealed record TextEffect : AnimatedEffect
{
    public const int MaxLength = 1000, MaxPosition = 4096, MinLetterSpacing = -4, MaxLetterSpacing = 16, MinLineSpacing = -8, MaxLineSpacing = 32;

    private readonly string _text = DefaultText;
    private readonly int _x, _y, _letter, _line;

    public const string DefaultText = "Text";

    /// <summary>그릴 글(줄바꿈 = 새 줄, "\r\n"은 "\n"으로). 최대 <see cref="MaxLength"/>자.</summary>
    public string Text
    {
        get => _text;
        init
        {
            var t = (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
            _text = t.Length > MaxLength ? t[..MaxLength] : t;
        }
    }

    public TextFont Font { get; init; } = TextFont.Galmuri11;

    public Color32 Color { get; init; } = Color32.Black;

    /// <summary>기준점 X(맞춤에 따라 줄의 왼쪽 끝·가운데·오른쪽 끝, 효과 원본 왼쪽 위 기준 px), ±<see cref="MaxPosition"/>.</summary>
    public int X { get => _x; init => _x = Math.Clamp(value, -MaxPosition, MaxPosition); }

    /// <summary>첫 줄 위쪽 끝(글꼴 ascent 위) Y, ±<see cref="MaxPosition"/>.</summary>
    public int Y { get => _y; init => _y = Math.Clamp(value, -MaxPosition, MaxPosition); }

    /// <summary>글자 사이에 더할 px(<see cref="MinLetterSpacing"/>~<see cref="MaxLetterSpacing"/>).</summary>
    public int LetterSpacing { get => _letter; init => _letter = Math.Clamp(value, MinLetterSpacing, MaxLetterSpacing); }

    /// <summary>줄 사이에 더할 px(<see cref="MinLineSpacing"/>~<see cref="MaxLineSpacing"/>).</summary>
    public int LineSpacing { get => _line; init => _line = Math.Clamp(value, MinLineSpacing, MaxLineSpacing); }

    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>한 글자씩 나타나기(효과 범위의 진행에 따라).</summary>
    public bool Typewriter { get; init; }

    public override string DisplayName => Chickxel.Core.Localization.DomainStrings.Effect_Text;
}
