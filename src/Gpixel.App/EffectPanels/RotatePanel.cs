using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>회전 설정: 각도(범위 동안 도는 전체), 방향, 기준점.</summary>
    private void RotatePanel(RotateEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static RotateEffect R(LayerEffect v) => (RotateEffect)v;
        SettingsRow(UiStrings.S_RotatePanel_RotatePanel_BE26BAB163, NumEditor(0, RotateEffect.MaxAngle, e.Angle, NumKinds.Rotation, v => set(o => R(o) with { Angle = v })), Ui.Label(UiStrings.S_RotatePanel_RotatePanel_32751BEF8A));
        SettingsRow(UiStrings.S_RotatePanel_RotatePanel_CE92044AC2, ChoiceEditor([UiStrings.S_RotatePanel_RotatePanel_EFD49131D2, UiStrings.S_RotatePanel_RotatePanel_C0A4488527], e.Clockwise ? 0 : 1, k => set(o => R(o) with { Clockwise = k == 0 })));
        SettingsRow(UiStrings.S_RotatePanel_RotatePanel_C002C942CF, ChoiceEditor(Anchors, (int)e.Anchor, k => set(o => R(o) with { Anchor = (SquashAnchor)k })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint(UiStrings.S_RotatePanel_RotatePanel_CF8C27D904
                                    + UiStrings.S_RotatePanel_RotatePanel_C0881989CD));
    }
}
