using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 스킨 설정 창(모드리스, 하나만): 위 "스킨 프리셋" 카드 줄(썸네일 = 그 프리셋을 입힌 지금 프레임), 아래 "레이어 스킨" — 스킨이 둘 이상인 레이어마다
/// (타임라인처럼 위→아래, 나무 깊이만큼 들여 써서) 스킨 카드 줄. 카드를 누르면 바로 입히고, 우클릭 메뉴로 복제·이름·삭제·순서를 고친다
/// (실행취소됨 — 이 창에서도 실행취소·다시 실행 단축키가 듣는다). 스킨 카드를 Ctrl/⌘+클릭하면 프리셋에 넣을 스킨으로 고르고(레이어마다 하나),
/// 아래 "프리셋으로 등록…"이 고른 스킨(고른 것이 없으면 지금 스킨 전체)을 프리셋으로 등록한다.
/// 프로젝트 변경·현재 프레임 변경·다른 프로젝트 열기 때 약 100ms 늦춰 다시 만든다(그리는 중에는 끝날 때까지 미룸).
/// </summary>
public sealed class SkinManagerWindow : Window
{
    private const int PresetThumb = 72, CardThumb = 56, IndentStep = 18;
    /// <summary>대상 도움말(F1) 가이드 쪽.</summary>
    private const string Page = "parts-skin-manager";
    private const string PresetsPage = "parts-outfits";
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor PanCursor = new(StandardCursorType.SizeWestEast);
    /// <summary>처음 크기(예전 680×720의 1.7배 — 화면보다 크면 화면 안으로 줄임).</summary>
    private const double StartWidth = 680 * 1.7, StartHeight = 720 * 1.7;

    private static SkinManagerWindow? _instance;

