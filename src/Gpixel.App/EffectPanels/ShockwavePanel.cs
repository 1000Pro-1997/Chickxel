using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>충격파 고리 설정: 중심(+ 직접 X·Y), 최대 반지름, 두께, 색, 사라짐, 앞/뒤.</summary>
    private void ShockwavePanel(ShockwaveEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static ShockwaveEffect W(LayerEffect v) => (ShockwaveEffect)v;
        var cx = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, e.CenterX, CenterKind(vertical: false), v => set(o => W(o) with { CenterX = v }));
        var cy = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, e.CenterY, CenterKind(vertical: true), v => set(o => W(o) with { CenterY = v }));
        cx.IsEnabled = cy.IsEnabled = e.Center == ExplodeCenter.Custom;
        SettingsRow("중심", ChoiceEditor(Centers, (int)e.Center, k =>
        {
            cx.IsEnabled = cy.IsEnabled = (ExplodeCenter)k == ExplodeCenter.Custom;
            set(o => W(o) with { Center = (ExplodeCenter)k });
        }));
        SettingsSubRow("X", cx, Ui.Label("Y"), cy, Ui.Label("px (원본 왼쪽 위 기준)"));
        SettingsRow("최대 반지름", NumEditor(1, ShockwaveEffect.MaxRadius, e.Radius, NumKinds.Distance, v => set(o => W(o) with { Radius = v })), Ui.Label("px"),
            Ui.Label("  두께"), NumEditor(1, ShockwaveEffect.MaxThickness, e.Thickness, NumKinds.Thickness, v => set(o => W(o) with { Thickness = v })), Ui.Label("px"));
        SettingsRow("색", ColorEditor(e.Color, v => set(o => W(o) with { Color = v })));
        SettingsRow("", CheckEditor("사라짐 (퍼지며 옅어짐)", e.Fade, v => set(o => W(o) with { Fade = v })));
        SettingsRow("위치", ChoiceEditor(["그림 앞 (위에)", "그림 뒤 (아래에)"], e.Behind ? 1 : 0, k => set(o => W(o) with { Behind = k == 1 })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint("중심 픽셀에서 고리가 최대 반지름까지 퍼집니다(두께 1px = 중점 원 — 빈틈·겹침 없는 픽셀 원). 범위 첫 프레임은 반지름 0이라 보이지 않고, 사방으로 반지름만큼 여백이 생깁니다."));
    }
}
