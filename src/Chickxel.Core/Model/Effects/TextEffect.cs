using Chickxel.Core.Text;

namespace Chickxel.Core.Model;

/// <summary>여러 줄 글씨의 줄 맞춤. 글 상자가 없으면 기준점(<see cref="TextEffect.X"/>)이 줄의 왼쪽 끝·가운데·오른쪽 끝, 있으면 상자 안에서 맞춘다.</summary>
public enum TextAlign
{
    Left,
    Center,
    Right,
}

/// <summary>
/// 글씨: 글꼴(<see cref="FontName"/> — <see cref="FontLibrary"/>)로 글(<see cref="Text"/>)을 효과 원본 왼쪽 위 기준 (<see cref="X"/>, <see cref="Y"/>)에 한 색으로 그린다
/// (그림 위에 덮음, 효과 원본 영역 밖은 잘림 — 여백 없음). 글 상자(<see cref="BoxWidth"/> &gt; 0)면 그 너비 안에서 낱말 단위로 줄을 바꾼다.
/// 빈 레이어에 붙이면 글씨 레이어처럼 쓰고(셀이 없어도 빈 그림 위에 그림 — <see cref="SpriteProject.HasContentEffect(int, int)"/>), 아래로 합치기·영구 적용으로 픽셀이 된다.
/// <see cref="Typewriter"/>면 효과 범위의 진행에 따라 앞 글자부터 하나씩 나타난다(첫 프레임은 빈 글, 마지막 프레임은 전부).
/// </summary>
public sealed record TextEffect : AnimatedEffect
{
    public const int MaxLength = 1000, MaxPosition = 4096, MinLetterSpacing = -32, MaxLetterSpacing = 128, MinLineSpacing = -64, MaxLineSpacing = 256;
    public const int MaxSize = 256, MinScale = 25, MaxScale = 400, MaxBoxSize = 4096, MaxFontNameLength = 255;

    private readonly string _text = DefaultText, _font = FontLibrary.DefaultName;
    private readonly int _x, _y, _letter, _line, _size, _scaleX = 100, _scaleY = 100, _boxWidth, _boxHeight;

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

    /// <summary>
    /// 글꼴 이름(<see cref="FontInfo.Name"/> — 기본 글꼴 "Galmuri11" 등, 사용자 글꼴은 글꼴 폴더 안 파일 이름). 비었으면 <see cref="FontLibrary.DefaultName"/>,
    /// 기본 글꼴 이름은 대소문자를 맞춘다. 이 컴퓨터에 없는 글꼴이면 기본 글꼴로 그린다(이름은 그대로 둠).
    /// </summary>
    public string FontName
    {
        get => _font;
        init
        {
            var name = (value ?? "").Trim();
            if (name.Length > MaxFontNameLength) name = name[..MaxFontNameLength];
            _font = name.Length == 0 ? FontLibrary.DefaultName : FontLibrary.BuiltInName(name) ?? name;
        }
    }

    /// <summary>
    /// 글자 크기 px(0~<see cref="MaxSize"/>, 0 = 도트 글꼴은 원래 크기, 앱에 든 도트 모양 벡터 글꼴은 도트가 칸에 맞는 크기, 그 밖의 벡터 글꼴은 12).
    /// 도트 글꼴은 줄 높이가 이 크기가 되게 칸 단위로 늘이거나 줄인다.
    /// </summary>
    public int Size { get => _size; init => _size = Math.Clamp(value, 0, MaxSize); }

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    /// <summary>벡터 글꼴의 가장자리를 부드럽게(덮인 정도 → 반투명). 끄면 또렷하게(1비트). 도트 글꼴은 늘 또렷하다.</summary>
    public bool Smooth { get; init; }

    public Color32 Color { get; init; } = Color32.Black;

    /// <summary>
    /// 기준점 X(효과 원본 왼쪽 위 기준 px), ±<see cref="MaxPosition"/>. 글 상자가 있으면 상자 왼쪽 끝, 없으면 맞춤에 따라 줄의 왼쪽 끝·가운데·오른쪽 끝.
    /// </summary>
    public int X { get => _x; init => _x = Math.Clamp(value, -MaxPosition, MaxPosition); }

    /// <summary>첫 줄 위쪽 끝(글꼴 ascent 위, 글 상자면 상자 위쪽 끝) Y, ±<see cref="MaxPosition"/>.</summary>
    public int Y { get => _y; init => _y = Math.Clamp(value, -MaxPosition, MaxPosition); }

    /// <summary>글 상자 너비 px(0~<see cref="MaxBoxSize"/>, 0 = 상자 없음 — 줄을 바꾸지 않음).</summary>
    public int BoxWidth { get => _boxWidth; init => _boxWidth = Math.Clamp(value, 0, MaxBoxSize); }

    /// <summary>글 상자 최소 높이 px(0~<see cref="MaxBoxSize"/>, 0 = 글 높이). 글은 잘리지 않는다.</summary>
    public int BoxHeight { get => _boxHeight; init => _boxHeight = Math.Clamp(value, 0, MaxBoxSize); }

    /// <summary>글자 사이에 더할 px(<see cref="MinLetterSpacing"/>~<see cref="MaxLetterSpacing"/>).</summary>
    public int LetterSpacing { get => _letter; init => _letter = Math.Clamp(value, MinLetterSpacing, MaxLetterSpacing); }

    /// <summary>줄 사이에 더할 px(<see cref="MinLineSpacing"/>~<see cref="MaxLineSpacing"/>).</summary>
    public int LineSpacing { get => _line; init => _line = Math.Clamp(value, MinLineSpacing, MaxLineSpacing); }

    /// <summary>가로 비율 %(<see cref="MinScale"/>~<see cref="MaxScale"/>).</summary>
    public int ScaleX { get => _scaleX; init => _scaleX = Math.Clamp(value, MinScale, MaxScale); }

    /// <summary>세로 비율 %(<see cref="MinScale"/>~<see cref="MaxScale"/>).</summary>
    public int ScaleY { get => _scaleY; init => _scaleY = Math.Clamp(value, MinScale, MaxScale); }

    public TextAlign Align { get; init; } = TextAlign.Left;

    /// <summary>한 글자씩 나타나기(효과 범위의 진행에 따라).</summary>
    public bool Typewriter { get; init; }

    public override string DisplayName => Chickxel.Core.Localization.DomainStrings.Effect_Text;
}
