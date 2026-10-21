using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>회전 설정: 각도(범위 동안 도는 전체), 방향, 기준점.</summary>
    private void RotatePanel(RotateEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static RotateEffect R(LayerEffect v) => (RotateEffect)v;
        SettingsRow("각도", NumEditor(0, RotateEffect.MaxAngle, e.Angle, NumKinds.Rotation, v => set(o => R(o) with { Angle = v })), Ui.Label("° (범위 동안 모두, 360 = 한 바퀴)"));
        SettingsRow("방향", ChoiceEditor(["시계 방향", "반시계 방향"], e.Clockwise ? 0 : 1, k => set(o => R(o) with { Clockwise = k == 0 })));
        SettingsRow("기준점", ChoiceEditor(Anchors, (int)e.Anchor, k => set(o => R(o) with { Anchor = (SquashAnchor)k })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("RotSprite 방식으로 돌립니다(그림을 Scale2x로 8배 키워 돌린 뒤 원래 크기로 집음 — 새 색·반투명 없이 선이 덜 깨짐). 90°의 배수는 손실 없이, 한 바퀴 끝은 원래 그림과 같습니다."
                                    + " 돌린 그림이 닿을 수 있는 만큼 부위 스프라이트에 여백이 생깁니다."));
    }
}
