using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>효과 창·타임라인 표시: 이름, 설명, 타임라인 색, 설정 칸, 목록 요약.</summary>
    private static EffectUi TextUi => EffectUi.For<TextEffect>(TextDefinition.Id, UiStrings.TextEffect_Name, UiStrings.TextEffect_Description,
        Avalonia.Media.Color.FromRgb(232, 180, 64), (dialog, e, set) => dialog.TextPanel(e, set),
        t => string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.TextEffect_Summary, TextSummary(t.Text), TextFontNames[(int)t.Font])
             + (t.Typewriter ? UiStrings.TextEffect_SummaryTypewriter : ""));

    /// <summary>글씨 글꼴 이름(<see cref="TextFont"/> 순서). 글씨 도구 설정도 같이 쓴다.</summary>
    internal static readonly string[] TextFontNames = ["Galmuri 7", "Galmuri 9", "Galmuri 11", UiStrings.TextEffect_FontGalmuri11Bold, "Galmuri 14"];

    private static readonly string[] TextAligns = [UiStrings.TextEffect_AlignLeft, UiStrings.TextEffect_AlignCenter, UiStrings.TextEffect_AlignRight];

    /// <summary>목록 요약에 보일 글: 첫 줄(앞뒤 공백 뺌)을 16자까지, 길면 말줄임. 빈 글이면 "(빈 글)".</summary>
    private static string TextSummary(string text)
    {
        var first = text.Split('\n')[0].Trim();
        if (first.Length == 0) return UiStrings.TextEffect_SummaryEmpty;
        return first.Length > 16 ? first[..16] + "…" : first;
    }

    /// <summary>글씨 설정: 글(여러 줄), 글꼴, 색, 기준점 X·Y, 글자·줄 간격, 줄 맞춤, 한 글자씩 나타나기.</summary>
    private void TextPanel(TextEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static TextEffect T(LayerEffect v) => (TextEffect)v;
        // 효과 창은 바꿀 때마다 미리보기만 고치고 확인할 때 한 번에 기록하므로 글자마다 바로 반영한다.
        var text = new TextBox
        {
            Text = e.Text,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            MaxLength = TextEffect.MaxLength,
            Width = 260,
            Height = 76,
            Margin = new Thickness(3),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(text, ScrollBarVisibility.Auto);
        text.TextChanged += (_, _) => set(o => T(o) with { Text = text.Text ?? "" });
        SettingsBlock(UiStrings.TextEffect_Text, text);
        SettingsRow(UiStrings.TextEffect_Font, ChoiceEditor(TextFontNames, (int)e.Font, k => set(o => T(o) with { Font = (TextFont)k })));
        SettingsRow(UiStrings.S_LayerEffectsDialog_FillSettings_F6DB7C3B67, EffectColorEditor(e, nameof(TextEffect.Color),
            ColorEditor(e.Color, v => set(o => T(o) with { Color = v })), set));
        SettingsRow(UiStrings.TextEffect_Position,
            Ui.Label("X"), NumEditor(-TextEffect.MaxPosition, TextEffect.MaxPosition, e.X, CenterKind(vertical: false), v => set(o => T(o) with { X = v })),
            Ui.Label("Y"), NumEditor(-TextEffect.MaxPosition, TextEffect.MaxPosition, e.Y, CenterKind(vertical: true), v => set(o => T(o) with { Y = v })),
            Ui.Label("px"));
        SettingsRow(UiStrings.TextEffect_LetterSpacing,
            NumEditor(TextEffect.MinLetterSpacing, TextEffect.MaxLetterSpacing, e.LetterSpacing, NumKinds.TextSpacing, v => set(o => T(o) with { LetterSpacing = v })), Ui.Label("px"),
            Ui.Label(UiStrings.TextEffect_LineSpacing),
            NumEditor(TextEffect.MinLineSpacing, TextEffect.MaxLineSpacing, e.LineSpacing, NumKinds.TextSpacing, v => set(o => T(o) with { LineSpacing = v })), Ui.Label("px"));
        SettingsRow(UiStrings.TextEffect_Align, ChoiceEditor(TextAligns, (int)e.Align, k => set(o => T(o) with { Align = (TextAlign)k })));
        SettingsRow("", CheckEditor(UiStrings.TextEffect_Typewriter, e.Typewriter, v => set(o => T(o) with { Typewriter = v })).Tip(UiStrings.TextEffect_TypewriterTip));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint(UiStrings.TextEffect_Hint));
    }
}
