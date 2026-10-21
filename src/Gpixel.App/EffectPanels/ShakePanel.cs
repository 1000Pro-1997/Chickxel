using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    private static readonly string[] Axes = ["가로", "세로", "둘 다"];

    /// <summary>흔들기 설정: 방향, 세기, 횟수, 줄어듦, 움직임(무작위 시드 / 규칙적).</summary>
    private void ShakePanel(ShakeEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static ShakeEffect S(LayerEffect v) => (ShakeEffect)v;
        SettingsRow("방향", ChoiceEditor(Axes, (int)e.Axis, k => set(o => S(o) with { Axis = (EffectAxis)k })));
        SettingsRow("세기", NumEditor(1, ShakeEffect.MaxAmount, e.Amount, NumKinds.Px, v => set(o => S(o) with { Amount = v })), Ui.Label("px"),
            Ui.Label("  횟수"), NumEditor(1, ShakeEffect.MaxCount, e.Count, NumKinds.Count, v => set(o => S(o) with { Count = v })), Ui.Label("번"));
        SettingsRow("", CheckEditor("줄어듦 (끝으로 갈수록 약하게)", e.Decay, v => set(o => S(o) with { Decay = v })));
        var seed = NumEditor(0, ShakeEffect.MaxSeed, e.Seed, NumKinds.Seed, v => set(o => S(o) with { Seed = v }));
        var reroll = Ui.Button("🎲", (_, _) => seed.Value = System.Random.Shared.Next(0, ShakeEffect.MaxSeed + 1), 36).Tip("다른 흔들림(시드를 무작위로)");
        seed.IsEnabled = reroll.IsEnabled = !e.Regular;
        SettingsRow("움직임", ChoiceEditor(["무작위 (시드)", "규칙적 (사인)"], e.Regular ? 1 : 0, k =>
        {
            seed.IsEnabled = reroll.IsEnabled = k == 0;
            set(o => S(o) with { Regular = k == 1 });
        }));
        SettingsSubRow("시드", seed, reroll);
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("그림 전체를 정수 픽셀만큼 옮겨 떱니다(많아야 세기 px). 시작·끝 프레임은 제자리이고, 흔들리는 만큼 부위 스프라이트에 여백이 생깁니다."));
    }
}
