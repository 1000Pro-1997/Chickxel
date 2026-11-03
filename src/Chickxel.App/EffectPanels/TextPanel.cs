using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>효과 창·타임라인 표시: 이름, 설명, 타임라인 색, 설정 칸, 목록 요약.</summary>
    private static EffectUi TextUi => EffectUi.For<TextEffect>(TextDefinition.Id, UiStrings.TextEffect_Name, UiStrings.TextEffect_Description,
        Avalonia.Media.Color.FromRgb(232, 180, 64), (dialog, e, set) => dialog.TextPanel(e, set),
        t => string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.TextEffect_Summary, TextSummary(t.Text), FontPicker.Label(t.FontName))
             + (t.Typewriter ? UiStrings.TextEffect_SummaryTypewriter : ""));

    private static readonly string[] TextAligns = [UiStrings.TextEffect_AlignLeft, UiStrings.TextEffect_AlignCenter, UiStrings.TextEffect_AlignRight];

    /// <summary>글자 크기(px, 0 = 글꼴 기본 크기).</summary>
    private static readonly NumKind TextSizeKind = new("text.size", "px", [0, 7, 8, 9, 10, 11, 12, 14, 16, 18, 24, 32, 48, 64],
        v => v == 0 ? UiStrings.TextTool_SizeNative : $"{v} px");
    /// <summary>글자 가로·세로 배율(%).</summary>
    private static readonly NumKind TextScaleKind = new("text.scale", "%", [25, 50, 75, 100, 125, 150, 200, 300, 400]);
    /// <summary>글 상자 가로·세로(px, 0 = 자동).</summary>
    private static readonly NumKind TextBoxKind = new("text.box", "px", [0, 16, 24, 32, 48, 64, 96, 128, 256],
        v => v == 0 ? UiStrings.TextEffect_BoxAuto : $"{v} px");

    /// <summary>목록 요약에 보일 글: 첫 줄(앞뒤 공백 뺌)을 16자까지, 길면 말줄임. 빈 글이면 "(빈 글)".</summary>
    private static string TextSummary(string text)
    {
        var first = text.Split('\n')[0].Trim();
        if (first.Length == 0) return UiStrings.TextEffect_SummaryEmpty;
        return first.Length > 16 ? first[..16] + "…" : first;
    }

    /// <summary>글씨 설정(효과 창): <see cref="AddTextFields"/> + 움직임 범위 안내.</summary>
    private void TextPanel(TextEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        AddTextFields(_form, e, set, CenterKind(vertical: false), CenterKind(vertical: true));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint(UiStrings.TextEffect_Hint));
    }

    /// <summary>
    /// 글씨 설정 줄: 글(여러 줄), 글꼴(없는 글꼴 경고), 크기·굵게·기울임·매끄럽게(벡터 글꼴만), 색, 기준점 X·Y, 상자 가로·세로,
    /// 가로·세로 배율, 글자·줄 간격, 줄 맞춤, 한 글자씩 나타나기. 기준점 칸 예시는 <paramref name="centerX"/>·<paramref name="centerY"/>.
    /// </summary>
    private static void AddTextFields(EffectForm form, TextEffect e, Action<Func<LayerEffect, LayerEffect>> set, NumKind centerX, NumKind centerY)
    {
        static TextEffect T(LayerEffect v) => (TextEffect)v;
        // 글자마다 바로 반영한다(효과 창은 미리보기만 고치고 확인할 때 한 번에, 독은 잠깐 사이의 변경을 실행취소 한 단계로 묶음).
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
        form.Block(UiStrings.TextEffect_Text, text);

        var font = new FontPicker(170) { Name = e.FontName };
        var missing = Ui.Label(UiStrings.TextEffect_FontMissingTip);
        missing.Foreground = AppTheme.WarningBrush;
        missing.TextWrapping = TextWrapping.Wrap;
        missing.MaxWidth = 290;
        var smooth = CheckEditor(UiStrings.TextEffect_Smooth, e.Smooth, v => set(o => T(o) with { Smooth = v })).Tip(UiStrings.TextEffect_SmoothTip);
        // 매끄럽게는 벡터 글꼴(TTF·OTF)일 때만, 없는 글꼴은 경고(기본 글꼴로 그림).
        void FontState()
        {
            missing.IsVisible = font.IsMissing;
            smooth.IsEnabled = font.IsVector;
        }
        font.Picked += name =>
        {
            set(o => T(o) with { FontName = name });
            FontState();
        };
        font.ListChanged += FontState;
        var addFont = Ui.IconButton(VectorIcon.Plus, UiStrings.TextTool_AddFontTip, (_, _) =>
        {
            if (FontPicker.AddFont(form.Owner) is not { } name) return;
            font.Name = name;
            set(o => T(o) with { FontName = name });
            FontState();
        }, 26);
        var openFonts = Ui.IconButton(VectorIcon.FolderOpen, UiStrings.TextTool_OpenFontFolder, (_, _) => FontPicker.OpenFolder(), 26);
        form.Row(UiStrings.TextEffect_Font, font.Combo, addFont, openFonts);
        form.Row("", missing);
        FontState();

        var bold = CheckEditor(UiStrings.TextEffect_Bold, e.Bold, v => set(o => T(o) with { Bold = v }));
        var italic = CheckEditor(UiStrings.TextEffect_Italic, e.Italic, v => set(o => T(o) with { Italic = v }));
        form.Row(UiStrings.TextEffect_Size,
            NumEditor(0, TextEffect.MaxSize, e.Size, TextSizeKind, v => set(o => T(o) with { Size = v })).Tip(UiStrings.TextEffect_SizeTip), Ui.Label("px"),
            bold, italic);
        form.Row("", smooth);
        form.Row(UiStrings.S_LayerEffectsDialog_FillSettings_F6DB7C3B67, EffectColorEditor(e, nameof(TextEffect.Color),
            ColorEditor(e.Color, v => set(o => T(o) with { Color = v })), set));
        form.Row(UiStrings.TextEffect_Position,
            Ui.Label("X"), NumEditor(-TextEffect.MaxPosition, TextEffect.MaxPosition, e.X, centerX, v => set(o => T(o) with { X = v })),
            Ui.Label("Y"), NumEditor(-TextEffect.MaxPosition, TextEffect.MaxPosition, e.Y, centerY, v => set(o => T(o) with { Y = v })),
            Ui.Label("px"));
        form.Row(UiStrings.TextEffect_Box,
            Ui.Label(UiStrings.TextEffect_Width), NumEditor(0, TextEffect.MaxBoxSize, e.BoxWidth, TextBoxKind, v => set(o => T(o) with { BoxWidth = v })),
            Ui.Label(UiStrings.TextEffect_Height), NumEditor(0, TextEffect.MaxBoxSize, e.BoxHeight, TextBoxKind, v => set(o => T(o) with { BoxHeight = v })),
            Ui.Label("px")).Tip(UiStrings.TextEffect_BoxTip);
        form.Row(UiStrings.TextEffect_Scale,
            Ui.Label(UiStrings.TextEffect_Width), NumEditor(TextEffect.MinScale, TextEffect.MaxScale, e.ScaleX, TextScaleKind, v => set(o => T(o) with { ScaleX = v })).Tip(UiStrings.TextEffect_ScaleXTip),
            Ui.Label(UiStrings.TextEffect_Height), NumEditor(TextEffect.MinScale, TextEffect.MaxScale, e.ScaleY, TextScaleKind, v => set(o => T(o) with { ScaleY = v })).Tip(UiStrings.TextEffect_ScaleYTip),
            Ui.Label("%"));
        form.Row(UiStrings.TextEffect_LetterSpacing,
            NumEditor(TextEffect.MinLetterSpacing, TextEffect.MaxLetterSpacing, e.LetterSpacing, NumKinds.TextSpacing, v => set(o => T(o) with { LetterSpacing = v })), Ui.Label("px"),
            Ui.Label(UiStrings.TextEffect_LineSpacing),
            NumEditor(TextEffect.MinLineSpacing, TextEffect.MaxLineSpacing, e.LineSpacing, NumKinds.TextSpacing, v => set(o => T(o) with { LineSpacing = v })), Ui.Label("px"));
        form.Row(UiStrings.TextEffect_Align, ChoiceEditor(TextAligns, (int)e.Align, k => set(o => T(o) with { Align = (TextAlign)k })));
        form.Row("", CheckEditor(UiStrings.TextEffect_Typewriter, e.Typewriter, v => set(o => T(o) with { Typewriter = v })).Tip(UiStrings.TextEffect_TypewriterTip));
    }
}
