using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 레이어 효과(비파괴): 효과 목록(추가·삭제·순서·켜기) + 고른 효과 설정(프레임 범위 포함) + 현재 프레임의 전/후 미리보기.
/// 고치는 동안 캔버스에도 바로 보이고(<see cref="SpriteProject.PreviewLayerEffects"/>), 확인이면 대화상자 한 번이 실행취소 한 단계,
/// 취소·창 닫기면 원래대로 돌아간다(<see cref="SpriteProject.EndLayerEffectsPreview"/>).
/// </summary>
public sealed class LayerEffectsDialog : DialogBase
{
    private const int PreviewBox = 220;

    private static readonly string[] Kinds = ["외곽선", "그림자", "가장자리 빛·그늘", "색 덮기"];

    private static readonly (LightDirection Dir, string Name)[] Directions =
    [
        (LightDirection.TopLeft, "왼쪽 위"), (LightDirection.Top, "위"), (LightDirection.TopRight, "오른쪽 위"),
        (LightDirection.Left, "왼쪽"), (LightDirection.Right, "오른쪽"),
        (LightDirection.BottomLeft, "왼쪽 아래"), (LightDirection.Bottom, "아래"), (LightDirection.BottomRight, "오른쪽 아래"),
    ];

    private readonly SpriteProject _project;
    private readonly Layer _layer;
    private readonly int _frame;
    private readonly IReadOnlyList<LayerEffect> _original;
    private readonly List<LayerEffect> _effects;
    private readonly PixelImage? _source;

    private readonly ListBox _list;
    private readonly StackPanel _settings;
    private readonly Button _remove, _up, _down;
    private readonly PreviewView _preview = new();
    private bool _syncing;

    /// <param name="focus">처음 고를 효과 번호(범위 밖이면 첫 효과).</param>
    /// <param name="newRange">새로 추가하는 효과의 프레임 범위(타임라인에서 칸을 고르고 "효과 추가"로 열었을 때). null = 모든 프레임.</param>
    /// <param name="addKind">열자마자 추가할 효과 종류(-1 = 추가하지 않음).</param>
    public LayerEffectsDialog(EditorContext ctx, Layer layer, int focus = -1, FrameRange? newRange = null, int addKind = -1) : base($"레이어 효과 — {layer.Name}")
    {
        _newRange = newRange;
        _project = ctx.Project ?? throw new InvalidOperationException("열린 프로젝트가 없습니다.");
        _layer = layer;
        _frame = Math.Clamp(ctx.CurrentFrame, 0, _project.Frames.Count - 1);
        _original = layer.Effects;
        _effects = [.. layer.Effects];
        _source = _project.EffectSource(layer, _frame);

        var kind = Ui.Combo(150, Kinds);
        var add = Ui.Button("추가", (_, _) => Add(kind.SelectedIndex), 60);
        _remove = Ui.Button("삭제", (_, _) => Remove(), 60);
        _up = Ui.Button("위로", (_, _) => Move(-1), 60);
        _down = Ui.Button("아래로", (_, _) => Move(1), 60);
        _list = new ListBox { Width = 300, Height = 170, Margin = new Thickness(3) };
        ToolTip.SetTip(_list, "줄을 끌어 순서를 바꿉니다(위에서부터 차례로 적용).");
        _drag.Moved += (from, to) =>
        {
            var effect = _effects[from];
            _effects.RemoveAt(from);
            _effects.Insert(to, effect);
            RebuildList(to);
        };
        _list.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            ShowSettings();
            UpdateButtons();
        };
        _settings = new StackPanel { Spacing = 2, MinHeight = 120, Width = 300 };

        var left = Ui.Stack(
            Ui.Row(kind, add),
            _list,
            Ui.Row(_remove, _up, _down),
            Ui.Group("설정", _settings));
        left.Width = 320;

        _preview.Width = PreviewBox * 2 + 12;
        _preview.Height = PreviewBox + 20;
        var right = Ui.Stack(
            Ui.Label($"미리보기 — 프레임 {_frame + 1}" + (layer.IsGroup ? " (그룹 합성)" : layer.IsPart ? " (부위)" : "") + " · 프레임 범위와 관계없이 켜진 효과 모두"),
            _preview,
            Hint(Intro(layer), PreviewBox * 2 + 12));

        var root = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        root.Children.Add(left);
        root.Children.Add(right);
        Body.Children.Add(root);

