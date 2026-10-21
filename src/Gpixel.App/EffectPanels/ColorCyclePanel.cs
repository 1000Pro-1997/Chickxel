using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>
    /// 색 순환 설정: 순환할 색(2~8개, 색마다 [−]) + [+ 색 추가]·[그림에서 색 가져오기 ▾](이 프레임 그림에 쓰인 색 — 누르면 그 색 추가), 칸 수, 방향.
    /// 색을 더하거나 빼면 설정 칸을 다시 만든다.
    /// </summary>
    private void ColorCyclePanel(ColorCycleEffect e, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static ColorCycleEffect C(LayerEffect v) => (ColorCycleEffect)v;
        static bool SameRgb(Color32 a, Color32 b) => a.R == b.R && a.G == b.G && a.B == b.B;
        void AddColors(IEnumerable<Color32> colors)
        {
            set(o =>
            {
                var list = C(o).Colors.ToList();
                foreach (var c in colors)
                    if (list.Count < ColorCycleEffect.MaxColors && !list.Any(x => SameRgb(x, c))) list.Add(c);
                return C(o) with { Colors = list };
            });
            ShowSettings();
        }

        var source = SourceAt(_frame);
        IReadOnlyList<Color32> used = source is null ? [] : PaletteRemap.UsedColors(source);

        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int k = 0; k < e.Colors.Count; k++)
        {
            int at = k;
            var field = new ColorField(e.Colors[k], 64);
            field.ColorChanged += (_, _) => set(o => C(o) with { Colors = C(o).Colors.Select((c, j) => j == at ? field.Color : c).ToArray() });
            var remove = Ui.Button("−", (_, _) =>
            {
                set(o => C(o) with { Colors = C(o).Colors.Where((_, j) => j != at).ToArray() });
                ShowSettings();
            }, 26).Tip("이 색 빼기");
            remove.IsEnabled = e.Colors.Count > ColorCycleEffect.MinColors;
            row.Children.Add(Ui.Row(Ui.Label($"{k + 1}"), field, remove));
        }
        SettingsBlock("색", row);

        bool full = e.Colors.Count >= ColorCycleEffect.MaxColors;
        var add = Ui.Button("+ 색 추가", (_, _) =>
        {
            var next = used.FirstOrDefault(c => !C(_effects[Selected]).Colors.Any(x => SameRgb(x, c)), Color32.Black);
            AddColors([next]);
        });
        add.IsEnabled = !full;
        var pick = Ui.Button("그림에서 색 가져오기 ▾");
        if (used.Count == 0 || full) pick.IsEnabled = false;
        else
        {
            var flyout = new Flyout();
            var panel = new StackPanel { Spacing = 4, MaxWidth = 280 };
            panel.Children.Add(Hint($"이 프레임 그림에 쓰인 색 {used.Count}개(많이 쓰인 순) — 누르면 순환할 색에 넣습니다(최대 {ColorCycleEffect.MaxColors}개).", 270));
            panel.Children.Add(ColorPickDialog.Swatches(used, c =>
            {
                flyout.Hide();
                AddColors([c]);
            }));
            flyout.Content = panel;
            pick.Flyout = flyout;
        }
        SettingsRow("", add, pick.Tip(used.Count == 0 ? "이 프레임에 그림이 없습니다." : full ? $"색은 {ColorCycleEffect.MaxColors}개까지입니다." : "그림에 쓰인 색을 골라 넣습니다."));
        SettingsRow("칸 수", NumEditor(1, ColorCycleEffect.MaxSteps, e.Steps, StepsKind(e.Colors.Count), v => set(o => C(o) with { Steps = v })), Ui.Label("칸 (범위 동안 넘어가는 횟수)"));
        SettingsRow("방향", ChoiceEditor(["앞으로 (1 → 2 → …)", "거꾸로"], e.Reverse ? 1 : 0, k => set(o => C(o) with { Reverse = k == 1 })));
        AddMotionEditors(Selected, set);
        _settings.Children.Add(Hint($"그림에서 i번째 색인 픽셀이 (i + 칸)번째 색이 됩니다(목록에 없는 색·투명도는 그대로). 칸 수가 색 개수({e.Colors.Count})의 배수면 범위 끝이 처음과 같아 되풀이해도 이어집니다(물·불·네온)."));
    }

    /// <summary>칸 수 예시: 1과 색 개수의 배수(범위 끝이 처음과 같아 되풀이해도 이어짐 — "N바퀴"로 보임).</summary>
    private static NumKind StepsKind(int colors)
    {
        var values = new SortedSet<decimal> { 1 };
        for (int k = 1; values.Count < 12 && k * colors <= ColorCycleEffect.MaxSteps; k++) values.Add(k * colors);
        return new("effect.cycle.steps", "칸", [.. values], v => v % colors == 0 ? $"{v} 칸 ({v / colors}바퀴)" : $"{v} 칸");
    }
}
