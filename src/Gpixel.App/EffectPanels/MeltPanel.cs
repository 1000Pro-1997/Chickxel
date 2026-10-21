using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>녹아내리기 설정: 거리, 불규칙함, 늘어짐, 사라짐, 시드.</summary>
    private void MeltPanel(MeltEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static MeltEffect M(LayerEffect v) => (MeltEffect)v;
        SettingsRow("거리", NumEditor(1, MeltEffect.MaxDistance, e.Distance, NumKinds.Distance, v => set(o => M(o) with { Distance = v })), Ui.Label("px (끝까지 내려가는 거리)"));
        SettingsRow("불규칙함", NumEditor(0, 100, e.Irregularity, NumKinds.Random, v => set(o => M(o) with { Irregularity = v })), Ui.Label("% (열마다 늦게·느리게)"));
        SettingsRow("", CheckEditor("늘어짐 (흘러내린 자국)", e.Stretch, v => set(o => M(o) with { Stretch = v })),
            CheckEditor("사라짐", e.Fade, v => set(o => M(o) with { Fade = v })));
        var seed = NumEditor(0, MeltEffect.MaxSeed, e.Seed, NumKinds.Seed, v => set(o => M(o) with { Seed = v }));
        var reroll = Ui.IconButton(VectorIcon.Dice, "다른 녹는 모양(시드를 무작위로)", (_, _) => seed.Value = System.Random.Shared.Next(0, MeltEffect.MaxSeed + 1), 36);
        SettingsRow("시드", seed, reroll);
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("픽셀이 열마다 조금씩 다른 때·빠르기로 아래로 흘러내리고(늘어짐이면 아래쪽일수록 더 내려가 자국이 남음), 사라짐이면 다 내려가면서 투명해집니다."
                                    + " 범위 첫 프레임은 그림 그대로, 아래로 거리만큼 여백이 생깁니다."));
    }
}
