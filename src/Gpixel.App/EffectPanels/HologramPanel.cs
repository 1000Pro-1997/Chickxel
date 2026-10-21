using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>홀로그램 설정: 색·색 세기, 주사선(간격·세기·방식·흐르기), 밝은 띠, 투명도(디더), 깜빡임, 글리치(RGB 분리), 테두리 빛, 시드.</summary>
    private void HologramPanel(HologramEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static HologramEffect H(LayerEffect v) => (HologramEffect)v;
        SettingsRow("색", ColorEditor(e.Color, v => set(o => H(o) with { Color = v })));
        SettingsRow("색 세기", NumEditor(0, 100, e.TintStrength, NumKinds.Strength, v => set(o => H(o) with { TintStrength = v })), Ui.Label("%"));
        SettingsRow("주사선 간격", NumEditor(HologramEffect.MinScanGap, HologramEffect.MaxScanGap, e.ScanGap, NumKinds.ScanGap, v => set(o => H(o) with { ScanGap = v })), Ui.Label("줄"),
            Ui.Label("  세기"), NumEditor(0, 100, e.ScanStrength, NumKinds.Strength, v => set(o => H(o) with { ScanStrength = v })), Ui.Label("% (0 = 끔)"));
        SettingsSubRow("방식", ChoiceEditor(["어둡게", "투명하게"], (int)e.ScanStyle, k => set(o => H(o) with { ScanStyle = (HologramScanStyle)k })),
            CheckEditor("흐르기", e.ScanScroll, v => set(o => H(o) with { ScanScroll = v })));
        var bandSize = NumEditor(1, HologramEffect.MaxBandSize, e.BandSize, NumKinds.Thickness, v => set(o => H(o) with { BandSize = v }));
        var bandStrength = NumEditor(0, 100, e.BandStrength, NumKinds.Strength, v => set(o => H(o) with { BandStrength = v }));
        bandSize.IsEnabled = bandStrength.IsEnabled = e.Band;
        SettingsCheckRow(CheckEditor("밝은 띠", e.Band, v => { bandSize.IsEnabled = bandStrength.IsEnabled = v; set(o => H(o) with { Band = v }); }),
            Ui.Label("두께"), bandSize, Ui.Label("px"), Ui.Label("  세기"), bandStrength, Ui.Label("%"));
        SettingsRow("투명도", NumEditor(0, 100, e.Opacity, NumKinds.Opacity, v => set(o => H(o) with { Opacity = v })), Ui.Label("%"));
        SettingsRow("", CheckEditor("디더 (반투명 대신 Bayer 무늬)", e.Dither, v => set(o => H(o) with { Dither = v })));
        SettingsRow("깜빡임 빈도", NumEditor(0, 100, e.FlickerRate, NumKinds.Rate, v => set(o => H(o) with { FlickerRate = v })), Ui.Label("%"),
            Ui.Label("  세기"), NumEditor(0, 100, e.FlickerStrength, NumKinds.Strength, v => set(o => H(o) with { FlickerStrength = v })), Ui.Label("%"));
        SettingsRow("글리치 빈도", NumEditor(0, 100, e.GlitchRate, NumKinds.Rate, v => set(o => H(o) with { GlitchRate = v })), Ui.Label("%"),
            Ui.Label("  최대 밀림"), NumEditor(1, HologramEffect.MaxGlitchShift, e.GlitchShift, NumKinds.Px, v => set(o => H(o) with { GlitchShift = v })), Ui.Label("px"));
        SettingsRow("", CheckEditor("RGB 분리 (빨강·파랑 1px 어긋남)", e.RgbSplit, v => set(o => H(o) with { RgbSplit = v })).Tip("글리치 프레임에 빨강·파랑이 1px 어긋납니다."));
        var edge = NumEditor(0, 100, e.EdgeOpacity, NumKinds.Strength, v => set(o => H(o) with { EdgeOpacity = v }));
        edge.IsEnabled = e.EdgeGlow;
        SettingsCheckRow(CheckEditor("테두리 빛", e.EdgeGlow, v => { edge.IsEnabled = v; set(o => H(o) with { EdgeGlow = v }); }).Tip("그림 둘레 1px에 빛 테두리를 두릅니다."),
            Ui.Label("세기"), edge, Ui.Label("%"));
        var seed = NumEditor(0, HologramEffect.MaxSeed, e.Seed, NumKinds.Seed, v => set(o => H(o) with { Seed = v }));
        var reroll = Ui.Button("🎲", (_, _) => seed.Value = System.Random.Shared.Next(0, HologramEffect.MaxSeed + 1), 36).Tip("다른 깜빡임·글리치(시드를 무작위로)");
        SettingsRow("시드", seed, reroll);
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("보이는 픽셀을 밝기 그대로 한 색으로 물들인(명암·모양 유지) SF 홀로그램입니다. 주사선 줄마다 어둡게·투명하게 하고, 흐르기면 범위 동안 한 간격 아래로 흐릅니다."
                                    + " 밝은 띠는 범위 처음에 그림 위에서 들어와 끝에 아래로 빠져나갑니다(빠르기·반복이 주사선 흐름과 띠에 쓰임)."));
        _settings.Children.Add(Hint("깜빡임·글리치는 시드와 범위 안 프레임 번호로 정해집니다(같은 설정이면 캔버스·내보내기·GIF가 같음). 글리치 프레임엔 1~3개 가로 조각(1~3px)이 좌우로 밀리므로"
                                    + " 좌우로 최대 밀림만큼, 테두리 빛이면 사방 1px 여백이 생깁니다."));
    }
}
