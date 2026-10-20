using Gpixel.Core.Edit;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// 레이어 효과(비파괴): 효과 목록(추가·삭제·순서·켜기·영구 적용) + 고른 효과 설정(프레임 범위 포함) + 전/후 미리보기(움직이는 효과는 그 범위를 돌려 봄).
/// 고치는 동안 캔버스에도 바로 보이고(<see cref="SpriteProject.PreviewLayerEffects"/>), 확인이면 대화상자 한 번이 실행취소 한 단계,
/// 취소·창 닫기면 원래대로 돌아간다(<see cref="SpriteProject.EndLayerEffectsPreview"/>).
/// </summary>
public sealed class LayerEffectsDialog : DialogBase
{
    private const int PreviewBox = 220;

    /// <summary>추가할 수 있는 효과 종류(번호 = <see cref="Create"/>의 kind). 타임라인 셀 메뉴 "효과 추가 ▸"도 같은 목록.</summary>
    internal static readonly string[] Kinds = ["외곽선", "그림자", "가장자리 빛·그늘", "색 덮기", "터지기", "빛 지나가기", "늘었다 줄기"];

    /// <summary>종류 번호의 새 효과(기본 설정).</summary>
    internal static LayerEffect Create(int kind) => kind switch
    {
        1 => new ShadowEffect(),
        2 => new RimLightEffect(),
        3 => new ColorOverlayEffect(),
        4 => new ExplodeEffect(),
        5 => new ShineEffect(),
        6 => new SquashEffect(),
        _ => new OutlineEffect(),
    };

    /// <summary>종류가 움직이는 효과(프레임 범위에 따라 모습이 바뀜)인지.</summary>
    internal static bool IsAnimated(int kind) => Create(kind) is AnimatedEffect;

    private static readonly (LightDirection Dir, string Name)[] Directions =
    [
        (LightDirection.TopLeft, "왼쪽 위"), (LightDirection.Top, "위"), (LightDirection.TopRight, "오른쪽 위"),
        (LightDirection.Left, "왼쪽"), (LightDirection.Right, "오른쪽"),
        (LightDirection.BottomLeft, "왼쪽 아래"), (LightDirection.Bottom, "아래"), (LightDirection.BottomRight, "오른쪽 아래"),
    ];

    private static readonly string[] Easings = ["처음 빠르게", "일정하게", "천천히 시작", "천천히 시작·끝"];
    private static readonly string[] Repeats = ["한 번", "반복", "왕복"];
    private static readonly string[] Centers = ["가운데", "피벗", "아래 가운데", "직접 (X, Y)"];
    private static readonly string[] Anchors = ["아래 가운데 (발 고정)", "가운데", "피벗"];

    private readonly SpriteProject _project;
    private readonly Layer _layer;
    private readonly int _frame;
    private readonly IReadOnlyList<LayerEffect> _original;
    private readonly List<LayerEffect> _effects;
    private readonly Dictionary<int, PixelImage?> _sources = new();

    private readonly ListBox _list;
    private readonly StackPanel _settings;
    private readonly Button _remove, _up, _down, _bake, _play;
    private readonly TextBlock _caption;
    private readonly PreviewView _preview = new();
    private readonly DispatcherTimer _timer = new();
    private int _previewFrame;
    private bool _syncing, _baked;

    /// <summary>영구 적용으로 닫혔으면 상태 표시줄에 보일 알림.</summary>
    public string? BakeMessage { get; private set; }

