using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Effects;

/// <summary>글씨 효과 정의: 설정 schema, 렌더러 연결. 원본 없이 그리므로 빈 셀에도 그린다(<see cref="EffectDefinition.GeneratesContent"/>), 여백은 없다(원본 영역 밖은 잘림).</summary>
public static class TextDefinition
{
    public const string Id = "chickxel.effect.text";

    public static EffectDefinition Definition { get; } = EffectDefinition.Create<TextEffect>(Id, "text", TextRender.Apply, s =>
    [
        s.Text("text", e => e.Text, (e, v) => e with { Text = v }, TextEffect.MaxLength),
        s.Choice("font", e => e.Font, (e, v) => e with { Font = v }),
        s.Color("color", e => e.Color, (e, v) => e with { Color = v }),
        s.Int("x", -TextEffect.MaxPosition, TextEffect.MaxPosition, e => e.X, (e, v) => e with { X = v }),
        s.Int("y", -TextEffect.MaxPosition, TextEffect.MaxPosition, e => e.Y, (e, v) => e with { Y = v }),
        s.Int("letterSpacing", TextEffect.MinLetterSpacing, TextEffect.MaxLetterSpacing, e => e.LetterSpacing, (e, v) => e with { LetterSpacing = v }),
        s.Int("lineSpacing", TextEffect.MinLineSpacing, TextEffect.MaxLineSpacing, e => e.LineSpacing, (e, v) => e with { LineSpacing = v }),
        s.Choice("align", e => e.Align, (e, v) => e with { Align = v }),
        s.Bool("typewriter", e => e.Typewriter, (e, v) => e with { Typewriter = v }),
    ], generatesContent: true);
}
