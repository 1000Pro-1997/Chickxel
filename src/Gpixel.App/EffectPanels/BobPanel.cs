using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>둥실둥실 설정: 높이, 횟수, 방향(위아래 / 좌우).</summary>
    private void BobPanel(BobEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static BobEffect B(LayerEffect v) => (BobEffect)v;
        SettingsRow(Ui.Label("높이"), NumEditor(1, BobEffect.MaxHeight, e.Height, v => set(o => B(o) with { Height = v })), Ui.Label("px"),
            Ui.Label("  횟수"), NumEditor(1, BobEffect.MaxCycles, e.Cycles, v => set(o => B(o) with { Cycles = v })), Ui.Label("번"));
        SettingsRow(Ui.Label("방향"), ChoiceEditor(["위아래", "좌우"], e.Axis == EffectAxis.Horizontal ? 1 : 0,
            k => set(o => B(o) with { Axis = k == 1 ? EffectAxis.Horizontal : EffectAxis.Vertical })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("사인 곡선으로 부드럽게 오르내립니다(가운데에서 위·아래로 높이 px, 정수 픽셀). 범위 처음과 끝이 같아서 되풀이해도 끊기지 않습니다 — 범위의 마지막 프레임은 첫 프레임과 같은 모습입니다."));
    }
}