    /// <param name="focus">처음 고를 효과 번호(범위 밖이면 첫 효과).</param>
    /// <param name="newRange">새로 추가하는 효과의 프레임 범위(타임라인에서 칸을 고르고 "효과 추가"로 열었을 때). null = 모든 프레임.</param>
    /// <param name="addKind">열자마자 추가할 효과 종류(-1 = 추가하지 않음).</param>
    public LayerEffectsDialog(EditorContext ctx, Layer layer, int focus = -1, FrameRange? newRange = null, int addKind = -1) : base($"레이어 효과 — {layer.Name}")
    {
        _newRange = newRange;
        _project = ctx.Project ?? throw new InvalidOperationException("열린 프로젝트가 없습니다.");
        _layer = layer;
        _frame = Math.Clamp(ctx.CurrentFrame, 0, _project.Frames.Count - 1);
        _previewFrame = _frame;
        _original = layer.Effects;
        _effects = [.. layer.Effects];

        var kind = Ui.Combo(150, Kinds);
        var add = Ui.Button("추가", (_, _) => Add(kind.SelectedIndex), 60);
        _remove = Ui.Button("삭제", (_, _) => Remove(), 60);
        _up = Ui.Button("위로", (_, _) => Move(-1), 60);
        _down = Ui.Button("아래로", (_, _) => Move(1), 60);
        _bake = Ui.Button("영구 적용", (_, _) => Bake(), 80);
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
            SyncPlayback();
        };
        _settings = new StackPanel { Spacing = 2, MinHeight = 120, Width = 300 };

        var left = Ui.Stack(
            Ui.Row(kind, add),
            _list,
            Ui.Row(_remove, _up, _down, _bake),
            Ui.Group("설정", new ScrollViewer { Content = _settings, MaxHeight = 430, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }));
        left.Width = 330;

        _preview.Width = PreviewBox * 2 + 12;
        _preview.Height = PreviewBox + 20;
        _caption = Ui.Label("");
        _play = Ui.Button("▶ 재생", (_, _) => TogglePlay(), 80);
        ToolTip.SetTip(_play, "움직이는 효과(터지기·빛 지나가기·늘었다 줄기)를 고른 효과의 프레임 범위로 돌려 봅니다.");
        _timer.Tick += (_, _) => NextPreviewFrame();
        var right = Ui.Stack(
            Ui.Row(_play, _caption),
            _preview,
            Hint(Intro(layer), PreviewBox * 2 + 12));

        var root = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        root.Children.Add(left);
        root.Children.Add(right);
        Body.Children.Add(root);