    private readonly EditorContext _ctx;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly StackPanel _presets = new();
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly Control _presetBox, _layerBox;
    private readonly Button _savePreset, _clearPicked;
    private readonly TextBlock _pickedText = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = AppTheme.DimTextBrush, Margin = new Thickness(8, 0) };
    /// <summary>마지막 프리셋 입히기에서 건너뛴 항목 안내(다음 입히기·프로젝트 바꾸기 때 지움).</summary>
    private string? _presetWarning;
    /// <summary>이번에 만든 썸네일 비트맵(다시 만들거나 닫을 때 Dispose).</summary>
    private readonly List<WriteableBitmap> _bitmaps = new();
    /// <summary>Ctrl/⌘+클릭으로 프리셋에 넣으려고 고른 스킨(레이어마다 하나).</summary>
    private readonly Dictionary<Layer, Skin> _picked = new(ReferenceEqualityComparer.Instance);
    /// <summary>강조할 레이어(칩·메뉴로 열었을 때 — 스킨이 하나뿐이어도 줄을 보임), 다음 다시 만들기에서 그 줄로 스크롤할지.</summary>
    private Layer? _focus;
    private bool _scrollToFocus;
    /// <summary>레이어 줄의 가로 스크롤 위치(카드를 눌러 다시 만들어도 보던 자리 그대로).</summary>
    private readonly Dictionary<Layer, double> _scrollX = new(ReferenceEqualityComparer.Instance);

    private SkinManagerWindow(EditorContext ctx)
    {
        _ctx = ctx;
        Title = UiStrings.SkinSettings_Title;
        ShowInTaskbar = false;
        Width = StartWidth;
        Height = StartHeight;
        MinWidth = 360;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Opened += (_, _) => FitToScreen();

        var help = new TextBlock
        {
            Text = UiStrings.SkinSettings_Help,
            TextWrapping = TextWrapping.Wrap,
            Foreground = AppTheme.DimTextBrush,
            Margin = new Thickness(2, 0, 2, 6),
        };
        // 대상 도움말(F1): 창 = "스킨 설정 창" 쪽, 프리셋 묶음 = 스킨 프리셋, 카드·줄은 만들 때(Rebuild) 그 자리를 붙인다
        this.HelpAt(Page);
        _presetBox = Ui.Group(UiStrings.SkinSettings_Presets, _presets).HelpAt(PresetsPage, anchor: "Outfit presets");
        _layerBox = Ui.Group(UiStrings.SkinSettings_Layers, _list).HelpAt(Page, anchor: "skin cards for each group");
        var content = new StackPanel { Margin = new Thickness(10, 8), Children = { help, _presetBox, _layerBox } };

        // 아래: 프리셋으로 등록(고른 스킨 또는 지금 스킨 전체) · 고르기 지우기 · 고른 개수 | 확인(Esc도 닫기 — 카드를 누를 때마다 이미 입혔으므로 창만 닫는다)
        _savePreset = new Button { Content = UiStrings.SkinSettings_SavePreset, Padding = new Thickness(8, 2) }
            .HelpAt(PresetsPage, anchor: "Use ④ Save current outfit")
            .Tip(UiStrings.SkinSettings_SavePresetTip);
        _savePreset.Click += (_, _) => Dispatcher.UIThread.Post(SavePreset);
        _clearPicked = new Button { Content = UiStrings.SkinSettings_ClearPicked, Padding = new Thickness(8, 2), Margin = new Thickness(4, 0, 0, 0) };
        _clearPicked.Click += (_, _) =>
        {
            _picked.Clear();
            Dispatcher.UIThread.Post(Rebuild);
        };
        var ok = new Button { Content = UiStrings.S_SkinManagerWindow_SkinManagerWindow_E08E14A158, MinWidth = 90, IsCancel = true, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Close();
        var buttons = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(ok, Dock.Right);
        buttons.Children.Add(ok);
        buttons.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _savePreset, _clearPicked, _pickedText } });
        var footer = new Border
        {
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10, 6),
            Child = buttons,
        };
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(Ui.ScrollHost(content));
        Content = root;

        _timer.Tick += (_, _) =>
        {
            // 그리는 중에는 미룬다(획이 끝나면 다음 틱에 마지막으로 한 번 다시 만든다).
            if (_ctx.Project is { IsStrokeActive: true }) return;
            _timer.Stop();
            Rebuild();
        };
        _ctx.ProjectChanged += OnProjectChanged;
        _ctx.CurrentChanged += OnCurrentChanged;
        _ctx.ProjectReplaced += OnProjectReplaced;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
                return;
            }
            // 삭제·복제·이름 바꾸기 등은 문서 실행취소에 남으므로 이 창에 있어도 메인 창의 실행취소·다시 실행 키로 되돌린다.
            if (Owner is MainForm main && main.RunUndoKey(e)) e.Handled = true;
        };
    }

    /// <summary>처음 크기가 화면 작업 영역보다 크면 화면 안으로 줄이고 가운데에 둔다.</summary>
    private void FitToScreen()
    {
        if ((Screens.ScreenFromWindow(this) ?? Screens.Primary) is not { } screen) return;
        var area = screen.WorkingArea;
        double scale = screen.Scaling, maxW = area.Width / scale * 0.95, maxH = area.Height / scale * 0.92;
        if (Width <= maxW && Height <= maxH) return;
        Width = Math.Min(Width, maxW);
        Height = Math.Min(Height, maxH);
        Position = new PixelPoint(area.X + (int)((area.Width - Width * scale) / 2), area.Y + (int)((area.Height - Height * scale) / 2));
    }

    /// <summary>
    /// 레이어 줄의 카드를 한 가로 스크롤로 묶고, 카드 사이 빈 곳을 잡아 좌우로 끌면 통째로 옮긴다.
    /// 카드·단추는 누르기를 먼저 처리하므로 여기까지 오지 않는다(카드 끌기 = 순서 바꾸기 그대로).
    /// </summary>
    private ScrollViewer PanScroll(Layer layer, Control content)
    {
        var sv = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = Brushes.Transparent,
        };
        Avalonia.Point? press = null;
        double pressOffset = 0;
        sv.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(sv).Properties.IsLeftButtonPressed) return;
            press = e.GetPosition(sv);
            pressOffset = sv.Offset.X;
            sv.Cursor = PanCursor;
            e.Pointer.Capture(sv);
            e.Handled = true;
        };
        sv.PointerMoved += (_, e) =>
        {
            if (press is not { } start) return;
            double max = Math.Max(0, sv.Extent.Width - sv.Viewport.Width);
            sv.Offset = sv.Offset.WithX(Math.Clamp(pressOffset - (e.GetPosition(sv).X - start.X), 0, max));
        };
        void End()
        {
            press = null;
            sv.Cursor = null;
        }
        sv.PointerReleased += (_, e) =>
        {
            if (press is null) return;
            End();
            e.Pointer.Capture(null);
        };
        sv.PointerCaptureLost += (_, _) => End();
        sv.ScrollChanged += (_, _) => _scrollX[layer] = sv.Offset.X;
        if (_scrollX.TryGetValue(layer, out var x) && x > 0)
            Dispatcher.UIThread.Post(() => sv.Offset = sv.Offset.WithX(x), DispatcherPriority.Loaded);
        return sv;
    }

    /// <summary>
    /// 스킨 설정 창을 연다(이미 열려 있으면 앞으로). <paramref name="focus"/>가 있으면 그 레이어 줄을 강조하고 그 줄로 스크롤한다
    /// (스킨이 하나뿐인 레이어도 줄을 보여 거기서 스킨을 더할 수 있다).
    /// </summary>
    public static void Open(EditorContext ctx, Visual? owner, Layer? focus = null)
    {
        var w = _instance;
        if (w is null)
        {
            w = new SkinManagerWindow(ctx);
            _instance = w;
            w.Closed += (_, _) => { if (ReferenceEquals(_instance, w)) _instance = null; };
            w.SetFocus(focus);
            w.Rebuild();
            if ((Ui.MainWindow ?? Ui.Owner(owner)) is { } main && !ReferenceEquals(main, w)) w.Show(main);
            else w.Show();
            return;
        }
        w.SetFocus(focus);
        w.Rebuild();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    private void SetFocus(Layer? focus)
    {
        _focus = focus is { IsTilemap: false } ? focus : null;
        _scrollToFocus = _focus is not null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _ctx.ProjectChanged -= OnProjectChanged;
        _ctx.CurrentChanged -= OnCurrentChanged;
        _ctx.ProjectReplaced -= OnProjectReplaced;
        _presets.Children.Clear();
        _list.Children.Clear();
        DisposeBitmaps();
        base.OnClosed(e);
    }

    // ───────────── 다시 만들기 ─────────────

    private int _builtFrame = -1;

    private void Schedule()
    {
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void OnProjectChanged(object? sender, ProjectChangedEventArgs e)
    {
        // 프리셋 목록 편집은 Layers | Settings로 온다(Settings만 와도 다시 만듦).
        if ((e.Change & (ProjectChange.Layers | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Canvas | ProjectChange.Settings)) != 0) Schedule();
    }

    private void OnCurrentChanged(object? sender, EventArgs e)
    {
        if (_ctx.CurrentFrame != _builtFrame) Schedule();
    }

    private void OnProjectReplaced(object? sender, EventArgs e)
    {
        _focus = null;
        _presetWarning = null;
        _scrollToFocus = false;
        _picked.Clear();
        _scrollX.Clear();
        Schedule();
    }

    private void DisposeBitmaps()
    {
        foreach (var b in _bitmaps) b.Dispose();
        _bitmaps.Clear();
    }

    private WriteableBitmap Track(PixelImage image)
    {
        var b = ImageUtil.ToBitmap(image);
        _bitmaps.Add(b);
        return b;
    }

    private void Rebuild()
    {
        _timer.Stop();
        _list.Children.Clear();
        _presets.Children.Clear();
        DisposeBitmaps();
        if (_ctx.Project is not { } p || p.Frames.Count == 0)
        {
            _builtFrame = -1;
            _picked.Clear();
            _presetBox.IsVisible = false;
            _list.Children.Add(Note(UiStrings.S_SkinManagerWindow_Rebuild_5A6FE8B9B0));
            UpdateFooter(null);
            return;
        }
        int frame = Math.Clamp(_ctx.CurrentFrame, 0, p.Frames.Count - 1);
        _builtFrame = _ctx.CurrentFrame;
        // 지운 레이어·스킨을 가리키는 고르기는 버린다.
        foreach (var (layer, skin) in _picked.ToList())
            if (p.IndexOf(layer) < 0 || !layer.Skins.Contains(skin)) _picked.Remove(layer);
        if (_focus is not null && p.IndexOf(_focus) < 0) _focus = null;

        _presetBox.IsVisible = true;
        BuildPresets(p, frame);

        Control? focusRow = null;
        for (int i = p.Layers.Count - 1; i >= 0; i--)
        {
            var layer = p.Layers[i];
            if (layer.IsTilemap || (layer.Skins.Count < 2 && !ReferenceEquals(layer, _focus))) continue;
            var row = LayerRow(p, layer, i, frame);
            if (ReferenceEquals(layer, _focus)) focusRow = row;
            _list.Children.Add(row);
        }
        if (_list.Children.Count == 0) _list.Children.Add(Note(UiStrings.SkinSettings_LayersEmpty));
        UpdateFooter(p);
        if (_scrollToFocus && focusRow is not null)
        {
            _scrollToFocus = false;
            var target = focusRow;
            Dispatcher.UIThread.Post(() => target.BringIntoView(), DispatcherPriority.Background);
        }
    }

    private void UpdateFooter(SpriteProject? p)
    {
        _savePreset.IsEnabled = p is not null && (_picked.Count > 0 || p.CurrentSkinEntries().Count > 0);
        _clearPicked.IsVisible = _picked.Count > 0;
        _pickedText.Text = p is null ? ""
            : _picked.Count > 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.SkinSettings_Picked, _picked.Count)
            : UiStrings.SkinSettings_PickedNone;
    }

    private static TextBlock Note(string text) => new()
    {
        Text = text,
        Foreground = AppTheme.DimTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(2, 4),
    };

    // ───────────── 스킨 프리셋 ─────────────

    /// <summary>스킨 프리셋 묶음 안: 건너뛴 항목 안내, 프리셋 카드(누르면 입힘, 두 번 = 입히고 닫기, 우클릭 = 메뉴).</summary>
    private void BuildPresets(SpriteProject p, int frame)
    {
        if (_presetWarning is { } warning)
        {
            var line = new TextBlock
            {
                Text = warning + UiStrings.S_SkinManagerWindow_OutfitSection_C353654C72,
                Foreground = AppTheme.WarningBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 2, 4),
                Cursor = HandCursor,
            };
            line.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                _presetWarning = null;
                Dispatcher.UIThread.Post(Rebuild);
            };
            _presets.Children.Add(line);
        }
        var presets = p.SkinPresets;
        if (presets.Count == 0)
        {
            _presets.Children.Add(Note(UiStrings.SkinSettings_PresetsEmpty));
            return;
        }
        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < presets.Count; i++)
        {
            var preset = presets[i];
            var missing = p.MissingSkinPresetEntries(preset);
            var bmp = Track(p.FlattenSkinPreset(frame, preset));
            var card = Card(bmp, PresetThumb, preset.Name, IsCurrentPreset(p, preset), picked: false, missing: missing.Count > 0).HelpAt(PresetsPage, anchor: "click ⑤ its card");
            var tip = string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.SkinSettings_PresetTip, preset.Name, preset.Entries.Count);
            if (missing.Count > 0) tip += "\n" + UiStrings.SkinSettings_PresetMissing + "\n" + string.Join("\n", missing.Select(m => "· " + m));
            card.Tip(tip);
            int index = i;
            Clickable(card, () => ApplyPreset(preset), pick: null, () => PresetCardMenu(preset, index));
            cards.Children.Add(card);
        }
        _presets.Children.Add(cards);
    }

    /// <summary>이 스킨 프리셋을 입혀도 바뀔 것이 없는지(쓸 수 있는 항목이 하나 이상이고 그 레이어가 모두 이미 그 스킨).</summary>
    public static bool IsCurrentPreset(SpriteProject p, SkinPreset preset)
    {
        bool any = false;
        foreach (var (layer, skin) in preset.Entries)
        {
            if (layer.IsTilemap || p.IndexOf(layer) < 0 || !layer.Skins.Contains(skin)) continue;
            any = true;
            if (!ReferenceEquals(layer.ActiveSkin, skin)) return false;
        }
        return any;
    }

    private void ApplyPreset(SkinPreset preset)
    {
        if (_ctx.Project is not { } p || !p.SkinPresets.Contains(preset)) return;
        IReadOnlyList<string> missing = [];
        _ctx.Structural(() => missing = p.ApplySkinPreset(preset));
        _presetWarning = missing.Count == 0 ? null : string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.SkinSettings_PresetSkipped, preset.Name, string.Join(", ", missing));
        Schedule();
    }

    /// <summary>고른 스킨(위 레이어부터), 고른 것이 없으면 빈 목록.</summary>
    private List<SkinPresetEntry> PickedEntries(SpriteProject p)
        => _picked.Select(x => new SkinPresetEntry(x.Key, x.Value)).OrderByDescending(e => p.IndexOf(e.Layer)).ToList();

    /// <summary>고른 스킨(없으면 스킨이 둘 이상인 모든 레이어의 지금 스킨)을 이름을 물어 스킨 프리셋으로 등록한다(프리셋 줄 끝, 실행취소됨).</summary>
    private void SavePreset()
    {
        if (_ctx.Project is not { } p) return;
        var entries = PickedEntries(p);
        // 고른 스킨 이름이 모두 같으면 그 이름을 제안(예: 레이어마다 "knight")
        string suggested = entries.Count > 0 && entries.All(e => e.Skin.Name == entries[0].Skin.Name) ? entries[0].Skin.Name : "";
        var name = InputDialog.Ask(this, UiStrings.SkinSettings_SavePresetTitle, entries.Count > 0
            ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.SkinSettings_SavePresetPicked, entries.Count)
            : UiStrings.SkinSettings_SavePresetAll, p.UniqueSkinPresetName(suggested));
        if (name is null || !ReferenceEquals(_ctx.Project, p)) return;
        try
        {
            _ctx.Structural(() => p.SaveSkinPreset(name, entries.Count > 0 ? entries : null));
            _picked.Clear();
            Schedule();
        }
        catch (ArgumentException ex)
        {
            Ui.Error(this, SkinOps.Message(ex));
        }
    }

    private void RenamePreset(SkinPreset preset)
    {
        if (_ctx.Project is not { } p) return;
        var name = InputDialog.Ask(this, UiStrings.SkinSettings_RenamePreset, UiStrings.SkinSettings_PresetName, preset.Name);
        if (name is null || !p.SkinPresets.Contains(preset)) return;
        try
        {
            _ctx.Structural(() => p.RenameSkinPreset(preset, name));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(this, SkinOps.Message(ex));
        }
    }

    private Control[] PresetCardMenu(SkinPreset preset, int index)
    {
        int count = _ctx.Project?.SkinPresets.Count ?? 0;
        void Run(Action<SpriteProject> action)
        {
            if (_ctx.Project is { } p && p.SkinPresets.Contains(preset)) _ctx.Structural(() => action(p));
        }
        return
        [
            Item(UiStrings.S_SkinManagerWindow_OutfitCardMenu_C88DD8A38B, () => ApplyPreset(preset)),
            Item(UiStrings.SkinSettings_Overwrite, () => Run(p => p.OverwriteSkinPreset(preset))).Tip(UiStrings.SkinSettings_OverwriteTip),
            Item(UiStrings.S_SkinManagerWindow_OutfitCardMenu_B895F5A706, () => RenamePreset(preset)),
            Item(UiStrings.S_SkinManagerWindow_OutfitCardMenu_890201A7A6, () => Run(p => p.DeleteSkinPreset(preset))),
            Item(UiStrings.S_SkinManagerWindow_OutfitCardMenu_4992E182C5, () => Run(p => p.MoveSkinPreset(index, index - 1)), index > 0),
            Item(UiStrings.S_SkinManagerWindow_OutfitCardMenu_DAFD1AEAB0, () => Run(p => p.MoveSkinPreset(index, index + 1)), index < count - 1),
        ];
    }

    // ───────────── 레이어 스킨 ─────────────

    /// <summary>레이어 한 줄(나무 깊이만큼 들여 씀): 이름 + 스킨 카드(끌어서 순서, 맨 앞 = 기본) + 새 스킨/복제.</summary>
    private Control LayerRow(SpriteProject p, Layer layer, int index, int frame)
    {
        bool focused = ReferenceEquals(layer, _focus);
        var name = new TextBlock
        {
            Text = layer.Name,
            FontWeight = FontWeight.Bold,
            Foreground = p.IsLayerVisible(layer) ? AppTheme.TextBrush : AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 4),
        };

        // 카드는 한 줄로 늘어놓아 끌어서 순서를 바꾼다(맨 앞 = 기본 스킨). 누르기만 하면 그 스킨을 입힌다.
        var cards = new StackPanel { Orientation = Orientation.Horizontal };
        var drag = new DragReorder(Orientation.Horizontal);
        drag.Moved += (from, to) => Dispatcher.UIThread.Post(() => SkinOps.MoveSkin(_ctx, layer, from, to));
        var skins = layer.Skins;
        _picked.TryGetValue(layer, out var picked);
        for (int i = 0; i < skins.Count; i++)
        {
            var skin = skins[i];
            // 카드 그림 = 타임라인 칸과 같은 썸네일(효과 없이, 그 레이어 자리로 자름 — SpriteProject.RenderCelThumbnail)
            var bmp = p.RenderCelThumbnail(index, frame, skin) is { } thumb ? Track(thumb) : null;
            var card = Card(bmp, CardThumb, i == 0 ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_SkinManagerWindow_GroupRow_3C53E562C4, skin.Name) : skin.Name,
                    ReferenceEquals(skin, layer.ActiveSkin), ReferenceEquals(skin, picked), missing: false, empty: SizeOf(p, layer, index))
                .HelpAt(Page, anchor: "In a card's right-click menu");
            card.Tip(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.SkinSettings_SkinTip, layer.Name, skin.Name));
            int at = i;
            Clickable(card, () => _ctx.SetActiveSkin(layer, skin), () => TogglePick(layer, skin), () => SkinCardMenu(layer, skin, at), onRelease: true);
            drag.Attach(card);
            cards.Children.Add(card);
        }
        var add = Ui.Button(VectorIcon.Plus, UiStrings.S_SkinManagerWindow_GroupRow_9C2AC337EC, (_, _) => SkinOps.AddSkin(_ctx, layer, null)).HelpAt("parts-skin-empty", anchor: "New skin (empty)").Tip(UiStrings.S_SkinManagerWindow_GroupRow_DB88A91B7D);
        var dup = Ui.Button(UiStrings.S_SkinManagerWindow_GroupRow_A6B044A804, (_, _) => SkinOps.AddSkin(_ctx, layer, layer.ActiveSkin)).HelpAt("parts-skin-empty", anchor: "Duplicate").Tip(UiStrings.S_SkinManagerWindow_GroupRow_9D0EB19E37);
        add.HorizontalAlignment = dup.HorizontalAlignment = HorizontalAlignment.Stretch;
        // 단추는 카드 줄 끝에(등록하지 않은 줄이라 끄는 동안 제자리를 지킨다).
        cards.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), Children = { add, dup } });

        return new Border
        {
            Margin = new Thickness(p.DepthOf(layer) * IndentStep, 0, 0, 0),
            Background = AppTheme.BackBrush,
            BorderBrush = focused ? AppTheme.AccentBrush : AppTheme.BorderBrush,
            BorderThickness = new Thickness(focused ? 2 : 1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(6),
            Child = new StackPanel { Children = { name, PanScroll(layer, cards) } },
        }.HelpAt(Page, anchor: "skin cards for each group");
    }

    /// <summary>빈 셀 카드의 체커 비율: 자리 있는 레이어는 그 크기, 일반 레이어는 썸네일 자리(없으면 캔버스).</summary>
    private static Size SizeOf(SpriteProject p, Layer layer, int index)
    {
        if (layer.IsPart) return layer.PartSize;
        var area = p.ThumbnailBounds(index);
        return area.IsEmpty ? p.Size : area.Size;
    }

    /// <summary>Ctrl/⌘+클릭: 그 레이어에서 이 스킨을 고르거나(같은 줄의 다른 고르기는 바뀜), 이미 고른 스킨이면 고르기를 푼다.</summary>
    private void TogglePick(Layer layer, Skin skin)
    {
        if (_picked.TryGetValue(layer, out var old) && ReferenceEquals(old, skin)) _picked.Remove(layer);
        else _picked[layer] = skin;
        Rebuild();
    }

    private Control[] SkinCardMenu(Layer layer, Skin skin, int index)
    {
        int count = layer.Skins.Count;
        bool picked = _picked.TryGetValue(layer, out var cur) && ReferenceEquals(cur, skin);
        return
        [
            Item(UiStrings.S_SkinManagerWindow_GroupCardMenu_DBEF21539E, () => _ctx.SetActiveSkin(layer, skin)),
            Item(picked ? UiStrings.SkinSettings_Unpick : UiStrings.SkinSettings_Pick, () => TogglePick(layer, skin)),
            new Separator(),
            Item(UiStrings.S_SkinManagerWindow_GroupCardMenu_F64B3478B8, () => SkinOps.AddSkin(_ctx, layer, skin)),
            Item(UiStrings.S_SkinManagerWindow_GroupCardMenu_120694637E, () => SkinOps.RenameSkin(_ctx, this, layer, skin)),
            Item(UiStrings.S_SkinManagerWindow_GroupCardMenu_61F6FB267B, () => SkinOps.DeleteSkin(_ctx, this, layer, skin), count > 1),
            Item(UiStrings.S_SkinManagerWindow_GroupCardMenu_676B513AAC, () => SkinOps.MoveSkin(_ctx, layer, index, index - 1), index > 0),
            Item(UiStrings.S_SkinManagerWindow_GroupCardMenu_B0B89947BD, () => SkinOps.MoveSkin(_ctx, layer, index, index + 1), index < count - 1),
        ];
    }

    // ───────────── 카드 ─────────────

    /// <summary>
    /// 썸네일 + 이름 카드. 지금 스킨(입힌 프리셋)이면 강조 테두리, 프리셋용으로 고른 카드면 왼쪽 위 체크 표시,
    /// 쓸 수 없는 항목이 있는 프리셋이면 오른쪽 위 경고 표시. <paramref name="empty"/> = 그림이 없을 때 체커 비율.
    /// </summary>
    private static Border Card(Bitmap? bmp, int thumb, string title, bool selected, bool picked, bool missing, Size? empty = null)
    {
        var source = bmp is not null ? new Size(bmp.PixelSize.Width, bmp.PixelSize.Height) : empty ?? new Size(1, 1);
        var image = new SkinThumb(bmp, source) { Width = thumb, Height = thumb };
        var panel = new Panel { Children = { image } };
        if (missing) panel.Children.Add(Mark(VectorIcon.CircleAlert, AppTheme.WarningBrush, HorizontalAlignment.Right));
        if (picked) panel.Children.Add(Mark(VectorIcon.Check, AppTheme.AccentBrush, HorizontalAlignment.Left));
        var text = new TextBlock
        {
            Text = title,
            FontSize = AppTheme.SmallFontSize,
            Foreground = selected ? AppTheme.TextBrush : AppTheme.DimTextBrush,
            FontWeight = selected ? FontWeight.Bold : FontWeight.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            MaxWidth = thumb + 8,
            Margin = new Thickness(0, 2, 0, 0),
        };
        return new Border
        {
            Width = thumb + 12,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(selected ? 3 : 4),
            Background = selected ? AppTheme.HoverBrush : AppTheme.PanelBrush,
            BorderBrush = selected ? AppTheme.AccentBrush : picked ? AppTheme.TextBrush : AppTheme.BorderBrush,
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(3),
            Cursor = HandCursor,
            Child = new StackPanel { Children = { panel, text } },
        };
    }

    /// <summary>카드 위쪽 모서리의 작은 원 바탕 아이콘(고름 = 체크, 빠진 항목 = 경고).</summary>
    private static Border Mark(VectorIcon icon, IBrush brush, HorizontalAlignment side) => new()
    {
        Width = 16,
        Height = 16,
        CornerRadius = new CornerRadius(8),
        Background = brush,
        HorizontalAlignment = side,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(2),
        Child = new IconView(icon, Brushes.White, 2.6) { Width = 11, Height = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
    };

    /// <summary>
    /// 왼쪽 클릭 = <paramref name="apply"/>, 두 번 누르기 = 적용하고 창 닫기, Ctrl/⌘+클릭 = <paramref name="pick"/>(있을 때), 오른쪽 클릭 = 메뉴.
    /// </summary>
    /// <param name="onRelease">끌어서 순서를 바꾸는 카드: 누를 때가 아니라 끌지 않고 놓을 때 입힌다(끌면 <see cref="DragReorder"/>가 놓기를 처리함).</param>
    private void Clickable(Control card, Action apply, Action? pick, Func<Control[]> menu, bool onRelease = false)
    {
        bool PickKey(KeyModifiers mods) => pick is not null && (mods & Platform.CommandModifier) != 0;
        card.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            if (PickKey(e.KeyModifiers))
            {
                if (!onRelease) Dispatcher.UIThread.Post(pick!);
                return;
            }
            if (e.ClickCount >= 2)
            {
                Dispatcher.UIThread.Post(() => { apply(); Close(); });
                return;
            }
            // 다시 만들기가 이 카드를 지우므로 처리기가 끝난 뒤에 적용한다.
            if (!onRelease) Dispatcher.UIThread.Post(apply);
        };
        if (onRelease)
            card.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left || !card.Bounds.WithX(0).WithY(0).Contains(e.GetPosition(card))) return;
                e.Handled = true;
                Dispatcher.UIThread.Post(PickKey(e.KeyModifiers) ? pick! : apply);
            };
        PopupMenus.Attach(card, menu);
    }

    private static MenuItem Item(string text, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = new TextBlock { Text = text }, IsEnabled = enabled };
        // 메뉴가 닫힌 뒤 실행해 대화상자가 메뉴와 겹치지 않게 한다.
        item.Click += (_, _) => Dispatcher.UIThread.Post(action);
        return item;
    }

    // ───────────── 썸네일 ─────────────

    /// <summary>체커 위에 비트맵을 정수 배율(최근접)로 가운데 맞춰 그린다. 비트맵이 없으면 원본 비율의 체커만.</summary>
    private sealed class SkinThumb(Bitmap? bitmap, Size source) : Control
    {
        public override void Render(DrawingContext g)
        {
            var area = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.InputBrush, area);
            var inner = area.Deflate(2);
            if (source.Width <= 0 || source.Height <= 0 || inner.Width <= 0 || inner.Height <= 0) return;
            var src = new Rectangle(0, 0, source.Width, source.Height);
            if (bitmap is not null)
            {
                ImageUtil.DrawFitted(g, bitmap, src, inner, checker: true);
                return;
            }
            double fit = Math.Min(inner.Width / source.Width, inner.Height / source.Height);
            double scale = fit >= 1 ? Math.Floor(fit) : fit;
            double w = Math.Max(1, Math.Floor(source.Width * scale)), h = Math.Max(1, Math.Floor(source.Height * scale));
            var dest = new Rect(Math.Floor(inner.X + (inner.Width - w) / 2), Math.Floor(inner.Y + (inner.Height - h) / 2), w, h);
            AppTheme.DrawChecker(g, dest, Math.Max(2, Math.Min(8, (int)scale * 2)));
        }
    }

    // ───────────── 상태 글 ─────────────

    /// <summary>레이어의 스킨 상태 한 줄("스킨: knight"). 스킨이 하나뿐이거나 타일맵이면 "".</summary>
    public static string SkinStatus(Layer layer)
        => layer.Skins.Count < 2 || layer.ActiveSkin is not { } active ? "" : string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_SkinManagerWindow_SkinStatus_24731B367D, active.Name);
}

