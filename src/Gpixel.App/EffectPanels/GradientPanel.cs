using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>그라데이션 덮기 설정: 각도, 색 A·B(맞바꾸기), 세기, 섞기, 디더.</summary>
    private void GradientPanel(GradientOverlayEffect g, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static GradientOverlayEffect G(LayerEffect v) => (GradientOverlayEffect)v;
        SettingsRow("각도", NumEditor(0, 359, g.Angle, NumKinds.Angle, v => set(o => G(o) with { Angle = v })), Ui.Label("° (A → B, 270 = 위 → 아래)"));
        var a = ColorEditor(g.ColorA, v => set(o => G(o) with { ColorA = v }));
        var b = ColorEditor(g.ColorB, v => set(o => G(o) with { ColorB = v }));
        SettingsRow("색 A", a);
        SettingsRow("색 B", b, Ui.IconButton(VectorIcon.Swap, "색 A와 B를 맞바꿉니다", (_, _) => (a.Color, b.Color) = (b.Color, a.Color), 32));
        SettingsRow("세기", NumEditor(0, 100, g.Strength, NumKinds.Strength, v => set(o => G(o) with { Strength = v })), Ui.Label("%"));
        SettingsRow("섞기", ChoiceEditor(Blends, (int)g.Blend, k => set(o => G(o) with { Blend = (EffectBlend)k })));
        SettingsRow("디더", ChoiceEditor(Dithers, (int)g.Dither, k => set(o => G(o) with { Dither = (EffectDither)k })));
        _settings.Children.Add(Hint("보이는 픽셀에만 A에서 B로 바뀌는 색을 섞습니다(그 프레임 그림의 한쪽 끝 = A, 반대쪽 끝 = B, 투명도는 그대로)."
                                    + " 디더를 고르면 픽셀마다 A나 B 하나만 써서 픽셀아트다운 무늬 단계가 됩니다."));
    }
}
