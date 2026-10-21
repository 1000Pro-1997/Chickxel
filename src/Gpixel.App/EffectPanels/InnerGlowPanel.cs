using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>안쪽 빛 설정: 색, 두께, 세기, 섞기, 안쪽으로 약해짐.</summary>
    private void InnerGlowPanel(InnerGlowEffect g, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static InnerGlowEffect G(LayerEffect v) => (InnerGlowEffect)v;
        SettingsRow("색", ColorEditor(g.Color, v => set(o => G(o) with { Color = v })));
        SettingsRow("두께", NumEditor(1, InnerGlowEffect.MaxSize, g.Size, NumKinds.Thickness, v => set(o => G(o) with { Size = v })), Ui.Label("px"),
            Ui.Label("  세기"), NumEditor(0, 100, g.Strength, NumKinds.Strength, v => set(o => G(o) with { Strength = v })), Ui.Label("%"));
        SettingsRow("섞기", ChoiceEditor(Blends, (int)g.Blend, k => set(o => G(o) with { Blend = (EffectBlend)k })));
        SettingsRow("", CheckEditor("안쪽으로 갈수록 약하게 (계단식)", g.Fade, v => set(o => G(o) with { Fade = v })));
        _settings.Children.Add(Hint("그림 가장자리에서 안쪽으로 두께만큼 색을 섞습니다(모서리 포함 8방향 거리, 투명도는 그대로). 색의 투명도는 쓰지 않습니다."));
    }
}
