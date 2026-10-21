using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    private static readonly string[] BlinkHides = ["완전 투명", "반투명", "디더 50%"];

    /// <summary>깜빡이기 설정: 간격(프레임 수 / 범위 안 횟수), 숨김 방식(+ 반투명 불투명도).</summary>
    private void BlinkPanel(BlinkEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static BlinkEffect B(LayerEffect v) => (BlinkEffect)v;
        var on = NumEditor(1, BlinkEffect.MaxFrames, e.OnFrames, NumKinds.Frames, v => set(o => B(o) with { OnFrames = v }));
        var off = NumEditor(1, BlinkEffect.MaxFrames, e.OffFrames, NumKinds.Frames, v => set(o => B(o) with { OffFrames = v }));
        var count = NumEditor(1, BlinkEffect.MaxCount, e.Count, NumKinds.Count, v => set(o => B(o) with { Count = v }));
        void Sync(BlinkTiming t)
        {
            on.IsEnabled = off.IsEnabled = t == BlinkTiming.Frames;
            count.IsEnabled = t == BlinkTiming.Count;
        }
        Sync(e.Timing);
        SettingsRow("간격", ChoiceEditor(["프레임 수로", "범위 안 횟수로"], (int)e.Timing, k =>
        {
            Sync((BlinkTiming)k);
            set(o => B(o) with { Timing = (BlinkTiming)k });
        }));
        SettingsSubRow("보임", on, Ui.Label("숨김"), off, Ui.Label("프레임"));
        SettingsSubRow("횟수", count, Ui.Label("번 (범위 안)"));
        var opacity = NumEditor(0, 100, e.Opacity, NumKinds.Opacity, v => set(o => B(o) with { Opacity = v }));
        opacity.IsEnabled = e.Hide == BlinkHide.Translucent;
        SettingsRow("숨김 방식", ChoiceEditor(BlinkHides, (int)e.Hide, k =>
        {
            opacity.IsEnabled = (BlinkHide)k == BlinkHide.Translucent;
            set(o => B(o) with { Hide = (BlinkHide)k });
        }), opacity, Ui.Label("%"));
        AddMotionEditors(Selected, set, timing: false);
        _settings.Children.Add(Hint("프레임 수로: 범위 첫 프레임부터 보임 N프레임 → 숨김 M프레임을 되풀이합니다. 횟수로: 범위의 프레임을 보임·숨김 칸으로 고르게 나눠 그 횟수만큼 숨습니다(첫 프레임은 보임)."
                                    + " 디더 50%는 바둑판 칸만 남깁니다(그림을 옮겨도 무늬가 그대로)."));
    }
}
