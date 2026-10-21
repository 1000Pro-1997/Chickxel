using System.Diagnostics;
using Avalonia.Controls.Templates;
using Gpixel.Core.Model;

namespace Gpixel.App;

/// <summary>
/// Piskel식 미리보기(오른쪽 위): 현재 태그(없으면 전체 프레임)를 실시간으로 재생한다.
/// 자체 FPS(0 = 프레임 길이 사용), 배율 1×/2×/4×/맞춤, 배경색을 고를 수 있고, 멈춰 있으면 현재 프레임을 보여 준다.
/// 그룹(스킨 슬롯)이 있으면 아래쪽 "착장" 줄에서 그룹마다(타임라인처럼 위→아래) 보일 스킨을 고르고, "관리…"로 스킨 관리 창을 연다.
/// </summary>
public sealed class PreviewPanel : UserControl
{
    private static readonly string[] Backgrounds = ["체커", "검정", "흰색", "회색", "초록", "사용자 색…"];
    private static readonly Color?[] PresetColors = [null, Colors.Black, Colors.White, Color.FromRgb(128, 128, 128), Color.FromRgb(60, 140, 70), null];

    /// <summary>배경 목록 항목: 이름 + 색 견본(null = 체커).</summary>
    private sealed record BackgroundOption(string Name, Color? Color);

