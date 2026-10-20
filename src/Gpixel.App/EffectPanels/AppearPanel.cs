using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    private static readonly string[] AppearStyles = ["투명도", "디더 (4×4 Bayer)", "쓸기", "픽셀 흩어짐"];
    private static readonly string[] WipeFroms = ["위", "아래", "왼쪽", "오른쪽"];

    /// <summary>나타나기 / 사라지기 설정: 모드, 방식, 쓸기 시작 쪽·가장자리, 흩어짐 시드.</summary>
    private void AppearPanel(AppearEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static AppearEffect A(LayerEffect v) => (AppearEffect)v;
        SettingsRow(Ui.Label("모드"), ChoiceEditor(["나타나기", "사라지기"], e.Disappear ? 1 : 0, k => set(o => A(o) with { Disappear = k == 1 })));
        var from = ChoiceEditor(WipeFroms, (int)e.From, k => set(o => A(o) with { From = (WipeDirection)k }));
        var edge = ChoiceEditor(["딱딱하게", "디더 1px", "디더 2px"], e.Edge, k => set(o => A(o) with { Edge = k }));
        var seed = NumEditor(0, AppearEffect.MaxSeed, e.Seed, v => set(o => A(o) with { Seed = v }));
        var reroll = Ui.Button("🎲", (_, _) => seed.Value = System.Random.Shared.Next(0, AppearEffect.MaxSeed + 1), 36).Tip("다른 흩어짐(시드를 무작위로)");
        void Sync(AppearStyle s)
        {
            from.IsEnabled = edge.IsEnabled = s == AppearStyle.Wipe;
            seed.IsEnabled = reroll.IsEnabled = s == AppearStyle.Scatter;
        }
        Sync(e.Style);
        SettingsRow(Ui.Label("방식"), ChoiceEditor(AppearStyles, (int)e.Style, k =>
        {
            Sync((AppearStyle)k);
            set(o => A(o) with { Style = (AppearStyle)k });
        }));
        SettingsRow(Ui.Label("    쓸기 시작"), from);
        SettingsRow(Ui.Label("    가장자리"), edge);
        SettingsRow(Ui.Label("    시드"), seed, reroll);
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("나타나기는 범위 첫 프레임이 비어 있고 마지막 프레임이 그림 그대로, 사라지기는 그 반대입니다. 디더·흩어짐·쓸기는 픽셀이 켜지고 꺼지기만 해서(반투명 없음) 픽셀 아트에 어울립니다."
                                    + " 쓸기는 고른 쪽에서 시작합니다(사라지기면 그 쪽부터 지워짐)."));
    }
}