        RebuildList(_effects.Count == 0 ? -1 : focus >= 0 && focus < _effects.Count ? focus : 0);
        if (addKind >= 0) Add(addKind);
        if (newRange is { } nr) kind.Tip($"추가하는 효과는 고른 칸(프레임 {nr})에만 입혀집니다.");
        SyncPlayback();
        Closed += (_, _) =>
        {
            _timer.Stop();
            int index = _project.IndexOf(_layer);
            if (index >= 0 && !_baked) _project.EndLayerEffectsPreview(index, _original, DialogResult == DialogResult.OK);
            _preview.Dispose();
        };
    }

    private static string Intro(Layer layer)
        => "효과는 그림을 고치지 않고 보일 때만 입혀집니다(나중에 그린 것에도 자동으로). 위에서부터 차례로 적용됩니다."
           + " 터지기·빛 지나가기·늘었다 줄기는 프레임 범위의 첫 프레임에서 시작해 마지막 프레임에서 끝납니다(범위가 길수록 느림)."
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
        SyncPlayback();
    }

    private void UpdateButtons()
    {
        _remove.IsEnabled = Selected >= 0;
        _up.IsEnabled = Selected > 0;
        _down.IsEnabled = Selected >= 0 && Selected < _effects.Count - 1;
        var (canBake, why) = CanBake(_project, _layer);
        _bake.IsEnabled = canBake && Selected >= 0;
        int extra = Selected >= 0 ? LayerEffects.BakeSet(_effects, [Selected]).Count - 1 : 0;
        ToolTip.SetTip(_bake, why ?? ("고른 효과를 셀 픽셀에 구워 넣고 효과에서 뺍니다(실행취소 한 단계, 창이 닫힘)."
                                      + (extra > 0 ? $" 위의 켜진 효과 {extra}개도 함께 구워집니다(보이던 그대로가 되도록)." : "")));
    }

    private void Add(int kind)
    {
        var effect = Create(kind);
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
        SyncPlayback();
    }

    /// <summary>목록이 바뀜: 단추 상태, 캔버스(미리보기 효과)·전/후 미리보기를 갱신한다.</summary>
    private void Changed()
    {
        UpdateButtons();
        int index = _project.IndexOf(_layer);
        if (index >= 0) _project.PreviewLayerEffects(index, _effects);
        RefreshPreview();
    }

    private static string Summary(LayerEffect e) => (e switch
    {
        OutlineEffect o => $"외곽선 · {(o.Inside ? "안쪽" : "바깥")} {o.Thickness}px · {(o.Diagonal ? "8방향" : "4방향")} · {o.Color.ToHex()}",
        ShadowEffect s => $"그림자 · ({s.OffsetX}, {s.OffsetY}) · {s.Color.ToHex()}",
        RimLightEffect r => $"가장자리 빛·그늘 · {Directions.First(d => d.Dir == r.Direction).Name}",
        ColorOverlayEffect c => $"색 덮기 · {c.Amount}% · {c.Color.ToHex()}",
        ExplodeEffect x => $"터지기 · {x.Distance}px · {Centers[(int)x.Center].Split(' ')[0]}" + (x.KeepOriginal ? " · 원본 남김" : ""),
        ShineEffect s => $"빛 지나가기 · {s.Angle}° · {s.Width}px · {s.Strength}%",
        SquashEffect q => $"늘었다 줄기 · {q.Amount}% · {q.Bounces}번",
        _ => e.DisplayName,
    }) + (e.Frames is { } f ? $" · 프레임 {f}" : "");

    // ───────────── 영구 적용 ─────────────

    /// <summary>레이어에 효과를 영구 적용할 수 있는지와, 안 되면 그 까닭.</summary>
    internal static (bool Ok, string? Why) CanBake(SpriteProject project, Layer layer)
    {
        if (layer.IsGroup) return (false, "그룹 효과는 영구 적용할 수 없습니다 — 부위에 적용하세요.");
        if (project.IsLayerLocked(project.IndexOf(layer))) return (false, "잠긴 레이어에는 영구 적용할 수 없습니다.");
        return (true, null);
    }

    /// <summary>
    /// 효과 영구 적용(<see cref="SpriteProject.BakeLayerEffects"/>)한 결과 알림 — "효과 영구 적용: 외곽선 (프레임 1~4)".
    /// </summary>
    internal static string BakeSummary((IReadOnlyList<LayerEffect> Baked, FrameRange? Frames, int Cels, bool Grown) r, Layer layer)
        => $"효과 영구 적용: {string.Join(", ", r.Baked.Select(e => e.DisplayName))} ("
           + (r.Frames is { } f ? f.From == f.To ? $"프레임 {f.From + 1}" : $"프레임 {f.From + 1}~{f.To + 1}" : "모든 프레임")
           + $", 셀 {r.Cels}개)"
           + (r.Grown ? $" · 부위 크기 {layer.PartSize.Width}×{layer.PartSize.Height}로 넓힘" : "")
           + " — Ctrl+Z로 되돌립니다.";

    /// <summary>
    /// 고른 효과 영구 적용: 창에서 고친 효과 목록을 확정하고 곧바로 구워 넣는다(둘을 합쳐 실행취소 한 단계). 창은 닫힌다.
    /// </summary>
    private void Bake()
    {
        int index = _project.IndexOf(_layer), selected = Selected;
        if (index < 0 || selected < 0) return;
        var list = _effects.ToList();
        _project.EndLayerEffectsPreview(index, _original, commit: false);
        try
        {
            (IReadOnlyList<LayerEffect>, FrameRange?, int, bool) result = default;
            _project.Edit("효과 영구 적용", ProjectChange.Layers | ProjectChange.Pixels, () =>
            {
                _project.SetLayerEffects(index, list);
                result = _project.BakeLayerEffects(index, [selected]);
            });
            BakeMessage = BakeSummary(result, _layer);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _project.PreviewLayerEffects(index, _effects);
            Ui.Error(this, ex.Message);
            return;
        }
        _baked = true;
        DialogResult = DialogResult.OK;
    }

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
        void Add(params Control[] row) => _settings.Children.Add(Ui.Row(row));
        AddRangeEditor(i);

        switch (e)
        {
            case OutlineEffect o:
                Add(Ui.Label("색"), ColorEditor(o.Color, c => Set(x => (OutlineEffect)x with { Color = c })));
                Add(Ui.Label("두께"), NumEditor(1, OutlineEffect.MaxThickness, o.Thickness, v => Set(x => (OutlineEffect)x with { Thickness = v })), Ui.Label("px"));
                Add(Ui.Label("방향"), ChoiceEditor(["4방향 (십자)", "8방향 (모서리 포함)"], o.Diagonal ? 1 : 0, k => Set(x => (OutlineEffect)x with { Diagonal = k == 1 })));
                Add(Ui.Label("위치"), ChoiceEditor(["바깥 (그림 밖으로)", "안쪽 (가장자리 픽셀)"], o.Inside ? 1 : 0, k => Set(x => (OutlineEffect)x with { Inside = k == 1 })));
                break;
            case ShadowEffect s:
                Add(Ui.Label("색"), ColorEditor(s.Color, c => Set(x => (ShadowEffect)x with { Color = c })));
                Add(Ui.Label("X"),
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetX, v => Set(x => (ShadowEffect)x with { OffsetX = v })),
                    Ui.Label("Y"),
                    NumEditor(-ShadowEffect.MaxOffset, ShadowEffect.MaxOffset, s.OffsetY, v => Set(x => (ShadowEffect)x with { OffsetY = v })),
                    Ui.Label("px"));
                _settings.Children.Add(Hint("그림 모양 그대로 옮긴 단단한 그림자(알파 = 진하기)."));
                break;
            case RimLightEffect r:
                Add(Ui.Label("빛이 오는 쪽"),
                    ChoiceEditor(Directions.Select(d => d.Name).ToArray(), Array.FindIndex(Directions, d => d.Dir == r.Direction),
                        k => Set(x => (RimLightEffect)x with { Direction = Directions[k].Dir })));
                Add(Ui.Label("빛 색"), ColorEditor(r.Light, c => Set(x => (RimLightEffect)x with { Light = c })));
                Add(Ui.Label("그늘 색"), ColorEditor(r.Shade, c => Set(x => (RimLightEffect)x with { Shade = c })));
                _settings.Children.Add(Hint("빛 쪽 가장자리 1px에 빛 색, 반대쪽에 그늘 색을 덧칠합니다(알파 = 세기, 0 = 안 칠함)."));
                break;
            case ColorOverlayEffect c:
                Add(Ui.Label("색"), ColorEditor(c.Color, v => Set(x => (ColorOverlayEffect)x with { Color = v })));
                Add(Ui.Label("양"), NumEditor(0, 100, c.Amount, v => Set(x => (ColorOverlayEffect)x with { Amount = v })), Ui.Label("%"));
                _settings.Children.Add(Hint("보이는 픽셀을 그 색 쪽으로 물들입니다(100% = 실루엣, 투명도는 그대로)."));
                break;
            case ExplodeEffect x:
            {
                ExplodeEffect X(LayerEffect v) => (ExplodeEffect)v;
                Add(Ui.Label("거리"), NumEditor(1, ExplodeEffect.MaxDistance, x.Distance, v => Set(o => X(o) with { Distance = v })), Ui.Label("px (끝까지 날아가는 거리)"));
                var cx = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, x.CenterX, v => Set(o => X(o) with { CenterX = v }));
                var cy = NumEditor(-PixelImage.MaxSize, PixelImage.MaxSize, x.CenterY, v => Set(o => X(o) with { CenterY = v }));
                cx.IsEnabled = cy.IsEnabled = x.Center == ExplodeCenter.Custom;
                Add(Ui.Label("중심"), ChoiceEditor(Centers, (int)x.Center, k =>
                {
                    cx.IsEnabled = cy.IsEnabled = (ExplodeCenter)k == ExplodeCenter.Custom;
                    Set(o => X(o) with { Center = (ExplodeCenter)k });
                }));
                Add(Ui.Label("    X"), cx, Ui.Label("Y"), cy, Ui.Label("px (원본 왼쪽 위 기준)"));
                Add(Ui.Label("흩어짐"), NumEditor(0, 100, x.Spread, v => Set(o => X(o) with { Spread = v })), Ui.Label("%"),
                    Ui.Label("  중력"), NumEditor(-ExplodeEffect.MaxGravity, ExplodeEffect.MaxGravity, x.Gravity, v => Set(o => X(o) with { Gravity = v })), Ui.Label("px"));
                Add(Ui.Label("조각 크기"), NumEditor(1, ExplodeEffect.MaxPieceSize, x.PieceSize, v => Set(o => X(o) with { PieceSize = v })), Ui.Label("px"),
                    CheckEditor("끝으로 갈수록 작게", x.Shrink, v => Set(o => X(o) with { Shrink = v })));
                var fadeStart = NumEditor(0, 100, x.FadeStart, v => Set(o => X(o) with { FadeStart = v }));
                fadeStart.IsEnabled = x.Fade;
                Add(CheckEditor("사라짐 (흐려지기)", x.Fade, v => { fadeStart.IsEnabled = v; Set(o => X(o) with { Fade = v }); }),
                    Ui.Label("시작"), fadeStart, Ui.Label("%"));
                Add(Ui.Label("번쩍임"), NumEditor(0, ExplodeEffect.MaxFlashFrames, x.FlashFrames, v => Set(o => X(o) with { FlashFrames = v })), Ui.Label("프레임"),
                    ColorEditor(x.FlashColor, v => Set(o => X(o) with { FlashColor = v })));
                Add(CheckEditor("원본 남기기 (복사본이 날아감)", x.KeepOriginal, v => Set(o => X(o) with { KeepOriginal = v })));
                var seed = NumEditor(0, ExplodeEffect.MaxSeed, x.Seed, v => Set(o => X(o) with { Seed = v }));
                var reroll = Ui.Button("🎲", (_, _) => seed.Value = System.Random.Shared.Next(0, ExplodeEffect.MaxSeed + 1), 36);
                ToolTip.SetTip(reroll, "다른 흩어짐(시드를 무작위로)");
                Add(Ui.Label("시드"), seed, reroll);
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint("범위 첫 프레임은 원래 그림 그대로, 그 뒤로 보이는 픽셀이 중심에서 바깥으로 튀어 나가며 사라집니다. 셀 그림은 그대로입니다."
                                            + " 범위의 프레임마다 그 프레임 그림이 조각이 됩니다(그림이 없는 프레임은 터질 것도 없음)."));
                break;
            }
            case ShineEffect s:
            {
                ShineEffect S(LayerEffect v) => (ShineEffect)v;
                Add(Ui.Label("각도"), NumEditor(0, 359, s.Angle, v => Set(o => S(o) with { Angle = v })), Ui.Label("° (지나가는 방향, 45 = ↗)"));
                Add(Ui.Label("폭"), NumEditor(1, ShineEffect.MaxWidth, s.Width, v => Set(o => S(o) with { Width = v })), Ui.Label("px"),
                    Ui.Label("  세기"), NumEditor(0, 100, s.Strength, v => Set(o => S(o) with { Strength = v })), Ui.Label("%"));
                Add(Ui.Label("색"), ColorEditor(s.Color, v => Set(o => S(o) with { Color = v })));
                Add(Ui.Label("부드러움"), ChoiceEditor(["딱딱하게", "가장자리 1px", "가장자리 2px"], s.Softness, k => Set(o => S(o) with { Softness = k })));
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint("빛 띠가 그림 한쪽 바로 밖에서 들어와 반대쪽으로 빠져나갑니다. 보이는 픽셀만 밝아지고(스크린), 투명한 곳·투명도는 그대로입니다."));
                break;
            }
            case SquashEffect q:
            {
                SquashEffect Q(LayerEffect v) => (SquashEffect)v;
                Add(Ui.Label("세기"), NumEditor(1, SquashEffect.MaxAmount, q.Amount, v => Set(o => Q(o) with { Amount = v })), Ui.Label("%"),
                    Ui.Label("  튕김"), NumEditor(1, SquashEffect.MaxBounces, q.Bounces, v => Set(o => Q(o) with { Bounces = v })), Ui.Label("번"));
                Add(Ui.Label("기준점"), ChoiceEditor(Anchors, (int)q.Anchor, k => Set(o => Q(o) with { Anchor = (SquashAnchor)k })));
                Add(Ui.Label("순서"), ChoiceEditor(["옆으로 먼저 (납작 → 길쭉)", "위로 먼저 (길쭉 → 납작)"], q.StretchFirst ? 1 : 0, k => Set(o => Q(o) with { StretchFirst = k == 1 })));
                AddMotionEditors(i, Set);
                _settings.Children.Add(Hint("젤리처럼 출렁이다 원래 모양으로 돌아옵니다(가로 1 + 세기 × 출렁임, 세로 1 − 세기 × 출렁임). 픽셀은 늘이거나 줄여도 또렷합니다."));
                break;
            }
        }
    }

    /// <summary>움직이는 효과 공통: 빠르기 곡선, 반복(한 번/반복/왕복 + 횟수), 범위 길이 안내.</summary>
    private void AddMotionEditors(int i, Action<Func<LayerEffect, LayerEffect>> set)
    {
        var a = (AnimatedEffect)_effects[i];
        Add(Ui.Label("빠르기"), ChoiceEditor(Easings, (int)a.Easing, k => set(o => (AnimatedEffect)o with { Easing = (EffectEasing)k })));
        var count = NumEditor(1, AnimatedEffect.MaxRepeatCount, a.RepeatCount, v => set(o => (AnimatedEffect)o with { RepeatCount = v }));
        count.IsEnabled = a.Repeat != EffectRepeat.Once;
        Add(Ui.Label("반복"), ChoiceEditor(Repeats, (int)a.Repeat, k =>
        {
            count.IsEnabled = (EffectRepeat)k != EffectRepeat.Once;
            set(o => (AnimatedEffect)o with { Repeat = (EffectRepeat)k });
        }), count, Ui.Label("번"));
        var (from, to) = RangeOf(a);
        _settings.Children.Add(Hint($"지금 {to - from + 1}프레임(프레임 {from + 1}~{to + 1}) 동안 움직입니다 — 범위를 늘리면 느려지고 줄이면 빨라집니다."
                                    + (from == to ? " 한 프레임이면 움직이지 않습니다." : "")));

        void Add(params Control[] row) => _settings.Children.Add(Ui.Row(row));
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
            if (_effects[i] is AnimatedEffect) ShowSettings(); // 범위 길이 안내 갱신
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

    private static CheckBox CheckEditor(string text, bool value, Action<bool> changed)
    {
        var c = Ui.Check(text, value);
        c.IsCheckedChanged += (_, _) => changed(c.On());
        return c;
    }

    // ───────────── 미리보기 ─────────────

    /// <summary>움직이는 효과의 프레임 범위(null = 모든 프레임).</summary>
    private (int From, int To) RangeOf(LayerEffect e)
        => e.Frames is { } r ? (r.From, Math.Min(r.To, _project.Frames.Count - 1)) : (0, _project.Frames.Count - 1);

    /// <summary>재생할 범위: 고른 효과가 움직이는 효과면 그 범위, 아니면 켜진 움직이는 효과가 있을 때 모든 프레임. 돌릴 것이 없으면 null.</summary>
    private (int From, int To)? PlayRange()
    {
        if (Selected >= 0 && Selected < _effects.Count && _effects[Selected] is AnimatedEffect a) return RangeOf(a);
        return _effects.Any(e => e is AnimatedEffect { Enabled: true }) ? (0, _project.Frames.Count - 1) : null;
    }

    /// <summary>고른 효과가 움직이는 효과면 그 범위를 돌려 보고(자동 재생), 돌릴 것이 없으면 멈추고 지금 프레임으로.</summary>
    private void SyncPlayback()
    {
        var range = PlayRange();
        _play.IsEnabled = range is not null;
        bool animatedSelected = Selected >= 0 && Selected < _effects.Count && _effects[Selected] is AnimatedEffect;
        if (range is null || !animatedSelected && !_timer.IsEnabled)
        {
            _timer.Stop();
            if (_previewFrame != _frame)
            {
                _previewFrame = _frame;
                RefreshPreview();
            }
        }
        else if (animatedSelected && !_timer.IsEnabled)
        {
            var (from, to) = range.Value;
            if (_previewFrame < from || _previewFrame > to) _previewFrame = from;
            StartTimer();
        }
        _play.Content = _timer.IsEnabled ? "⏸ 멈춤" : "▶ 재생";
    }

    private void TogglePlay()
    {
        if (_timer.IsEnabled) _timer.Stop();
        else if (PlayRange() is { } range)
        {
            if (_previewFrame < range.From || _previewFrame >= range.To) _previewFrame = range.From;
            StartTimer();
            RefreshPreview();
        }
        _play.Content = _timer.IsEnabled ? "⏸ 멈춤" : "▶ 재생";
    }

    private void StartTimer()
    {
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(40, _project.Frames[_previewFrame].DurationMs));
        _timer.Start();
    }

    /// <summary>재생 중 다음 프레임(범위 끝 다음은 처음 — 끝 프레임은 조금 더 보여 줌).</summary>
    private void NextPreviewFrame()
    {
        if (PlayRange() is not { } range)
        {
            SyncPlayback();
            return;
        }
        _previewFrame = _previewFrame >= range.To || _previewFrame < range.From ? range.From : _previewFrame + 1;
        int ms = Math.Max(40, _project.Frames[_previewFrame].DurationMs);
        _timer.Interval = TimeSpan.FromMilliseconds(_previewFrame == range.To ? ms * 3 : ms);
        RefreshPreview();
    }

    /// <summary>효과 원본(그 프레임, 창이 열린 동안 바뀌지 않으므로 프레임마다 한 번만 만든다).</summary>
    private PixelImage? SourceAt(int frame)
    {
        if (!_sources.TryGetValue(frame, out var src)) _sources[frame] = src = _project.EffectSource(_layer, frame);
        return src;
    }

    /// <summary>미리보기 프레임의 전(효과 없이)·후(모든 효과를 그 프레임으로 움직여)를 그린다. 크기는 모든 프레임 같게(효과 여백 전체).</summary>
    private void RefreshPreview()
    {
        int f = _previewFrame, n = _project.Frames.Count;
        var source = SourceAt(f);
        string where = _layer.IsGroup ? " (그룹 합성)" : _layer.IsPart ? " (부위)" : "";
        if (source is null)
        {
            _caption.Text = $"프레임 {f + 1}{where}";
            _preview.SetImages(null, null);
            return;
        }
        var effects = LayerEffects.Animate(_effects, f, n, _project.EffectPivot(_layer, f));
        var (after, dx, dy) = LayerEffects.Render(source, effects, LayerEffects.Padding(_effects, source.Width, source.Height));
        var before = new PixelImage(after.Width, after.Height);
        before.Blit(source, -dx, -dy);
        _preview.SetImages(before, after);
        string progress = Selected >= 0 && Selected < effects.Count && effects[Selected] is AnimatedEffect a ? $" · 진행 {a.Time * 100:0}%" : "";
        _caption.Text = $"미리보기 — 프레임 {f + 1}{where}{progress} · 프레임 범위와 관계없이 켜진 효과 모두";
    }

    /// <summary>전·후를 같은 크기·같은 정수 배율로 나란히 그린다(후 그림이 번진 만큼 둘 다 넓힘).</summary>
    private sealed class PreviewView : Control, IDisposable
    {
        private WriteableBitmap? _before, _after;

        public void SetImages(PixelImage? before, PixelImage? after)
        {
            Dispose();
            if (before is not null && after is not null)
            {
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