    private readonly EditorContext _ctx;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private readonly Stopwatch _clock = new();
    private readonly View _view;
    private readonly Button _play;
    private readonly NumericUpDown _fps;
    private readonly ComboBox _background;
    private readonly Button _scaleButton;
    private static readonly int[] ScaleSteps = [0, 1, 2, 4];
    private int _scale; // 0 = 맞춤
    private Color? _bgColor;
    private Color _customColor = Color.FromRgb(40, 60, 90);
    private bool _suppressBackground;
    private List<int> _order = new();
    private int _orderIndex;
    private double _elapsedInFrame;
    /// <summary>착장 줄: 그룹마다 이름 + 스킨 고르기 상자. 그룹이 없으면 숨김.</summary>
    private readonly WrapPanel _outfit = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 1) };
    private readonly Border _outfitHost;
    /// <summary>착장 줄을 다시 만드는 중(상자 선택 바뀜을 스킨 고르기로 보지 않음).</summary>
    private bool _syncingOutfit;

    /// <summary>재생 중 보이는 프레임이 바뀜(-1 = 재생 멈춤).</summary>
    public event EventHandler<int>? PlayingFrameChanged;

    public PreviewPanel(EditorContext ctx)
    {
        _ctx = ctx;
        Background = AppTheme.PanelBrush;
        _view = new View(this);
        _play = Ui.Button("▶", (_, _) => TogglePlay(), 34).Tip("재생/정지 (Shift+Enter)");
        _fps = Ui.Num(0, 60, 0, 46)
            .Presets([0, 4, 6, 8, 10, 12, 15, 20, 24, 30, 60], [], v => v == 0 ? "0 (프레임마다 정한 길이)" : $"{v:0} FPS")
            .RecentKey("preview.fps")
            .Tip("미리보기 FPS (0 = 프레임마다 정한 길이)");
        _scaleButton = new Button
        {
            Width = 52,
            Margin = new Thickness(1, 2, 1, 2),
            Padding = new Thickness(0, 2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        }.Tip("배율: 클릭하면 맞춤 → 1× → 2× → 4× 차례로, 오른쪽 클릭은 거꾸로");
        _scaleButton.Click += (_, _) => StepScale(+1);
        _scaleButton.PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Right) return;
            StepScale(-1);
            e.Handled = true;
        };
        _background = new ComboBox
        {
            Margin = new Thickness(1, 2, 1, 2),
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            // 펼친 목록은 견본 + 이름, 닫힌 상자는 견본만.
            ItemTemplate = new FuncDataTemplate<BackgroundOption>((o, _) => SwatchRow(o), supportsRecycling: false),
            SelectionBoxItemTemplate = new FuncDataTemplate<BackgroundOption>((o, _) => Swatch(o), supportsRecycling: false),
        };
        for (int i = 0; i < Backgrounds.Length; i++)
            _background.Items.Add(new BackgroundOption(Backgrounds[i], i == Backgrounds.Length - 1 ? _customColor : PresetColors[i]));
        _background.SelectedIndex = 0;
        _background.Tip("미리보기 배경");
        _background.SelectionChanged += (_, _) => { if (!_suppressBackground) OnBackgroundChanged(); };
        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Background = AppTheme.PanelBrush };
        DockPanel.SetDock(bar, Dock.Bottom);
        bar.Children.Add(_play);
        var fpsLabel = Ui.Label("FPS");
        fpsLabel.VerticalAlignment = VerticalAlignment.Center;
        bar.Children.Add(fpsLabel);
        bar.Children.Add(_fps);
        bar.Children.Add(_scaleButton);
        bar.Children.Add(_background);
        _outfitHost = new Border
        {
            Background = AppTheme.PanelBrush,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            IsVisible = false,
            Child = new ScrollViewer
            {
                Content = _outfit,
                MaxHeight = 92,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            },
        };
        DockPanel.SetDock(_outfitHost, Dock.Bottom);
        var root = new DockPanel();
        root.Children.Add(Ui.Header("미리보기"));
        root.Children.Add(bar);
        root.Children.Add(_outfitHost);
        root.Children.Add(_view);
        Content = root;
        SetScale(0);

        _timer.Tick += (_, _) => Tick();
        ctx.ProjectReplaced += (_, _) =>
        {
            Stop();
            RebuildOutfit();
            _view.InvalidateVisual();
        };
        ctx.ViewChanged += (_, _) => _view.InvalidateVisual();
        // 재생 중에 태그를 만들거나 고르면 그 태그 안에서 반복하도록 순서를 바꾼다.
        ctx.CurrentChanged += (_, _) => RefreshOrder();
        ctx.ProjectChanged += (_, e) =>
        {
            if ((e.Change & (ProjectChange.Tags | ProjectChange.Frames)) != 0) RefreshOrder();
            // 스킨 고르기·추가·삭제·이름·순서와 그룹 추가·삭제·이름은 모두 Layers 변경이다(실행취소 포함).
            if ((e.Change & ProjectChange.Layers) != 0) RebuildOutfit();
        };
        RebuildOutfit();
    }

    // ───────────── 착장(그룹마다 보일 스킨) ─────────────

    /// <summary>착장 줄을 프로젝트에 맞춰 다시 만든다(그룹이 없으면 숨김).</summary>
    private void RebuildOutfit()
    {
        _syncingOutfit = true;
        try
        {
            _outfit.Children.Clear();
            var p = _ctx.Project;
            var groups = p is null ? [] : p.Layers.Where(l => l.IsGroup).Reverse().ToList();
            _outfitHost.IsVisible = groups.Count > 0;
            if (p is null || groups.Count == 0) return;
            var title = Ui.Label("착장");
            title.Foreground = AppTheme.DimTextBrush;
            title.VerticalAlignment = VerticalAlignment.Center;
            title.Margin = new Thickness(2, 0, 4, 0);
            _outfit.Children.Add(title);
            foreach (var group in groups) _outfit.Children.Add(OutfitItem(p, group));
            var manage = new Button
            {
                Content = "관리…",
                Padding = new Thickness(6, 0),
                MinHeight = 20,
                Margin = new Thickness(2, 2, 2, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Focusable = false,
            }.Tip("스킨 관리 창: 스킨 카드로 갈아입히기·추가·복제·이름·삭제, 부위마다 따로 고르기");
            manage.Click += (_, _) => SkinManagerWindow.Open(_ctx, this);
            _outfit.Children.Add(manage);
        }
        finally
        {
            _syncingOutfit = false;
        }
    }

    /// <summary>
    /// 그룹 하나: 이름(누르면 스킨 관리 창이 그 그룹으로, 부위만 따로 고른 스킨이 있으면 "*") + 스킨 상자(스킨이 하나뿐이면 끔).
    /// 고르면 그 그룹의 지금 스킨이 바뀐다(부위만 따로 고른 스킨은 그대로).
    /// </summary>
    private Control OutfitItem(SpriteProject p, Layer group)
    {
        int overrides = p.ChildrenOf(group).Count(x => x.SkinOverride is not null);
        var label = new TextBlock
        {
            Text = overrides > 0 ? group.Name + " *" : group.Name,
            Foreground = group.Visible ? AppTheme.TextBrush : AppTheme.DimTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 80,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(4, 0, 2, 0),
            Cursor = new Cursor(StandardCursorType.Hand),
        }.Tip(overrides > 0 ? $"\"{group.Name}\": 부위 {overrides}개는 따로 고른 스킨 — 눌러서 스킨 관리" : $"\"{group.Name}\" — 눌러서 스킨 관리");
        label.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(label).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            Dispatcher.UIThread.Post(() => SkinManagerWindow.Open(_ctx, this, group));
        };
        var combo = new ComboBox
        {
            Width = 92,
            MinWidth = 0,
            Margin = new Thickness(1, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
            IsEnabled = group.Skins.Count > 1,
        };
        var skins = group.Skins.ToList();
        for (int i = 0; i < skins.Count; i++)
        {
            combo.Items.Add(i == 0 && skins.Count > 1 ? $"{skins[i].Name} (기본)" : skins[i].Name);
            if (ReferenceEquals(skins[i], group.ActiveSkin)) combo.SelectedIndex = i;
        }
        combo.Tip(skins.Count > 1
            ? $"\"{group.Name}\" 그룹에 보일 스킨"
            : $"\"{group.Name}\" 그룹은 스킨이 하나뿐입니다 — \"관리…\"에서 스킨을 더하세요");
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncingOutfit || combo.SelectedIndex < 0 || combo.SelectedIndex >= skins.Count) return;
            var skin = skins[combo.SelectedIndex];
            // 고르기가 착장 줄을 다시 만들므로, 상자가 닫힌 뒤에 바꾼다.
            Dispatcher.UIThread.Post(() =>
            {
                if (_ctx.Project is { } p && p.Layers.Contains(group) && group.Skins.Contains(skin)) _ctx.SetActiveSkin(group, skin);
            });
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Children = { label, combo } };
    }

    private void StepScale(int direction)
    {
        int i = Array.IndexOf(ScaleSteps, _scale);
        SetScale(ScaleSteps[(i + direction + ScaleSteps.Length) % ScaleSteps.Length]);
    }

    private void SetScale(int scale)
    {
        _scale = scale;
        _scaleButton.Content = scale == 0 ? "맞춤" : $"{scale}×";
        _view.InvalidateVisual();
    }

    /// <summary>배율 설정값(0 = 맞춤).</summary>
    public int ZoomSetting
    {
        get => _scale;
        set => SetScale(value is 1 or 2 or 4 ? value : 0);
    }

    /// <summary>미리보기 FPS(0 = 프레임 길이).</summary>
    public int FpsSetting
    {
        get => (int)_fps.NumValue();
        set => _fps.Value = Math.Clamp(value, 0, 60);
    }

    /// <summary>배경 설정값("checker" 또는 #RRGGBB).</summary>
    public string BackgroundSetting
    {
        get => _bgColor is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : "checker";
        set
        {
            if (value != "checker" && Color32.TryParseHex(value, out var c))
            {
                _customColor = c.ToColor();
                _bgColor = _customColor;
                UpdateCustomOption();
                int idx = Array.FindIndex(PresetColors, p => p == _customColor);
                _suppressBackground = true;
                _background.SelectedIndex = idx >= 0 ? idx : Backgrounds.Length - 1;
                _suppressBackground = false;
            }
            _view.InvalidateVisual();
        }
    }

    private void OnBackgroundChanged()
    {
        int i = _background.SelectedIndex;
        if (i < 0) return;
        if (i == Backgrounds.Length - 1)
        {
            if (Ui.PickColor(this, _customColor.ToColor32()) is { } picked) _customColor = picked.ToColor();
            _bgColor = _customColor;
            UpdateCustomOption();
        }
        else _bgColor = PresetColors[i];
        _view.InvalidateVisual();
    }

    /// <summary>"사용자 색…" 항목의 견본을 현재 사용자 색으로 바꾼다(선택은 유지).</summary>
    private void UpdateCustomOption()
    {
        int last = Backgrounds.Length - 1;
        if (_background.Items[last] is BackgroundOption { Color: var c } && c == _customColor) return;
        bool selected = _background.SelectedIndex == last;
        _suppressBackground = true;
        _background.Items[last] = new BackgroundOption(Backgrounds[last], _customColor);
        if (selected) _background.SelectedIndex = last;
        _suppressBackground = false;
    }

    /// <summary>둥근 색 견본 + 이름(닫힌 상자와 펼친 목록 모두).</summary>
    private static Control SwatchRow(BackgroundOption? o) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { Swatch(o), new TextBlock { Text = o?.Name ?? "", VerticalAlignment = VerticalAlignment.Center } },
    };

    private static Control Swatch(BackgroundOption? o)
    {
        return new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new SwatchFill(o?.Color),
        };
    }

    /// <summary>견본 안쪽: 색이 없으면 체커.</summary>
    private sealed class SwatchFill(Color? color) : Control
    {
        public override void Render(DrawingContext g)
        {
            var r = new Rect(Bounds.Size);
            if (color is { } c) g.FillRectangle(new SolidColorBrush(c), r);
            else AppTheme.DrawChecker(g, r, 4);
        }
    }

    // ───────────── 재생 ─────────────

    public bool IsPlaying => _timer.IsEnabled;

    /// <summary>재생 중 보이는 프레임(멈춰 있으면 현재 프레임).</summary>
    public int ShownFrame => IsPlaying && _orderIndex < _order.Count ? _order[_orderIndex] : _ctx.CurrentFrame;

    public void TogglePlay()
    {
        if (IsPlaying) Stop();
        else Play();
    }

    public void Play()
    {
        if (!_ctx.HasProject || _ctx.FrameCount == 0) return;
        _order = _ctx.PlaybackOrder();
        if (_order.Count == 0) return;
        int at = _order.IndexOf(_ctx.CurrentFrame);
        _orderIndex = at < 0 ? 0 : at;
        _elapsedInFrame = 0;
        _clock.Restart();
        _timer.Start();
        _play.Content = "⏸";
        PlayingFrameChanged?.Invoke(this, ShownFrame);
        _view.InvalidateVisual();
    }

    public void Stop()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _play.Content = "▶";
        PlayingFrameChanged?.Invoke(this, -1);
        _view.InvalidateVisual();
    }

    /// <summary>재생 중이면 재생 순서(태그 범위)를 지금 상태로 다시 정한다.</summary>
    private void RefreshOrder()
    {
        if (!IsPlaying || !_ctx.HasProject) return;
        var order = _ctx.PlaybackOrder();
        if (order.Count == 0) { Stop(); return; }
        if (order.SequenceEqual(_order)) return;
        int shown = ShownFrame;
        _order = order;
        int at = order.IndexOf(shown);
        _orderIndex = at < 0 ? 0 : at;
        _elapsedInFrame = 0;
    }

    private int FrameMs(int frame)
    {
        decimal fps = _fps.NumValue();
        return fps > 0 ? (int)Math.Round(1000 / (double)fps) : _ctx.FrameDuration(frame);
    }

    private void Tick()
    {
        if (!_ctx.HasProject || _order.Count == 0) { Stop(); return; }
        _elapsedInFrame += _clock.Elapsed.TotalMilliseconds;
        _clock.Restart();
        bool changed = false;
        int guard = 0;
        while (_elapsedInFrame >= FrameMs(_order[_orderIndex]) && guard++ < 1000)
        {
            _elapsedInFrame -= FrameMs(_order[_orderIndex]);
            if (_orderIndex + 1 < _order.Count) _orderIndex++;
            else if (_ctx.PlaybackLoops) _orderIndex = 0;
            else { Stop(); return; }
            changed = true;
        }
        if (!changed) return;
        PlayingFrameChanged?.Invoke(this, ShownFrame);
        _view.InvalidateVisual();
    }

    /// <summary>미리보기를 그리는 영역.</summary>
    private sealed class View : Control
    {
        private readonly PreviewPanel _owner;

        public View(PreviewPanel owner) => _owner = owner;

        public override void Render(DrawingContext g)
        {
            var ctx = _owner._ctx;
            var bounds = new Rect(Bounds.Size);
            if (_owner._bgColor is { } bg) g.FillRectangle(AppTheme.Brush(bg), bounds);
            else
            {
                g.FillRectangle(AppTheme.InputBrush, bounds);
                AppTheme.DrawChecker(g, bounds, 8);
            }
            if (!ctx.HasProject) return;
            var area = new Rect(4, 4, Math.Max(0, bounds.Width - 8), Math.Max(0, bounds.Height - 24));
            int frame = Math.Clamp(_owner.ShownFrame, 0, Math.Max(0, ctx.FrameCount - 1));
            if (ctx.FrameBitmap(frame) is not { } bmp) return;
            ImageUtil.DrawFitted(g, bmp, new Rectangle(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), area, _owner._scale);
            var tag = ctx.ActiveTag;
            string caption = $"{frame + 1}/{ctx.FrameCount} · {ctx.FrameDuration(frame)}ms" + (tag is null ? "" : $" · {tag.Name}");
            var textBrush = _owner._bgColor is { } c && Lightness(c) > 0.6 ? Brushes.Black : Brushes.White;
            AppTheme.DrawTextIn(g, caption, new Rect(4, bounds.Height - 18, Math.Max(1, bounds.Width - 8), 16), textBrush, AppTheme.SmallFontSize);
        }

        /// <summary>HSL 밝기(System.Drawing.Color.GetBrightness와 같은 값).</summary>
        private static double Lightness(Color c) => (Math.Max(c.R, Math.Max(c.G, c.B)) + Math.Min(c.R, Math.Min(c.G, c.B))) / 510.0;
    }
}
