using Gpixel.Core.Edit;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

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
        _settings.Children.Add(Ui.Group(UiStrings.S_GradientPanel_GradientPanel_BF99C1BE30, editor));
        SettingsRow(UiStrings.S_GradientPanel_GradientPanel_A91464E1CE, NumEditor(0, 100, g.Strength, NumKinds.Strength,
            value => set(effect => G(effect) with { Strength = value })), Ui.Label("%"));
        SettingsRow(UiStrings.S_GradientPanel_GradientPanel_B93B4EB88D, ChoiceEditor(Blends, (int)g.Blend,
            index => set(effect => G(effect) with { Blend = (EffectBlend)index })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint(UiStrings.S_GradientPanel_GradientPanel_07BCE35122
                                    + UiStrings.S_GradientPanel_GradientPanel_002A41B539));
    }
}
