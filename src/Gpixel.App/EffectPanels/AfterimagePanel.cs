using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>잔상 설정: 개수, 간격, 불투명도, 색 입히기.</summary>
    private void AfterimagePanel(AfterimageEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static AfterimageEffect A(LayerEffect v) => (AfterimageEffect)v;
        SettingsRow(UiStrings.S_AfterimagePanel_AfterimagePanel_A7E10C304E, NumEditor(1, AfterimageEffect.MaxCount, e.Count, NumKinds.Copies, v => set(o => A(o) with { Count = v })), Ui.Label(UiStrings.S_AfterimagePanel_AfterimagePanel_932239C718),
            Ui.Label(UiStrings.S_AfterimagePanel_AfterimagePanel_F16DC92644), NumEditor(1, AfterimageEffect.MaxInterval, e.Interval, NumKinds.Frames, v => set(o => A(o) with { Interval = v })), Ui.Label(UiStrings.S_AfterimagePanel_AfterimagePanel_29E836BF07));
        SettingsRow(UiStrings.S_AfterimagePanel_AfterimagePanel_88FF7125A4, NumEditor(1, 100, e.Opacity, NumKinds.Opacity, v => set(o => A(o) with { Opacity = v })), Ui.Label(UiStrings.S_AfterimagePanel_AfterimagePanel_3D694F080C));
        var tint = EffectColorEditor(e, nameof(AfterimageEffect.TintColor),
            ColorEditor(e.TintColor, v => set(o => A(o) with { TintColor = v })), set);
        tint.IsEnabled = e.Tint;
        SettingsCheckRow(CheckEditor(UiStrings.S_AfterimagePanel_AfterimagePanel_D0BC4AE314, e.Tint, v =>
        {
            tint.IsEnabled = v;
            set(o => A(o) with { Tint = v });
        }), tint);
        AddMotionEditors(Selected, set, timing: false);
        _settings.Children.Add(Hint(UiStrings.S_AfterimagePanel_AfterimagePanel_9CBFACCDD7
                                    + UiStrings.S_AfterimagePanel_AfterimagePanel_ABB66D8551));
    }
}
