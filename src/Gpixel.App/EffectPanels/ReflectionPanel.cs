using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>반사 설정: 방향, 간격, 높이, 불투명도 시작·끝, 디더 흐리기, 색 입히기, 물결.</summary>
    private void ReflectionPanel(ReflectionEffect m, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static ReflectionEffect M(LayerEffect v) => (ReflectionEffect)v;
        SettingsRow("방향", ChoiceEditor(["아래 (바닥·물)", "위 (천장)"], m.Above ? 1 : 0, k => set(o => M(o) with { Above = k == 1 })));
        SettingsRow("간격", NumEditor(0, ReflectionEffect.MaxGap, m.Gap, NumKinds.Px, v => set(o => M(o) with { Gap = v })), Ui.Label("px"),
            Ui.Label("  높이"), NumEditor(1, 100, m.Height, NumKinds.Portion, v => set(o => M(o) with { Height = v })), Ui.Label("%"));
        SettingsRow("불투명도", NumEditor(0, 100, m.OpacityStart, NumKinds.Opacity, v => set(o => M(o) with { OpacityStart = v })), Ui.Label("% →"),
            NumEditor(0, 100, m.OpacityEnd, NumKinds.Opacity, v => set(o => M(o) with { OpacityEnd = v })), Ui.Label("%"));
        SettingsRow("", CheckEditor("디더로 흐리기 (반투명 대신 무늬)", m.DitherFade, v => set(o => M(o) with { DitherFade = v })));
        SettingsRow("색 입히기", EffectColorEditor(m, nameof(ReflectionEffect.Tint),
            ColorEditor(m.Tint, v => set(o => M(o) with { Tint = v })), set));
        SettingsRow("물결", NumEditor(0, ReflectionEffect.MaxWave, m.Wave, NumKinds.Px, v => set(o => M(o) with { Wave = v })), Ui.Label("px (줄마다 가로로 흔듦, 0 = 끔)"));
        _settings.Children.Add(Hint("그 프레임 그림을 위아래로 뒤집어 간격만큼 띄워 뒤에 깝니다(높이 = 그림 높이의 몇 %가 비칠지)."
                                    + " 그림 쪽 줄이 시작 불투명도, 끝 줄이 끝 불투명도입니다. 색 입히기의 투명도 = 물드는 세기(0 = 안 물들임)."));
    }
}
