using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>
    /// 색 바꾸기 설정: 쌍마다 [바꿀 색] → [바뀐 색] [−], 아래에 [+ 쌍 추가]·[그림에서 색 가져오기 ▾](이 프레임 그림에 쓰인 색, 많이 쓰인 순 — 누르면 그 색의 쌍 추가)와 허용 차이.
    /// 쌍을 더하거나 빼면 설정 칸을 다시 만든다.
    /// </summary>
    private void PaletteSwapPanel(PaletteSwapEffect p, Action<Func<LayerEffect, LayerEffect>> set)
    {
        static PaletteSwapEffect P(LayerEffect v) => (PaletteSwapEffect)v;
        void ChangePair(int at, Func<ColorSwap, ColorSwap> change)
            => set(o => P(o) with { Pairs = P(o).Pairs.Select((q, j) => j == at ? change(q) : q).ToArray() });
        void AddPairs(IEnumerable<Color32> colors)
        {
            set(o =>
            {
                var pairs = P(o).Pairs.ToList();
                foreach (var c in colors)
                    if (!pairs.Any(q => q.From.R == c.R && q.From.G == c.G && q.From.B == c.B)) pairs.Add(new ColorSwap(c, c));
                return P(o) with { Pairs = pairs };
            });
            ShowSettings();
        }

        var source = SourceAt(_frame);
        IReadOnlyList<Color32> used = source is null ? [] : PaletteRemap.UsedColors(source);

        if (p.Pairs.Count == 0) _settings.Children.Add(Ui.Label("바꿀 색 쌍이 없습니다 — 아래에서 추가하세요."));
        for (int k = 0; k < p.Pairs.Count; k++)
        {
            int at = k;
            var from = new ColorField(p.Pairs[k].From, 64);
            var to = new ColorField(p.Pairs[k].To, 64);
            from.ColorChanged += (_, _) => ChangePair(at, q => q with { From = from.Color });
            to.ColorChanged += (_, _) => ChangePair(at, q => q with { To = to.Color });
            var remove = Ui.IconButton(VectorIcon.Minus, null, (_, _) =>
            {
                set(o => P(o) with
                {
                    Pairs = P(o).Pairs.Where((_, j) => j != at).ToArray(),
                    ColorGradients = RemoveIndexedGradient(o.ColorGradients, "Pairs", at)
                });
                ShowSettings();
            }, 26).Tip("이 쌍 빼기");
            SettingsRow($"{k + 1} 원본", EffectColorEditor(p, $"Pairs[{at}].From", from, set));
            SettingsSubRow("대상", EffectColorEditor(p, $"Pairs[{at}].To", to, set), remove);
        }

        var add = Ui.Button(VectorIcon.Plus, "쌍 추가", (_, _) =>
        {
            // 아직 바꾸지 않는, 가장 많이 쓰인 색부터(없으면 검정)
            var next = used.FirstOrDefault(c => !P(_effects[Selected]).Pairs.Any(q => q.From.R == c.R && q.From.G == c.G && q.From.B == c.B), Color32.Black);
            AddPairs([next]);
        });
        var pick = Ui.Button(VectorIcon.ChevronDown, "그림에서 색 가져오기", iconAfter: true);
        if (used.Count == 0) pick.IsEnabled = false;
        else
        {
            var flyout = new Flyout();
            var panel = new StackPanel { Spacing = 4, MaxWidth = 280 };
            panel.Children.Add(Hint($"이 프레임 그림에 쓰인 색 {used.Count}개(많이 쓰인 순) — 누르면 그 색을 바꿀 쌍을 추가합니다.", 270));
            panel.Children.Add(ColorSourcePicker.Swatches(used, c =>
            {
                flyout.Hide();
                AddPairs([c]);
            }));
            panel.Children.Add(Ui.Button("모두 추가", (_, _) =>
            {
                flyout.Hide();
                AddPairs(used);
            }));
            flyout.Content = panel;
            pick.Flyout = flyout;
        }
        SettingsRow("", add, pick.Tip(used.Count == 0 ? "이 프레임에 그림이 없습니다." : "그림에 쓰인 색을 골라 쌍으로 추가합니다."));
        SettingsRow("허용 차이", NumEditor(0, PaletteSwapEffect.MaxTolerance, p.Tolerance, NumKinds.Tolerance, v => set(o => P(o) with { Tolerance = v })), Ui.Label("(0 = 정확히 같은 색만)"));
        _settings.Children.Add(Hint("보이는 픽셀의 색이 원본 색과 같으면(R·G·B 차이가 허용 차이 이하) 대상 색으로 바꿉니다. 그라데이션을 선택하면 픽셀 위치마다 원본·대상 색을 샘플링합니다. 위의 쌍이 먼저이고 한 번만 바뀝니다."
                                    + " 바뀐 색의 투명도 = 원래 투명도 × 대상 색 투명도(투명으로 바꾸면 지워짐)."));
    }
}
