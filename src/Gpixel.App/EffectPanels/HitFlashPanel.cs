using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    private static readonly Color32 HitRed = new(255, 40, 40);

    /// <summary>피격 번쩍임 설정: 색(흰색·빨강 단추), 세기, 횟수, 모양, 공통 움직임.</summary>
    private void HitFlashPanel(HitFlashEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static HitFlashEffect H(LayerEffect v) => (HitFlashEffect)v;
        var color = ColorEditor(e.Color, v => set(o => H(o) with { Color = v }));
        SettingsRow("색", color,
            Ui.Button("흰색", (_, _) => color.Color = Color32.White, 44),
            Ui.Button("빨강", (_, _) => color.Color = HitRed, 44));
        SettingsRow("세기", NumEditor(0, 100, e.Strength, NumKinds.Strength, v => set(o => H(o) with { Strength = v })), Ui.Label("%"),
            Ui.Label("  횟수"), NumEditor(1, HitFlashEffect.MaxCount, e.Count, NumKinds.Count, v => set(o => H(o) with { Count = v })), Ui.Label("번"));
        SettingsRow("모양", ChoiceEditor(["꽉 채움 (켜졌다 꺼짐)", "점점 엷어짐"], (int)e.Shape, k => set(o => H(o) with { Shape = (HitFlashShape)k })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("범위 첫 프레임에서 바로 보이는 픽셀이 그 색으로 번쩍이고(투명도는 그대로), 횟수만큼 되풀이한 뒤 마지막 프레임은 원래 그림입니다."));
    }
}
