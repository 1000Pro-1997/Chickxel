using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>잔상 설정: 개수, 간격, 불투명도, 색 입히기.</summary>
    private void AfterimagePanel(AfterimageEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static AfterimageEffect A(LayerEffect v) => (AfterimageEffect)v;
        SettingsRow("개수", NumEditor(1, AfterimageEffect.MaxCount, e.Count, NumKinds.Copies, v => set(o => A(o) with { Count = v })), Ui.Label("개"),
            Ui.Label("  간격"), NumEditor(1, AfterimageEffect.MaxInterval, e.Interval, NumKinds.Frames, v => set(o => A(o) with { Interval = v })), Ui.Label("프레임"));
        SettingsRow("불투명도", NumEditor(1, 100, e.Opacity, NumKinds.Opacity, v => set(o => A(o) with { Opacity = v })), Ui.Label("% (가장 가까운 잔상, 멀수록 옅게)"));
        var tint = EffectColorEditor(e, nameof(AfterimageEffect.TintColor),
            ColorEditor(e.TintColor, v => set(o => A(o) with { TintColor = v })), set);
        tint.IsEnabled = e.Tint;
        SettingsCheckRow(CheckEditor("색 입히기", e.Tint, v =>
        {
            tint.IsEnabled = v;
            set(o => A(o) with { Tint = v });
        }), tint);
        AddMotionEditors(Selected, set, timing: false);
        _settings.Children.Add(Hint("앞 프레임에 다른 효과를 그 프레임의 진행대로 적용한 모습을 지금 그림 뒤에 점점 옅게 깝니다. 잔상 자체는 반복 적용하지 않고 효과 범위 안 프레임만 씁니다."
                                    + " 색 입히기 색의 투명도 = 물들이는 세기."));
    }
}
