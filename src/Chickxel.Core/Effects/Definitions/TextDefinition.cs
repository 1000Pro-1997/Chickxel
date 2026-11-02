using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Effects;

/// <summary>
/// 글씨 효과 정의: 설정 schema, 렌더러 연결. 원본 없이 그리므로 빈 셀에도 그린다(<see cref="EffectDefinition.GeneratesContent"/>), 여백은 없다(원본 영역 밖은 잘림).
/// "font"는 글꼴 이름(글 — 예전 파일의 글꼴 열거 이름이 그대로 기본 글꼴 이름), 크기·굵게 등 뒤에 생긴 키가 없는 예전 파일은 기본값.
/// </summary>
public static class TextDefinition
{
    public const string Id = "chickxel.effect.text";

    public static EffectDefinition Definition { get; } = EffectDefinition.Create<TextEffect>(Id, "text", TextRender.Apply, s =>
    [
        s.Text("text", e => e.Text, (e, v) => e with { Text = v }, TextEffect.MaxLength),
        s.Text("font", e => e.FontName, (e, v) => e with { FontName = v }, TextEffect.MaxFontNameLength),
        s.Int("size", 0, TextEffect.MaxSize, e => e.Size, (e, v) => e with { Size = v }),
        s.Bool("bold", e => e.Bold, (e, v) => e with { Bold = v }),
        s.Bool("italic", e => e.Italic, (e, v) => e with { Italic = v }),
        s.Bool("smooth", e => e.Smooth, (e, v) => e with { Smooth = v }),
        s.Color("color", e => e.Color, (e, v) => e with { Color = v }),
        s.Int("x", -TextEffect.MaxPosition, TextEffect.MaxPosition, e => e.X, (e, v) => e with { X = v }),
        s.Int("y", -TextEffect.MaxPosition, TextEffect.MaxPosition, e => e.Y, (e, v) => e with { Y = v }),
        s.Int("boxWidth", 0, TextEffect.MaxBoxSize, e => e.BoxWidth, (e, v) => e with { BoxWidth = v }),
        s.Int("boxHeight", 0, TextEffect.MaxBoxSize, e => e.BoxHeight, (e, v) => e with { BoxHeight = v }),
        s.Int("letterSpacing", TextEffect.MinLetterSpacing, TextEffect.MaxLetterSpacing, e => e.LetterSpacing, (e, v) => e with { LetterSpacing = v }),
        s.Int("lineSpacing", TextEffect.MinLineSpacing, TextEffect.MaxLineSpacing, e => e.LineSpacing, (e, v) => e with { LineSpacing = v }),
        s.Int("scaleX", TextEffect.MinScale, TextEffect.MaxScale, e => e.ScaleX, (e, v) => e with { ScaleX = v }),
        s.Int("scaleY", TextEffect.MinScale, TextEffect.MaxScale, e => e.ScaleY, (e, v) => e with { ScaleY = v }),
        s.Choice("align", e => e.Align, (e, v) => e with { Align = v }),
        s.Bool("typewriter", e => e.Typewriter, (e, v) => e with { Typewriter = v }),
    ], generatesContent: true);
}
