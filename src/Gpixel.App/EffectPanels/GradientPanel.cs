using Gpixel.Core.Edit;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    private static readonly string[] GradientChangeModes =
        [UiStrings.S_GradientPanel_GradientChangeModes_ColorTransition, UiStrings.S_GradientPanel_GradientChangeModes_RadialRotation];

    /// <summary>시간 그라데이션: 프레임마다 한 색 또는 그림 중심 둘레의 회전하는 색을 적용한다.</summary>
    private void GradientPanel(GradientOverlayEffect g, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static GradientOverlayEffect G(LayerEffect value) => (GradientOverlayEffect)value;
        SettingsRow(UiStrings.S_GradientPanel_GradientPanel_Mode, ChoiceEditor(GradientChangeModes, (int)g.Mode, index =>
        {
            set(effect => G(effect) with { Mode = (GradientChangeMode)index });
            ShowSettings();
        }));
        if (g.Mode == GradientChangeMode.RadialRotation)
            SettingsRow(UiStrings.GradientChange_RotationAngle,
                NumEditor(-GradientOverlayEffect.MaxAngle, GradientOverlayEffect.MaxAngle, g.Angle, NumKinds.RadialRotation,
                    value => set(effect => G(effect) with { Angle = value })), Ui.Label("°"));
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
        _settings.Children.Add(Ui.Group(g.Mode == GradientChangeMode.RadialRotation
            ? UiStrings.S_GradientPanel_GradientPanel_AngularGradient
            : UiStrings.S_GradientPanel_GradientPanel_BF99C1BE30, editor));
        SettingsRow(UiStrings.S_GradientPanel_GradientPanel_A91464E1CE, NumEditor(0, 100, g.Strength, NumKinds.Strength,
            value => set(effect => G(effect) with { Strength = value })), Ui.Label("%"));
        SettingsRow(UiStrings.S_GradientPanel_GradientPanel_B93B4EB88D, ChoiceEditor(Blends, (int)g.Blend,
            index => set(effect => G(effect) with { Blend = (EffectBlend)index })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint(g.Mode == GradientChangeMode.RadialRotation
            ? UiStrings.S_GradientPanel_GradientPanel_RadialHint
            : UiStrings.S_GradientPanel_GradientPanel_07BCE35122 + UiStrings.S_GradientPanel_GradientPanel_002A41B539));
    }
}
