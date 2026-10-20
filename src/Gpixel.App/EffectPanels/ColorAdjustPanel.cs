using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    private static readonly string[] AdjustModes = ["흑백", "세피아", "색조 이동", "밝기·대비", "채도"];

    private static string AdjustSummary(ColorAdjustEffect c) => AdjustModes[(int)c.Mode] + c.Mode switch
    {
        ColorAdjustMode.HueShift => $" {c.Hue:+0;-0;0}°",
        ColorAdjustMode.BrightnessContrast => $" {c.Brightness:+0;-0;0} / {c.Contrast:+0;-0;0}",
        ColorAdjustMode.Saturation => $" {c.Saturation:+0;-0;0}%",
        _ => $" {c.Amount}%",
    };

    /// <summary>색조 보정 설정: 방식(바꾸면 그 방식의 값만 보임)과 값.</summary>
    private void ColorAdjustPanel(ColorAdjustEffect c, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static ColorAdjustEffect C(LayerEffect v) => (ColorAdjustEffect)v;
        SettingsRow(Ui.Label("방식"), ChoiceEditor(AdjustModes, (int)c.Mode, k =>
        {
            set(o => C(o) with { Mode = (ColorAdjustMode)k });
            ShowSettings(); // 그 방식의 값 칸으로
        }));
        switch (c.Mode)
        {
            case ColorAdjustMode.Grayscale:
            case ColorAdjustMode.Sepia:
                SettingsRow(Ui.Label("양"), NumEditor(0, 100, c.Amount, v => set(o => C(o) with { Amount = v })), Ui.Label("%"));
                break;
            case ColorAdjustMode.HueShift:
                SettingsRow(Ui.Label("색상"), NumEditor(-ColorAdjustEffect.MaxHue, ColorAdjustEffect.MaxHue, c.Hue, v => set(o => C(o) with { Hue = v })), Ui.Label("° 돌리기"));
                break;
            case ColorAdjustMode.BrightnessContrast:
                SettingsRow(Ui.Label("밝기"), NumEditor(-100, 100, c.Brightness, v => set(o => C(o) with { Brightness = v })), Ui.Label("%"),
                    Ui.Label("  대비"), NumEditor(-100, 100, c.Contrast, v => set(o => C(o) with { Contrast = v })), Ui.Label("%"));
                break;
            case ColorAdjustMode.Saturation:
                SettingsRow(Ui.Label("채도"), NumEditor(-100, 100, c.Saturation, v => set(o => C(o) with { Saturation = v })), Ui.Label("% (−100 = 흑백)"));
                break;
        }
        _settings.Children.Add(Hint("보이는 픽셀의 색만 바꿉니다(투명도는 그대로). 셀 그림은 그대로라 언제든 끄거나 되돌릴 수 있습니다."));
    }
}
