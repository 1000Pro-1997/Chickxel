using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>반사 설정: 방향, 간격, 높이, 불투명도 시작·끝, 디더 흐리기, 색 입히기, 물결.</summary>
    private void ReflectionPanel(ReflectionEffect m, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static ReflectionEffect M(LayerEffect v) => (ReflectionEffect)v;
        SettingsRow(UiStrings.S_ReflectionPanel_ReflectionPanel_0B2DCDD256, ChoiceEditor([UiStrings.S_ReflectionPanel_ReflectionPanel_4FE25252B7, UiStrings.S_ReflectionPanel_ReflectionPanel_15D1419195], m.Above ? 1 : 0, k => set(o => M(o) with { Above = k == 1 })));
        SettingsRow(UiStrings.S_ReflectionPanel_ReflectionPanel_5E956839D2, NumEditor(0, ReflectionEffect.MaxGap, m.Gap, NumKinds.Px, v => set(o => M(o) with { Gap = v })), Ui.Label("px"),
            Ui.Label(UiStrings.S_ReflectionPanel_ReflectionPanel_436371597D), NumEditor(1, 100, m.Height, NumKinds.Portion, v => set(o => M(o) with { Height = v })), Ui.Label("%"));
        SettingsRow(UiStrings.S_ReflectionPanel_ReflectionPanel_3775129E0B, NumEditor(0, 100, m.OpacityStart, NumKinds.Opacity, v => set(o => M(o) with { OpacityStart = v })), Ui.Label("% →"),
            NumEditor(0, 100, m.OpacityEnd, NumKinds.Opacity, v => set(o => M(o) with { OpacityEnd = v })), Ui.Label("%"));
        SettingsRow("", CheckEditor(UiStrings.S_ReflectionPanel_ReflectionPanel_61D47D3F6B, m.DitherFade, v => set(o => M(o) with { DitherFade = v })));
        SettingsRow(UiStrings.S_ReflectionPanel_ReflectionPanel_742EB29F31, EffectColorEditor(m, nameof(ReflectionEffect.Tint),
            ColorEditor(m.Tint, v => set(o => M(o) with { Tint = v })), set));
        SettingsRow(UiStrings.S_ReflectionPanel_ReflectionPanel_F203E882CA, NumEditor(0, ReflectionEffect.MaxWave, m.Wave, NumKinds.Px, v => set(o => M(o) with { Wave = v })), Ui.Label(UiStrings.S_ReflectionPanel_ReflectionPanel_B7DC6AC8F7));
        _settings.Children.Add(Hint(UiStrings.S_ReflectionPanel_ReflectionPanel_11BC092C2F
                                    + UiStrings.S_ReflectionPanel_ReflectionPanel_316A07F4E0));
    }
}