        RebuildList(_effects.Count == 0 ? -1 : focus >= 0 && focus < _effects.Count ? focus : 0);
        if (addKind >= 0) Add(addKind);
        if (newRange is { } nr) kind.Tip($"추가하는 효과는 고른 칸(프레임 {nr})에만 입혀집니다.");
        Closed += (_, _) =>
        {
            int index = _project.IndexOf(_layer);
            if (index >= 0) _project.EndLayerEffectsPreview(index, _original, DialogResult == DialogResult.OK);
            _preview.Dispose();
        };
    }

    private static string Intro(Layer layer)
        => "효과는 그림을 고치지 않고 보일 때만 입혀집니다(나중에 그린 것에도 자동으로). 위에서부터 차례로 적용됩니다."
           + (layer.IsGroup ? " 그룹 효과는 부위들을 합친 모양에 입혀지며 프레임 시트에 들어갑니다(부위 아틀라스에는 부위 자신의 효과만)." : "")
           + (layer.IsPart ? " 부위 아틀라스에서는 번지는 만큼 스프라이트가 커지고 피벗은 같은 자리를 가리킵니다." : "");

    // ───────────── 목록 ─────────────

    private readonly FrameRange? _newRange;

    private int Selected => _list.SelectedIndex;

    private void RebuildList(int select)
    {
        _syncing = true;
        _drag.Clear();
        _list.Items.Clear();
        for (int i = 0; i < _effects.Count; i++)
        {
            int at = i;
            var check = new CheckBox { IsChecked = _effects[i].Enabled, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            check.IsCheckedChanged += (_, _) =>
            {
                if (_syncing || at >= _effects.Count) return;
                Update(at, _effects[at] with { Enabled = check.IsChecked == true });
            };
            var text = new TextBlock { Text = Summary(_effects[i]), VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
            row.Children.Add(check);
            row.Children.Add(text);
            var item = new ListBoxItem { Content = row };
            _list.Items.Add(item);
            _drag.Attach(item);
        }
        _list.SelectedIndex = Math.Clamp(select, -1, _effects.Count - 1);
        _syncing = false;
        ShowSettings();
        Changed();
    }

    private void UpdateButtons()
    {
        _remove.IsEnabled = Selected >= 0;
        _up.IsEnabled = Selected > 0;
        _down.IsEnabled = Selected >= 0 && Selected < _effects.Count - 1;
    }

    private void Add(int kind)
    {
        LayerEffect effect = kind switch
        {
            1 => new ShadowEffect(),
            2 => new RimLightEffect(),
            3 => new ColorOverlayEffect(),
            _ => new OutlineEffect(),
        };
        if (_newRange is { } range) effect = effect with { Frames = range };
        _effects.Add(effect);
        RebuildList(_effects.Count - 1);
    }

    private void Remove()
    {
        if (Selected is var i && i < 0) return;
        _effects.RemoveAt(i);
        RebuildList(Math.Min(i, _effects.Count - 1));
    }

    private void Move(int delta)
    {
        int i = Selected, j = i + delta;
        if (i < 0 || j < 0 || j >= _effects.Count) return;
        (_effects[i], _effects[j]) = (_effects[j], _effects[i]);
        RebuildList(j);
    }

    private readonly DragReorder _drag = new();

    /// <summary>효과 하나를 바꾸고(목록 글자 갱신) 캔버스·미리보기에 반영한다.</summary>
    private void Update(int index, LayerEffect effect)
    {
        if (index < 0 || index >= _effects.Count || _effects[index] == effect) return;
        _effects[index] = effect;
        if (_list.Items[index] is ListBoxItem { Content: StackPanel { Children: [CheckBox check, TextBlock text] } })
        {
            _syncing = true;
            check.IsChecked = effect.Enabled;
            text.Text = Summary(effect);
            _syncing = false;
        }
        Changed();
    }

    /// <summary>목록이 바뀜: 단추 상태, 캔버스(미리보기 효과)·전/후 미리보기를 갱신한다.</summary>
    private void Changed()
    {
        UpdateButtons();
        int index = _project.IndexOf(_layer);
        if (index >= 0) _project.PreviewLayerEffects(index, _effects);
        _preview.SetImages(_source, _effects);
    }

    private static string Summary(LayerEffect e) => (e switch
    {
        OutlineEffect o => $"외곽선 · {(o.Inside ? "안쪽" : "바깥")} {o.Thickness}px · {(o.Diagonal ? "8방향" : "4방향")} · {o.Color.ToHex()}",
        ShadowEffect s => $"그림자 · ({s.OffsetX}, {s.OffsetY}) · {s.Color.ToHex()}",
        RimLightEffect r => $"가장자리 빛·그늘 · {Directions.First(d => d.Dir == r.Direction).Name}",
        ColorOverlayEffect c => $"색 덮기 · {c.Amount}% · {c.Color.ToHex()}",
        _ => e.DisplayName,
    }) + (e.Frames is { } f ? $" · 프레임 {f}" : "");

    // ───────────── 설정 ─────────────

    private void ShowSettings()
    {
        _settings.Children.Clear();
        int i = Selected;
        if (i < 0 || i >= _effects.Count)
        {
            _settings.Children.Add(Ui.Label(_effects.Count == 0 ? "효과가 없습니다 — 위에서 골라 추가하세요." : "효과를 고르세요."));
            return;
        }
        var e = _effects[i];
        void Set(Func<LayerEffect, LayerEffect> change) => Update(i, change(_effects[i]));
        AddRangeEditor(i);

        switch (e)
        {
            case OutlineEffect o:
                _settings.Children.Add(Ui.Row(Ui.Label("색"), ColorEditor(o.Color, c => Set(x => (OutlineEffect)x with { Color = c }))));
                _settings.Children.Add(Ui.Row(Ui.Label("두께"),
                    NumEditor(1, OutlineEffect.MaxThickness, o.Thickness, v => Set(x => (OutlineEffect)x with { Thickness = v })), Ui.Label("px")));
                _settings.Children.Add(Ui.Row(Ui.Label("방향"),
                    ChoiceEditor(["4방향 (십자)", "8방향 (모서리 포함)"], o.Diagonal ? 1 : 0, k => Set(x => (OutlineEffect)x with { Diagonal = k == 1 }))));
                _settings.Children.Add(Ui.Row(Ui.Label("위치"),
                    ChoiceEditor(["바깥 (그림 밖으로)", "안쪽 (가장자리 픽셀)"], o.Inside ? 1 : 0, k => Set(x => (OutlineEffect)x with { Inside = k == 1 }))));
                break;
            case ShadowEffect s:
                _settings.Children.Add(Ui.Row(Ui.Label("색"), ColorEditor(s.Color, c => Set(x => (ShadowEffect)x with { Color = c }))));
                _settings.Children.Add(Ui.Row(Ui.Label("X"),
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetX, v => Set(x => (ShadowEffect)x with { OffsetX = v })),
                    Ui.Label("Y"),
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetY, v => Set(x => (ShadowEffect)x with { OffsetY = v })),
                    Ui.Label("px")));
                _settings.Children.Add(Hint("그림 모양 그대로 옮긴 단단한 그림자(알파 = 진하기)."));
                break;
            case RimLightEffect r:
                _settings.Children.Add(Ui.Row(Ui.Label("빛이 오는 쪽"),
                    ChoiceEditor(Directions.Select(d => d.Name).ToArray(), Array.FindIndex(Directions, d => d.Dir == r.Direction),
                        k => Set(x => (RimLightEffect)x with { Direction = Directions[k].Dir }))));
                _settings.Children.Add(Ui.Row(Ui.Label("빛 색"), ColorEditor(r.Light, c => Set(x => (RimLightEffect)x with { Light = c }))));
                _settings.Children.Add(Ui.Row(Ui.Label("그늘 색"), ColorEditor(r.Shade, c => Set(x => (RimLightEffect)x with { Shade = c }))));
                _settings.Children.Add(Hint("빛 쪽 가장자리 1px에 빛 색, 반대쪽에 그늘 색을 덧칠합니다(알파 = 세기, 0 = 안 칠함)."));
                break;
            case ColorOverlayEffect c:
                _settings.Children.Add(Ui.Row(Ui.Label("색"), ColorEditor(c.Color, v => Set(x => (ColorOverlayEffect)x with { Color = v }))));
                _settings.Children.Add(Ui.Row(Ui.Label("양"), NumEditor(0, 100, c.Amount, v => Set(x => (ColorOverlayEffect)x with { Amount = v })), Ui.Label("%")));
                _settings.Children.Add(Hint("보이는 픽셀을 그 색 쪽으로 물들입니다(100% = 실루엣, 투명도는 그대로)."));
                break;
        }
    }

    /// <summary>
    /// 프레임 범위: "모든 프레임"(나중에 늘어난 프레임까지) 또는 시작~끝 프레임(1부터). 체크를 풀면 현재 프레임 하나로 시작한다.
    /// 타임라인의 효과 줄에서 상자 끝을 끌어도 바뀐다.
    /// </summary>
    private void AddRangeEditor(int i)
    {
        int n = _project.Frames.Count;
        var range = _effects[i].Frames;
        var all = Ui.Check("모든 프레임", range is null);
        var from = Ui.Num(1, n, (range?.From ?? 0) + 1, 56);
        var to = Ui.Num(1, n, (range?.To ?? n - 1) + 1, 56);
        from.IsEnabled = to.IsEnabled = range is not null;
        bool syncing = false;
        void Apply()
        {
            if (syncing) return;
            var next = all.On() ? null : FrameRange.Of((int)from.NumValue() - 1, (int)to.NumValue() - 1, n);
            if (next is null && !all.On())
            {
                // 모든 프레임을 덮는 범위 = "모든 프레임"
                syncing = true;
                all.IsChecked = true;
                syncing = false;
            }
            from.IsEnabled = to.IsEnabled = next is not null;
            Update(i, _effects[i] with { Frames = next });
        }
        all.IsCheckedChanged += (_, _) =>
        {
            if (!all.On() && (int)from.NumValue() == 1 && (int)to.NumValue() == n)
            {
                // 모든 프레임을 덮는 범위는 "모든 프레임"과 같으므로 현재 프레임 하나로 시작한다.
                syncing = true;
                from.Value = to.Value = _frame + 1;
                syncing = false;
            }
            Apply();
        };
        from.ValueChanged += (_, _) => Apply();
        to.ValueChanged += (_, _) => Apply();
        _settings.Children.Add(Ui.Row(all, Ui.Label("프레임"), from, Ui.Label("~"), to));
    }

    private static TextBlock Hint(string text, double maxWidth = 290)
    {
        var t = Ui.Label(text);
        t.TextWrapping = TextWrapping.Wrap;
        t.MaxWidth = maxWidth;
        t.Foreground = AppTheme.DimTextBrush;
        return t;
    }

    private static ColorField ColorEditor(Color32 value, Action<Color32> changed)
    {
        var field = new ColorField(value);
        field.ColorChanged += (_, _) => changed(field.Color);
        return field;
    }

    private static NumericUpDown NumEditor(int min, int max, int value, Action<int> changed)
    {
        var n = Ui.Num(min, max, value, 56);
        n.ValueChanged += (_, _) => changed((int)n.NumValue());
        return n;
    }

    private static ComboBox ChoiceEditor(string[] items, int selected, Action<int> changed)
    {
        var c = Ui.Combo(170, items);
        c.SelectedIndex = Math.Max(0, selected);
        c.SelectionChanged += (_, _) => { if (c.SelectedIndex >= 0) changed(c.SelectedIndex); };
        return c;
    }

    // ───────────── 미리보기 ─────────────

    /// <summary>전(효과 없이)·후(효과 입힘)를 같은 크기·같은 정수 배율로 나란히 그린다(후 그림이 번진 만큼 둘 다 넓힘).</summary>
    private sealed class PreviewView : Control, IDisposable
    {
        private WriteableBitmap? _before, _after;

        public void SetImages(PixelImage? source, IReadOnlyList<LayerEffect> effects)
        {
            Dispose();
            if (source is not null)
            {
                var (after, dx, dy) = LayerEffects.Render(source, effects);
                var before = new PixelImage(after.Width, after.Height);
                before.Blit(source, -dx, -dy);
                _before = ImageUtil.ToBitmap(before);
                _after = ImageUtil.ToBitmap(after);
            }
            InvalidateVisual();
        }

        public override void Render(DrawingContext g)
        {
            var b = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, b);
            if (_before is null || _after is null)
            {
                AppTheme.DrawTextIn(g, "이 프레임에 그림이 없습니다", b, AppTheme.DimTextBrush, center: true);
                return;
            }
            double half = (b.Width - 12) / 2, top = 18;
            var src = new Rectangle(0, 0, _before.PixelSize.Width, _before.PixelSize.Height);
            AppTheme.DrawTextIn(g, "전", new Rect(0, 0, half, 16), AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
            AppTheme.DrawTextIn(g, "후", new Rect(half + 12, 0, half, 16), AppTheme.DimTextBrush, AppTheme.SmallFontSize, center: true);
            var area = new Rect(0, top, half, b.Height - top);
            double fit = Math.Min(area.Width / src.Width, area.Height / src.Height);
            int scale = fit >= 1 ? (int)Math.Floor(fit) : 0; // 같은 배율로(작으면 맞춤 축소)
            ImageUtil.DrawFitted(g, _before, src, area, scale, checker: true);
            ImageUtil.DrawFitted(g, _after, src, new Rect(half + 12, top, half, b.Height - top), scale, checker: true);
        }

        public void Dispose()
        {
            _before?.Dispose();
            _after?.Dispose();
            _before = _after = null;
        }
    }
}