/// <summary>스킨 편집(실행취소됨) 공용 동작: 타임라인 메뉴와 스킨 설정 창이 함께 쓴다. 오류는 대화상자로 알린다.</summary>
public static class SkinOps
{
    /// <summary>레이어에 스킨을 더한다(<paramref name="copyFrom"/>이 있으면 그 스킨 그림 복제). 새 스킨이 지금 스킨이 된다.</summary>
    public static void AddSkin(EditorContext ctx, Layer layer, Skin? copyFrom)
    {
        if (ctx.Project is not { } p) return;
        ctx.Structural(() => p.AddSkin(layer, null, copyFrom));
    }

    /// <summary>
    /// "자식까지": 레이어와 모든 자손에 같은 이름 스킨을 더해 입히고 그 묶음을 같은 이름의 스킨 프리셋으로 등록한다
    /// (<paramref name="copy"/>면 레이어마다 지금 스킨 그림 복제, 아니면 빈 스킨).
    /// </summary>
    public static void AddSkinToSubtree(EditorContext ctx, Visual owner, Layer layer, bool copy)
    {
        if (ctx.Project is not { } p) return;
        try
        {
            ctx.Structural(() => p.AddSkinToSubtree(layer, null, copy));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(owner, Message(ex));
        }
    }

    public static void RenameSkin(EditorContext ctx, Visual owner, Layer layer, Skin skin)
    {
        if (ctx.Project is not { } p) return;
        var name = InputDialog.Ask(owner, UiStrings.S_SkinManagerWindow_RenameSkin_5BEFE451AB,
            string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.SkinSettings_RenameSkinPrompt, layer.Name), skin.Name);
        if (name is null) return;
        try
        {
            ctx.Structural(() => p.RenameSkin(layer, skin, name));
        }
        catch (ArgumentException ex)
        {
            Ui.Error(owner, Message(ex));
        }
    }

    public static void DeleteSkin(EditorContext ctx, Visual owner, Layer layer, Skin skin)
    {
        if (ctx.Project is not { } p) return;
        try
        {
            ctx.Structural(() => p.DeleteSkin(layer, skin));
        }
        catch (InvalidOperationException ex)
        {
            Ui.Info(owner, ex.Message);
        }
    }

    public static void MoveSkin(EditorContext ctx, Layer layer, int from, int to)
    {
        if (ctx.Project is not { } p || to < 0 || to >= layer.Skins.Count) return;
        ctx.Structural(() => p.MoveSkin(layer, from, to));
    }

    /// <summary>예외 글에서 " (Parameter '…')"를 뗀다.</summary>
    internal static string Message(ArgumentException ex)
        => ex.ParamName is { } param ? ex.Message.Replace($" (Parameter '{param}')", "") : ex.Message;
}
