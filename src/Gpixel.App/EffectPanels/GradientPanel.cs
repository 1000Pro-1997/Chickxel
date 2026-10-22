using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>시간 그라데이션: 공용 정지점 편집기의 색을 효과 범위 진행에 따라 보이는 픽셀에 섞는다.</summary>
    private void GradientPanel(GradientOverlayEffect g, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static GradientOverlayEffect G(LayerEffect value) => (GradientOverlayEffect)value;
        var editor = new GradientEditor(spatialAngle: false);
        editor.Load(g.Gradient.Stops, g.Gradient.AlphaStops, g.Gradient.Interpolation, g.Gradient.Dither);
        editor.Changed += () => set(effect => G(effect) with
        {
            Gradient = new EffectColorGradient
            {
                Stops = editor.Stops,
                AlphaStops = editor.AlphaStops,
                Interpolation = editor.Interpolation,
                Dither = editor.Dither,
            }
        });
        _settings.Children.Add(Ui.Group("시간 그라데이션", editor));
        SettingsRow("세기", NumEditor(0, 100, g.Strength, NumKinds.Strength,
            value => set(effect => G(effect) with { Strength = value })), Ui.Label("%"));
        SettingsRow("섞기", ChoiceEditor(Blends, (int)g.Blend,
            index => set(effect => G(effect) with { Blend = (EffectBlend)index })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("범위 첫 프레임에는 그라데이션 왼쪽 끝 색, 마지막 프레임에는 오른쪽 끝 색이 적용되고 중간 프레임은 정지점 위치대로 변합니다."
                                    + " 부드럽게는 프레임마다 한 색으로 바뀌고, 디더는 전환 중인 두 색을 픽셀 무늬로 섞습니다. 원래 픽셀의 투명도는 유지합니다."));
    }
}
