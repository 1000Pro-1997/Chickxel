using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>그라데이션 덮기: 예전 A/B 방식 또는 색·불투명도 정지점 방식.</summary>
    private void GradientPanel(GradientOverlayEffect g, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static GradientOverlayEffect G(LayerEffect v) => (GradientOverlayEffect)v;
        bool hasGradient = g.ColorGradients.TryGetValue("Gradient", out var saved);
        var mode = ChoiceEditor(["일반색 (A → B)", "그라데이션 (정지점)"], hasGradient ? 1 : 0, _ => { });
        SettingsRow("색 방식", mode);
        var angleNum = NumEditor(0, 359, g.Angle, NumKinds.Angle, v => set(o => G(o) with { Angle = v }));
        var angleRow = SettingsRow("각도", angleNum, Ui.Label("° (A → B, 270 = 위 → 아래)"));
        var a = ColorEditor(g.ColorA, v => set(o => G(o) with { ColorA = v }));
        var b = ColorEditor(g.ColorB, v => set(o => G(o) with { ColorB = v }));
        var aRow = SettingsRow("색 A", a);
        var bRow = SettingsRow("색 B", b, Ui.IconButton(VectorIcon.Swap, "색 A와 B를 맞바꿉니다", (_, _) => (a.Color, b.Color) = (b.Color, a.Color), 32));
        var editor = new GradientEditor(spatialAngle: true);
        var gradientBox = Ui.Group("그라데이션 정지점", editor);
        _settings.Children.Add(gradientBox);
        var initial = saved ?? new EffectColorGradient
        {
            Stops = [new GradientStop(0, g.ColorA), new GradientStop(1, g.ColorB)],
            AlphaStops = EditorContext.DefaultGradientAlphaStops,
            Angle = g.Angle
        };
        editor.Load(initial.Stops, initial.AlphaStops, initial.Interpolation, initial.Dither, initial.Angle);
        void Save(EffectColorGradient? gradient)
        {
            set(o =>
            {
                var colors = new Dictionary<string, EffectColorGradient>(StringComparer.Ordinal);
                foreach (var (slot, value) in o.ColorGradients) colors[slot] = value;
                if (gradient is null) colors.Remove("Gradient");
                else colors["Gradient"] = gradient;
                return o with { ColorGradients = colors };
            });
        }
        editor.Changed += () => Save(new EffectColorGradient
        {
            Stops = editor.Stops,
            AlphaStops = editor.AlphaStops,
            Angle = editor.Angle,
            Interpolation = editor.Interpolation,
            Dither = editor.Dither
        });
        void ShowMode()
        {
            bool gradient = mode.SelectedIndex == 1;
            ((Control)angleRow.Parent!).IsVisible = !gradient;
            ((Control)aRow.Parent!).IsVisible = !gradient;
            ((Control)bRow.Parent!).IsVisible = !gradient;
            gradientBox.IsVisible = gradient;
        }
        ShowMode();
        mode.SelectionChanged += (_, _) =>
        {
            if (mode.SelectedIndex < 0) return;
            ShowMode();
            if (mode.SelectedIndex == 1)
            {
                var value = new EffectColorGradient
                {
                    Stops = hasGradient ? editor.Stops : [new GradientStop(0, a.Color), new GradientStop(1, b.Color)],
                    AlphaStops = hasGradient ? editor.AlphaStops : EditorContext.DefaultGradientAlphaStops,
                    Angle = hasGradient ? editor.Angle : (int)angleNum.NumValue(),
                    Interpolation = editor.Interpolation,
                    Dither = editor.Dither
                };
                editor.Load(value.Stops, value.AlphaStops, value.Interpolation, value.Dither, value.Angle);
                hasGradient = true;
                Save(value);
            }
            else Save(null);
        };
        SettingsRow("세기", NumEditor(0, 100, g.Strength, NumKinds.Strength, v => set(o => G(o) with { Strength = v })), Ui.Label("%"));
        SettingsRow("섞기", ChoiceEditor(Blends, (int)g.Blend, k => set(o => G(o) with { Blend = (EffectBlend)k })));
        SettingsRow("디더", ChoiceEditor(Dithers, (int)g.Dither, k => set(o => G(o) with { Dither = (EffectDither)k })));
        _settings.Children.Add(Hint("일반색은 A에서 B로 색이 바뀝니다. 그라데이션은 원하는 만큼 색·불투명도 정지점을 추가하고 각도·보간·디더를 조절합니다."
                                    + " 두 방식 모두 보이는 픽셀에만 섞으며 원래 픽셀의 투명도는 유지합니다."));
    }
}
