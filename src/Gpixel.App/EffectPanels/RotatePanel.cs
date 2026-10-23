using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>회전 설정: 부호 있는 각도(범위 동안 도는 전체), 기준점.</summary>
    private void RotatePanel(RotateEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static RotateEffect R(LayerEffect v) => (RotateEffect)v;
        SettingsRow(UiStrings.S_RotatePanel_RotatePanel_BE26BAB163, NumEditor(-RotateEffect.MaxAngle, RotateEffect.MaxAngle, e.Angle, NumKinds.Rotation, v => set(o => R(o) with { Angle = v })), Ui.Label(UiStrings.S_RotatePanel_RotatePanel_32751BEF8A));
        SettingsRow(UiStrings.S_RotatePanel_RotatePanel_C002C942CF, ChoiceEditor(Anchors, (int)e.Anchor, k => set(o => R(o) with { Anchor = (SquashAnchor)k })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint(UiStrings.S_RotatePanel_RotatePanel_CF8C27D904
                                    + UiStrings.S_RotatePanel_RotatePanel_C0881989CD));
    }
}
