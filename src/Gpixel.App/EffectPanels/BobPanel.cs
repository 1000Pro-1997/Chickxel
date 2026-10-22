using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>둥실둥실 설정: 높이, 횟수, 방향(위아래 / 좌우).</summary>
    private void BobPanel(BobEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static BobEffect B(LayerEffect v) => (BobEffect)v;
        SettingsRow(UiStrings.S_BobPanel_BobPanel_58A5A73C75, NumEditor(1, BobEffect.MaxHeight, e.Height, NumKinds.Px, v => set(o => B(o) with { Height = v })), Ui.Label("px"),
            Ui.Label(UiStrings.S_BobPanel_BobPanel_93ECFDAAAF), NumEditor(1, BobEffect.MaxCycles, e.Cycles, NumKinds.Count, v => set(o => B(o) with { Cycles = v })), Ui.Label(UiStrings.S_BobPanel_BobPanel_62AE6AF173));
        SettingsRow(UiStrings.S_BobPanel_BobPanel_817E586669, ChoiceEditor([UiStrings.S_BobPanel_BobPanel_178B12E7C6, UiStrings.S_BobPanel_BobPanel_EC9342821C], e.Axis == EffectAxis.Horizontal ? 1 : 0,
            k => set(o => B(o) with { Axis = k == 1 ? EffectAxis.Horizontal : EffectAxis.Vertical })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint(UiStrings.S_BobPanel_BobPanel_A229A39E90));
    }
}
