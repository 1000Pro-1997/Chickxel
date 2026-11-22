using System.Globalization;
using Chickxel.App.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Pixelate;

namespace Chickxel.App;

/// <summary>
/// 선 정리(편집 메뉴 "선 정리…"): 지금 레이어·지금 프레임 셀(선택 영역이 있으면 그 안만)의 선을 <see cref="LineArt"/>로 가르고(<see cref="LineArt.Trace"/>)
/// 1px 선으로 다시 그린다(<see cref="LineArt.Apply"/> — 곧은 조각은 고른 계단, 굽은 조각은 원호, 모서리는 그대로).
/// 왼쪽은 지금 그림과 정리한 그림을 나란히(최근접 확대, 휠 = 확대, 끌기 = 옮기기 — 두 그림이 함께, 두 번 누르기 = 맞추기) 보이고, 지금 그림 위에 찾은 획을
/// 벡터 선(직선·원호 색이 다름, 모서리 = 점)으로 겹친다. 획에 마우스를 올리면 밝아지고 누르면 정리에서 빼거나(점선·흐리게, 결과에서 그대로) 다시 넣는다.
/// 오른쪽은 <see cref="LineArtOptions"/> 설정 — 바꾸면 잠시 뒤 다른 스레드에서 다시 계산한다(앞서 하던 계산의 결과는 버림).
/// "모든 프레임"이면 이 레이어의 모든 프레임을 같은 설정으로 정리한다(실행취소 한 단계, 뺀 획은 지금 프레임에만). 설정은 앱을 끌 때까지 기억한다.
/// </summary>
public sealed class LineArtDialog : DialogBase
{
    private static readonly string Label = UiStrings.LineArt_Title;
    /// <summary>마지막으로 적용한 설정(다음에 열 때의 처음 값).</summary>
    private static LineArtOptions _last = new();
    private static bool _lastAllFrames;

    private readonly PixelImage _source;
    private readonly bool[]? _area;
    private readonly CheckBox _autoDark, _allFrames;
    private readonly NumericUpDown _darkness, _colorTolerance, _maxWidth, _minLength, _tolerance, _cornerAngle, _darken;
    private readonly ComboBox _colorMode;
    private readonly WrapPanel _chips;
    private readonly ToggleButton _picker;
    private readonly TextBlock _count;
    private readonly LinePreview _view;
    private readonly UiDebouncer _debounce;
    /// <summary>스포이트로 더한 선 색(바꿀 때마다 새 배열 — 설정 비교가 같은 목록으로 알아보게).</summary>
    private IReadOnlyList<Color32> _colors = [];
    /// <summary>뺀 획의 옛 선 픽셀 자리(y × 너비 + x) — 설정을 바꿔 다시 갈라도 같은 자리를 덮는 획은 계속 뺀다.</summary>
    private readonly HashSet<int> _excludedPixels = [];
    private CancellationTokenSource? _cts;
    /// <summary>마지막으로 끝난 계산(없으면 아직 계산 중).</summary>
    private Computed? _state;
    private bool _busy, _loading;

    private LineArtDialog(PixelImage source, bool[]? area, int frames) : base(Label, UiStrings.LineArt_Apply)
    {
        _source = source;
        _area = area;

        _autoDark = Ui.Check(UiStrings.LineArt_AutoDark);
        _darkness = Ui.Num(0, 100, 35, 60, increment: 5).Unit("%").Presets(20, 25, 30, 35, 40, 50, 60);
        _colorTolerance = Ui.Num(0, 50, 6, 60).Unit("%").Presets(0, 2, 4, 6, 8, 12, 16, 24);
        _maxWidth = Ui.Num(1, 8, 2.5m, 60, decimals: 1, increment: 0.5m).Unit("px").Presets(1, 1.5m, 2, 2.5m, 3, 4, 5);
        _minLength = Ui.Num(1, 200, 3, 60).Unit("px").Presets(1, 2, 3, 4, 6, 8, 12, 16);
        _tolerance = Ui.Num(0.1m, 3, 0.6m, 60, decimals: 1, increment: 0.1m).Unit("px").Presets(0.3m, 0.5m, 0.6m, 0.8m, 1, 1.5m, 2);
        _cornerAngle = Ui.Num(5, 150, 40, 60, increment: 5).Unit("°").Presets(20, 30, 40, 50, 60, 75, 90);
        _colorMode = Ui.Combo(150, UiStrings.LineArt_ModeKeep, UiStrings.LineArt_ModeDarken);
        _darken = Ui.Num(0, 100, 50, 60, increment: 5).Unit("%").Presets(Ui.Percents);
        _picker = new ToggleButton
        {
            Content = Ui.Icon(VectorIcon.Pipette, 14),
            Margin = new Thickness(2),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _picker.Tip(UiStrings.LineArt_PickTip);
        _chips = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 340, Margin = new Thickness(3, 0, 0, 2) };
        _allFrames = Ui.Check(UiStrings.FormatLineArt_AllFrames(count: frames));
        _allFrames.Tip(UiStrings.LineArt_AllFramesTip);
        _allFrames.IsVisible = frames > 1;
        _allFrames.VerticalAlignment = VerticalAlignment.Center;

        // ── 왼쪽: 두 그림 + 범례·개수 ──
        _view = new LinePreview(source) { Width = 2 * 360 + LinePreview.Gap, Height = 380 };
        _view.StrokeClicked += ToggleStroke;
        _view.ColorPicked += AddColor;
        _count = Ui.Label(UiStrings.LineArt_Working);
        _count.FontWeight = FontWeight.SemiBold;
        var legend = Ui.Row(
            LegendLine(LinePreview.StraightColor, dashed: false), Dim(Ui.Label(UiStrings.LineArt_LegendStraight)),
            LegendLine(LinePreview.ArcColor, dashed: false), Dim(Ui.Label(UiStrings.LineArt_LegendArc)),
            LegendDot(), Dim(Ui.Label(UiStrings.LineArt_LegendCorner)),
            LegendLine(LinePreview.StraightColor, dashed: true), Dim(Ui.Label(UiStrings.LineArt_LegendExcluded)),
            Ui.InfoBadge(UiStrings.LineArt_ViewTip));
        legend.Spacing = 4;
        var left = new StackPanel { Spacing = 4, Children = { new Border { BorderBrush = AppTheme.BorderBrush, BorderThickness = new Thickness(1), Child = _view }, legend, _count } };
        if (area is not null) left.Children.Add(Dim(Ui.Label(UiStrings.LineArt_SelectionOnly)));

        // ── 오른쪽: 설정 ──
        var detect = new StackPanel { Spacing = 2 };
        detect.Children.Add(Ui.Row(_autoDark, Ui.Label(UiStrings.LineArt_Darkness), _darkness, Ui.InfoBadge(UiStrings.LineArt_AutoDarkTip)));
        detect.Children.Add(Ui.Row(Ui.Label(UiStrings.LineArt_Colors), _picker, Ui.InfoBadge(UiStrings.LineArt_ColorsTip)));
        detect.Children.Add(_chips);
        detect.Children.Add(SettingRow(UiStrings.LineArt_ColorTolerance, _colorTolerance, UiStrings.LineArt_ColorToleranceTip));
        detect.Children.Add(SettingRow(UiStrings.LineArt_MaxWidth, _maxWidth, UiStrings.LineArt_MaxWidthTip));
        detect.Children.Add(SettingRow(UiStrings.LineArt_MinLength, _minLength, UiStrings.LineArt_MinLengthTip));
        var shape = new StackPanel { Spacing = 2 };
        shape.Children.Add(SettingRow(UiStrings.LineArt_Tolerance, _tolerance, UiStrings.LineArt_ToleranceTip));
        shape.Children.Add(SettingRow(UiStrings.LineArt_CornerAngle, _cornerAngle, UiStrings.LineArt_CornerAngleTip));
        var color = Ui.Row(_colorMode, Ui.Label(UiStrings.LineArt_Darken), _darken, Ui.InfoBadge(UiStrings.LineArt_ColorModeTip));
        var settings = new StackPanel
        {
            Width = 360,
            Spacing = 4,
            Children =
            {
                Ui.Group(UiStrings.LineArt_DetectGroup, detect),
                Ui.Group(UiStrings.LineArt_ShapeGroup, shape),
                Ui.Group(UiStrings.LineArt_ColorModeGroup, color),
            },
        };
        Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { left, settings } });

        AddFooter(_allFrames, OkButton);
        var reset = Ui.Button(UiStrings.LineArt_Reset, (_, _) => { Load(new LineArtOptions()); Changed(); });
        AddFooterLeft(reset);

        _debounce = new UiDebouncer(TimeSpan.FromMilliseconds(150), Start);
        Load(_last);
        _allFrames.IsChecked = _lastAllFrames && frames > 1;
        foreach (var n in new[] { _darkness, _colorTolerance, _maxWidth, _minLength, _tolerance, _cornerAngle, _darken })
            n.ValueChanged += (_, _) => Changed();
        _autoDark.IsCheckedChanged += (_, _) => Changed();
        _colorMode.SelectionChanged += (_, _) => Changed();
        _picker.IsCheckedChanged += (_, _) => _view.Picking = _picker.On();
        Closed += (_, _) =>
        {
            _debounce.Cancel();
            _cts?.Cancel();
        };
        Start();
    }

    /// <summary>고른 설정.</summary>
    public LineArtOptions Options => new()
    {
        AutoDark = _autoDark.On(),
        Darkness = (double)_darkness.NumValue() / 100,
        Colors = _colors,
        ColorTolerance = (double)_colorTolerance.NumValue() / 100,
        MaxWidth = (double)_maxWidth.NumValue(),
        MinLength = (int)_minLength.NumValue(),
        Tolerance = (double)_tolerance.NumValue(),
        CornerAngle = (double)_cornerAngle.NumValue(),
        ColorMode = _colorMode.SelectedIndex == 1 ? LineColorMode.Darken : LineColorMode.Keep,
        Darken = (double)_darken.NumValue() / 100,
    };

    /// <summary>이 레이어의 모든 프레임에 적용할지.</summary>
    public bool AllFrames => _allFrames.IsVisible && _allFrames.On();

    /// <summary>
    /// 선 정리를 쓸 수 없는 까닭(쓸 수 있으면 null): 그룹·타일맵·잠긴 레이어, 또는 지금 프레임 셀에 그림이 없음.
    /// 메뉴 항목을 끄고 풍선으로 알리는 데와 <see cref="Run"/>이 같이 쓴다.
    /// </summary>
    public static string? Blocker(EditorContext ctx)
    {
        if (ctx.Project is null) return null;
        if (!ctx.CanEdit) return UiStrings.LineArt_CannotEdit;
        if (ctx.EditImage is not { } img || img.IsRegionEmpty(img.Bounds)) return UiStrings.LineArt_Empty;
        return null;
    }

    /// <summary>창을 띄우고 고른 대로 정리한다. 상태 표시줄에 보일 문구를 돌려준다(취소했거나 프로젝트가 없으면 null).</summary>
    public static string? Run(EditorContext ctx, Visual owner)
    {
        if (ctx.Project is not { } p) return null;
        ctx.CommitFloating();
        if (Blocker(ctx) is { } why) return why;
        var layer = ctx.CurrentLayerObject!;
        var cel = ctx.EditImage!;
        var area = SelectionArea(ctx, cel.Width, cel.Height);
        int frames = p.Frames.Count(f => p.GetCel(layer, f) is { } c && !c.IsRegionEmpty(c.Bounds));
        var dlg = new LineArtDialog(cel.Clone(), area, frames);
        if (dlg.ShowDialog(owner) != DialogResult.OK || ctx.Project != p) return null;
        _last = dlg.Options;
        _lastAllFrames = dlg._allFrames.On();
        var result = dlg.FinalResult(owner);

        if (!dlg.AllFrames)
        {
            int changed = CountChanged(cel, result);
            if (changed == 0) return UiStrings.LineArt_NoChange;
            ctx.BeginStroke();
            ctx.EditImage!.CopyFrom(result);
            ctx.NotifyPixels();
            ctx.CommitStroke(Label);
            return UiStrings.FormatLineArt_Done(pixels: changed);
        }

        // 다른 프레임(부위면 모든 스킨의 셀)은 같은 설정으로 새로 가른다 — 뺀 획은 지금 프레임의 획이라 다른 프레임에는 없다.
        var o = _last;
        var others = p.StoredCels.Where(c => ReferenceEquals(c.Layer, layer) && !ReferenceEquals(c.Cel, cel))
            .Select(c => c.Cel).Distinct<PixelImage>(ReferenceEqualityComparer.Instance).ToList();
        var cleaned = WorkOverlay.Run(owner, UiStrings.LineArt_Busy, progress =>
        {
            var images = new PixelImage?[others.Count];
            using var stage = progress.Begin(others.Count);
            Parallel.For(0, others.Count, i =>
            {
                var image = others[i];
                var cellArea = area is not null && area.Length == image.Width * image.Height ? area : null;
                var r = LineArt.Apply(image, LineArt.Trace(image, o, cellArea), o);
                images[i] = r.ContentEquals(image) ? null : r;
                stage.Step(1);
            });
            return images;
        });
        var mapped = new Dictionary<PixelImage, PixelImage?>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < others.Count; i++) mapped[others[i]] = cleaned[i];
        mapped[cel] = result.ContentEquals(cel) ? null : result;
        int cels = 0;
        ctx.Structural(() => cels = p.MapCels(Label, (l, _) => ReferenceEquals(l, layer), c => mapped.GetValueOrDefault(c)));
        return cels > 0 ? UiStrings.FormatLineArt_DoneFrames(cels: cels) : UiStrings.LineArt_NoChange;
    }

    /// <summary>선택 영역(셀 좌표)을 픽셀마다의 참·거짓으로(선택이 없거나 셀 안에 든 칸이 없으면 null).</summary>
    private static bool[]? SelectionArea(EditorContext ctx, int width, int height)
    {
        if (!ctx.Selection.HasSelection) return null;
        var region = ctx.Selection.Region;
        var area = new bool[width * height];
        bool any = false;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (region.Contains(x, y)) any = area[y * width + x] = true;
        return any ? area : null;
    }

    /// <summary>적용할 결과: 지금 설정·뺀 획으로 끝난 계산이 있으면 그것, 아니면(계산 중이었으면) 그 자리에서 계산한다.</summary>
    private PixelImage FinalResult(Visual owner)
    {
        var o = Options;
        if (!_busy && !_debounce.IsPending && _state is { } s && s.Options == o && s.Excluded.SetEquals(ExcludedIds(s.Trace, _excludedPixels)))
            return s.Result;
        var pixels = new HashSet<int>(_excludedPixels);
        var reuse = _state;
        return WorkOverlay.Run(owner, UiStrings.LineArt_Busy, _ => Compute(_source, _area, o, pixels, reuse, CancellationToken.None).Result);
    }

    private void Load(LineArtOptions o)
    {
        _loading = true;
        _autoDark.IsChecked = o.AutoDark;
        SetNum(_darkness, o.Darkness * 100);
        _colors = [.. o.Colors];
        SetNum(_colorTolerance, o.ColorTolerance * 100);
        SetNum(_maxWidth, o.MaxWidth);
        SetNum(_minLength, o.MinLength);
        SetNum(_tolerance, o.Tolerance);
        SetNum(_cornerAngle, o.CornerAngle);
        _colorMode.SelectedIndex = o.ColorMode == LineColorMode.Darken ? 1 : 0;
        SetNum(_darken, o.Darken * 100);
        _loading = false;
        FillChips();
        UpdateEnabled();
    }

    private static void SetNum(NumericUpDown n, double value) => n.Value = Math.Clamp((decimal)Math.Round(value, 2), n.Minimum, n.Maximum);

    private void UpdateEnabled()
    {
        _darkness.IsEnabled = _autoDark.On();
        _darken.IsEnabled = _colorMode.SelectedIndex == 1;
    }

    /// <summary>설정이 바뀜: 잠시 뒤 다시 계산한다(값을 한꺼번에 바꾸는 중이면 건너뜀).</summary>
    private void Changed()
    {
        if (_loading) return;
        UpdateEnabled();
        _busy = true;
        UpdateCount();
        _debounce.Restart();
    }

    /// <summary>지금 설정·뺀 획으로 다른 스레드에서 다시 계산한다(앞서 하던 계산은 멈추고 결과를 버림). 가르기 설정이 그대로면 갈라 둔 획을 다시 쓴다.</summary>
    private void Start()
    {
        _debounce.Cancel();
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var o = Options;
        var pixels = new HashSet<int>(_excludedPixels);
        var reuse = _state;
        var source = _source;
        var area = _area;
        _busy = true;
        UpdateCount();
        Task.Run(() =>
        {
            try
            {
                var c = Compute(source, area, o, pixels, reuse, cts.Token);
                Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _busy = false;
                    _state = c;
                    _view.Show(c);
                    UpdateCount();
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _busy = false;
                    _count.Text = UiStrings.FormatLineArt_Error(message: ex.Message);
                });
            }
        });
    }

    private void UpdateCount()
    {
        if (_state is not { } s)
        {
            _count.Text = UiStrings.LineArt_Working;
            return;
        }
        int strokes = s.Trace.Strokes.Count, excluded = _view.Excluded.Count;
        string text = excluded > 0
            ? UiStrings.FormatLineArt_CountExcluded(strokes: strokes, excluded: excluded, pixels: s.Changed)
            : UiStrings.FormatLineArt_Count(strokes: strokes, pixels: s.Changed);
        _count.Text = _busy ? text + " · " + UiStrings.LineArt_Working : text;
    }

    /// <summary>획 하나를 정리에서 빼거나 다시 넣는다(뺀 모양은 바로, 결과는 다시 계산해서).</summary>
    private void ToggleStroke(int id)
    {
        if (_state is not { } s || s.Trace.Strokes.FirstOrDefault(t => t.Id == id) is not { } stroke) return;
        int w = s.Trace.Width;
        bool exclude = !_view.Excluded.Contains(id);
        foreach (var p in stroke.Pixels)
            if (exclude) _excludedPixels.Add(p.Y * w + p.X);
            else _excludedPixels.Remove(p.Y * w + p.X);
        _view.SetExcluded(ExcludedIds(s.Trace, _excludedPixels));
        Start();
    }

    /// <summary>스포이트로 누른 색을 선 색에 더한다(이미 있으면 그대로). 한 번 고르면 스포이트를 끈다.</summary>
    private void AddColor(Color32 c)
    {
        _picker.IsChecked = false;
        if (_colors.Contains(c)) return;
        _colors = [.. _colors, c];
        FillChips();
        Changed();
    }

    private void RemoveColor(Color32 c)
    {
        _colors = [.. _colors.Where(x => x != c)];
        FillChips();
        Changed();
    }

    /// <summary>선 색 칩(색 칸 + 빼기 단추)을 다시 채운다.</summary>
    private void FillChips()
    {
        _chips.Children.Clear();
        if (_colors.Count == 0)
        {
            _chips.Children.Add(Dim(Ui.Label(UiStrings.LineArt_NoColors)));
            return;
        }
        foreach (var c in _colors)
        {
            var color = c;
            var swatch = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(2),
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(color.ToColor()),
                VerticalAlignment = VerticalAlignment.Center,
            }.Tip(color.ToHex());
            var remove = Ui.IconButton(VectorIcon.X, UiStrings.LineArt_RemoveColor, (_, _) => RemoveColor(color), size: 10);
            remove.Padding = new Thickness(3, 1);
            remove.Margin = new Thickness(1, 0, 0, 0);
            _chips.Children.Add(new Border
            {
                BorderBrush = AppTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 1, 1, 1),
                Margin = new Thickness(0, 0, 4, 4),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { swatch, remove } },
            });
        }
    }

    private static StackPanel SettingRow(string label, NumericUpDown number, string tip)
    {
        var l = Ui.Label(label);
        l.Width = 165;
        return Ui.Row(l, number, Ui.InfoBadge(tip));
    }

    private static TextBlock Dim(TextBlock label)
    {
        label.Foreground = AppTheme.DimTextBrush;
        return label;
    }

    /// <summary>범례의 선 조각(점선이면 뺀 획 모양).</summary>
    private static Control LegendLine(Color color, bool dashed) => new Avalonia.Controls.Shapes.Line
    {
        StartPoint = new Avalonia.Point(0, 6),
        EndPoint = new Avalonia.Point(18, 6),
        Stroke = new SolidColorBrush(dashed ? Color.FromArgb(LinePreview.ExcludedAlpha, color.R, color.G, color.B) : color),
        StrokeThickness = 2,
        StrokeDashArray = dashed ? [2, 1.5] : null,
        Width = 18,
        Height = 12,
        Margin = new Thickness(6, 0, 0, 0),
    };

    private static Control LegendDot() => new Avalonia.Controls.Shapes.Ellipse
    {
        Width = 7,
        Height = 7,
        Fill = new SolidColorBrush(LinePreview.CornerColor),
        Stroke = new SolidColorBrush(LinePreview.HaloColor),
        StrokeThickness = 1,
        Margin = new Thickness(6, 0, 0, 0),
    };

    // ───────────── 계산(작업 스레드) ─────────────

    /// <summary>한 번 계산한 것: 설정, 가른 획, 획마다 그릴 모양, 픽셀 → 획 번호, 뺀 획, 결과 그림, 바뀐 픽셀 수.</summary>
    private sealed record Computed(LineArtOptions Options, LineArtTrace Trace, StrokeView[] Views, int[] Owner, HashSet<int> Excluded, PixelImage Result, int Changed);

    /// <summary>획 하나의 그릴 모양(칸 좌표): 조각마다 따라 찍은 점, 모서리 자리, 옛 선 픽셀. 화면 도형은 UI 스레드에서 처음 그릴 때 만든다.</summary>
    private sealed class StrokeView(int id, IReadOnlyList<(LineShapeKind Kind, List<(double X, double Y)> Points)> pieces, IReadOnlyList<(double X, double Y)> corners, IReadOnlyList<Point> pixels)
    {
        public int Id { get; } = id;
        public IReadOnlyList<(LineShapeKind Kind, List<(double X, double Y)> Points)> Pieces { get; } = pieces;
        public IReadOnlyList<(double X, double Y)> Corners { get; } = corners;
        public IReadOnlyList<Point> Pixels { get; } = pixels;
        /// <summary>직선·원호 조각의 화면 도형(그 종류가 없으면 null) — <see cref="Built"/> 뒤에만 뜻이 있다.</summary>
        public Geometry? Straight, Arc;
        public bool Built;
    }

    /// <summary>
    /// 가르고(<paramref name="reuse"/>의 가르기 설정이 같으면 그 획을 다시 씀) 뺀 픽셀 자리로 뺄 획을 정한 뒤 적용한다.
    /// </summary>
    private static Computed Compute(PixelImage source, bool[]? area, LineArtOptions o, HashSet<int> excludedPixels, Computed? reuse, CancellationToken ct)
    {
        LineArtTrace trace;
        StrokeView[] views;
        int[] owner;
        // 선 색 방식은 가르기에 쓰이지 않으므로 그것만 바뀌었으면 갈라 둔 것을 그대로 쓴다.
        if (reuse is { } r && r.Options with { ColorMode = o.ColorMode, Darken = o.Darken } == o)
            (trace, views, owner) = (r.Trace, r.Views, r.Owner);
        else
        {
            trace = LineArt.Trace(source, o, area);
            ct.ThrowIfCancellationRequested();
            views = [.. trace.Strokes.Select(s => BuildView(s, o.CornerAngle))];
            owner = new int[trace.Width * trace.Height];
            Array.Fill(owner, -1);
            for (int i = 0; i < views.Length; i++)
                foreach (var p in views[i].Pixels)
                {
                    int k = p.Y * trace.Width + p.X;
                    if ((uint)p.X < (uint)trace.Width && (uint)p.Y < (uint)trace.Height && owner[k] < 0) owner[k] = i;
                }
        }
        ct.ThrowIfCancellationRequested();
        var excluded = ExcludedIds(trace, excludedPixels);
        var result = LineArt.Apply(source, trace, o, excluded.Count > 0 ? excluded : null);
        return new Computed(o, trace, views, owner, excluded, result, CountChanged(source, result));
    }

    /// <summary>
    /// 획의 그릴 모양. 모서리는 이어진 두 조각이 만나는 자리 중 이음에서 방향이 모서리 각도의 3/4 넘게 꺾이는 곳이다
    /// (맞춤이 모서리 각도 이상 꺾인 곳에서 조각을 나누므로 — 직선과 원호가 매끄럽게 이어지는 자리는 빠짐, 따라 찍은 점으로 잰 방향이라 조금 넉넉히).
    /// </summary>
    private static StrokeView BuildView(LineStroke s, double cornerAngle)
    {
        var pieces = s.Shapes.Select(shape => (shape.Kind, shape.Sample(0.2))).ToList();
        var corners = new List<(double X, double Y)>();
        double limit = cornerAngle * 0.75 * Math.PI / 180;
        int joints = s.Closed ? pieces.Count : pieces.Count - 1;
        for (int k = 0; k < joints && pieces.Count > 1; k++)
        {
            var a = pieces[k].Item2;
            var b = pieces[(k + 1) % pieces.Count].Item2;
            if (a.Count < 2 || b.Count < 2) continue;
            double ax = a[^1].X - a[^2].X, ay = a[^1].Y - a[^2].Y, bx = b[1].X - b[0].X, by = b[1].Y - b[0].Y;
            double turn = Math.Abs(Math.Atan2(ax * by - ay * bx, ax * bx + ay * by));
            if (turn > limit) corners.Add(a[^1]);
        }
        return new StrokeView(s.Id, pieces, corners, s.Pixels);
    }

    /// <summary>옛 선 픽셀의 반 넘게가 뺀 자리에 든 획.</summary>
    private static HashSet<int> ExcludedIds(LineArtTrace trace, HashSet<int> pixels)
    {
        var ids = new HashSet<int>();
        if (pixels.Count == 0) return ids;
        foreach (var s in trace.Strokes)
        {
            int hit = 0;
            foreach (var p in s.Pixels)
                if (pixels.Contains(p.Y * trace.Width + p.X)) hit++;
            if (hit * 2 > s.Pixels.Count) ids.Add(s.Id);
        }
        return ids;
    }

    private static int CountChanged(PixelImage a, PixelImage b)
    {
        int n = 0;
        var pa = a.Pixels;
        var pb = b.Pixels;
        for (int i = 0; i < pa.Length; i++)
            if (pa[i] != pb[i]) n++;
        return n;
    }

    // ───────────── 미리보기 ─────────────

    /// <summary>
    /// 지금 그림(왼쪽)과 정리한 그림(오른쪽)을 같은 배율·자리로 나란히 그리고, 왼쪽에 찾은 획을 겹친다.
    /// 휠 = 확대(포인터 자리 기준), 끌기 = 옮기기, 두 번 누르기 = 맞추기. 획을 누르면 <see cref="StrokeClicked"/>, 스포이트 중이면 누른 픽셀 색으로 <see cref="ColorPicked"/>.
    /// </summary>
    private sealed class LinePreview : Control
    {
        public const double Gap = 8;
        private const double CaptionHeight = 20;
        public static readonly Color StraightColor = Color.FromRgb(70, 200, 255);
        public static readonly Color ArcColor = Color.FromRgb(255, 110, 200);
        public static readonly Color CornerColor = Color.FromRgb(255, 214, 64);
        public static readonly Color HaloColor = Color.FromArgb(170, 0, 0, 0);
        public const byte ExcludedAlpha = 120;
        private static readonly IBrush HoverFill = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        private static readonly IBrush CornerBrush = new SolidColorBrush(CornerColor);
        private static readonly IBrush HaloBrush = new SolidColorBrush(HaloColor);

        private readonly PixelImage _before;
        private readonly WriteableBitmap _beforeBitmap;
        private PixelImage? _after;
        private WriteableBitmap? _afterBitmap;
        private StrokeView[] _strokes = [];
        private int[] _owner = [];
        private HashSet<int> _excluded = [];
        private int _hover = -1;
        private double _zoom = 1;
        private Vector _offset;
        private bool _fitted;
        private Avalonia.Point? _drag, _click;
        private bool _picking;

        public LinePreview(PixelImage before)
        {
            _before = before;
            _beforeBitmap = ImageUtil.ToBitmap(before);
            ClipToBounds = true;
        }

        public event Action<int>? StrokeClicked;
        public event Action<Color32>? ColorPicked;

        /// <summary>지금 뺀 획 번호.</summary>
        public IReadOnlySet<int> Excluded => _excluded;

        /// <summary>스포이트 중(누르면 색을 고르고 획은 건드리지 않음).</summary>
        public bool Picking
        {
            get => _picking;
            set
            {
                _picking = value;
                Cursor = value ? PixelCanvas.PickerCursor.Value : null;
                InvalidateVisual();
            }
        }

        public void Show(Computed c)
        {
            _after = c.Result;
            _afterBitmap?.Dispose();
            _afterBitmap = ImageUtil.ToBitmap(c.Result);
            if (!ReferenceEquals(_strokes, c.Views)) _hover = -1;
            _strokes = c.Views;
            _owner = c.Owner;
            _excluded = c.Excluded;
            InvalidateVisual();
        }

        public void SetExcluded(HashSet<int> ids)
        {
            _excluded = ids;
            InvalidateVisual();
        }

        private Rect LeftPane => new(0, CaptionHeight, Math.Max(0, (Bounds.Width - Gap) / 2), Math.Max(0, Bounds.Height - CaptionHeight));
        private Rect RightPane => new(LeftPane.Right + Gap, CaptionHeight, LeftPane.Width, LeftPane.Height);

        private void Fit()
        {
            var pane = LeftPane;
            if (pane.Width <= 0 || pane.Height <= 0) return;
            double fit = Math.Min((pane.Width - 8) / _before.Width, (pane.Height - 8) / _before.Height);
            _zoom = fit >= 1 ? Math.Floor(fit) : Math.Max(0.05, fit);
            _offset = new Vector(Math.Round((pane.Width - _before.Width * _zoom) / 2), Math.Round((pane.Height - _before.Height * _zoom) / 2));
            _fitted = true;
        }

        /// <summary>포인터가 있는 칸과 그 안 좌표(두 칸이 같은 배치).</summary>
        private (bool Right, Avalonia.Point Local) PaneLocal(Avalonia.Point p)
        {
            bool right = p.X >= RightPane.X;
            var pane = right ? RightPane : LeftPane;
            return (right, new Avalonia.Point(p.X - pane.X, p.Y - pane.Y));
        }

        /// <summary>칸 안 좌표 → 그림 좌표(칸 단위 실수).</summary>
        private (double X, double Y) ToImage(Avalonia.Point local) => ((local.X - _offset.X) / _zoom, (local.Y - _offset.Y) / _zoom);

        /// <summary>왼쪽 칸의 점이 가리키는 획(배열 번호, 없으면 -1): 그 픽셀이 옛 선 픽셀인 획, 아니면 화면에서 5px 안에 지나가는 획.</summary>
        private int StrokeAt(Avalonia.Point p)
        {
            if (!LeftPane.Contains(p) || _strokes.Length == 0) return -1;
            var (x, y) = ToImage(PaneLocal(p).Local);
            int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
            if (_before.Contains(cx, cy) && _owner.Length == _before.Width * _before.Height && _owner[cy * _before.Width + cx] is var o and >= 0) return o;
            double best = 5 / _zoom;
            int found = -1;
            for (int i = 0; i < _strokes.Length; i++)
                foreach (var (_, pts) in _strokes[i].Pieces)
                    for (int k = 1; k < pts.Count; k++)
                    {
                        double d = SegmentDistance(x, y, pts[k - 1], pts[k]);
                        if (d < best)
                        {
                            best = d;
                            found = i;
                        }
                    }
            return found;
        }

        private static double SegmentDistance(double px, double py, (double X, double Y) a, (double X, double Y) b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = len <= 0 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / len, 0, 1);
            double ex = a.X + t * dx - px, ey = a.Y + t * dy - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            var (_, p) = PaneLocal(e.GetPosition(this));
            double z = ImageUtil.ZoomWheelStep(_zoom, e.Delta.Y > 0, 64);
            e.Handled = true;
            if (z == _zoom) return;
            _offset = new Vector(p.X - (p.X - _offset.X) * z / _zoom, p.Y - (p.Y - _offset.Y) * z / _zoom);
            _zoom = z;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            e.Handled = true;
            var point = e.GetCurrentPoint(this);
            if (e.ClickCount == 2)
            {
                Fit();
                InvalidateVisual();
                return;
            }
            if (!point.Properties.IsLeftButtonPressed) return;
            _drag = _click = point.Position;
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag is { } start)
            {
                _offset += p - start;
                _drag = p;
                InvalidateVisual();
                return;
            }
            int hover = _picking ? -1 : StrokeAt(p);
            if (!_picking) Cursor = hover >= 0 ? new Cursor(StandardCursorType.Hand) : null;
            if (hover == _hover) return;
            _hover = hover;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            if (_hover < 0) return;
            _hover = -1;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            var p = e.GetPosition(this);
            var start = _click;
            _drag = _click = null;
            e.Pointer.Capture(null);
            if (start is not { } s || Math.Abs(p.X - s.X) > 4 || Math.Abs(p.Y - s.Y) > 4) return;
            if (_picking)
            {
                if (!LeftPane.Contains(s) && !RightPane.Contains(s)) return;
                var (right, local) = PaneLocal(s);
                var (x, y) = ToImage(local);
                var image = right ? _after ?? _before : _before;
                var c = image.Get((int)Math.Floor(x), (int)Math.Floor(y));
                if (!c.IsTransparent) ColorPicked?.Invoke(c);
                return;
            }
            if (StrokeAt(s) is var i and >= 0) StrokeClicked?.Invoke(_strokes[i].Id);
        }

        public override void Render(DrawingContext g)
        {
            var bounds = new Rect(Bounds.Size);
            g.FillRectangle(AppTheme.BackBrush, bounds);
            if (!_fitted) Fit();
            var left = LeftPane;
            var right = RightPane;
            string zoom = "  ×" + _zoom.ToString("0.##", CultureInfo.CurrentCulture);
            AppTheme.DrawTextIn(g, UiStrings.LineArt_Before + zoom, new Rect(4, 0, left.Width - 8, 18), AppTheme.TextBrush, AppTheme.SmallFontSize, bold: true);
            AppTheme.DrawTextIn(g, UiStrings.LineArt_After, new Rect(right.X + 4, 0, right.Width - 8, 18), AppTheme.TextBrush, AppTheme.SmallFontSize, bold: true);
            DrawPane(g, left, _beforeBitmap, overlay: true);
            DrawPane(g, right, _afterBitmap ?? _beforeBitmap, overlay: false);
        }

        private void DrawPane(DrawingContext g, Rect pane, Bitmap bmp, bool overlay)
        {
            var dest = new Rect(pane.X + _offset.X, pane.Y + _offset.Y, _before.Width * _zoom, _before.Height * _zoom);
            using (g.PushClip(pane))
            {
                if (dest.Intersects(pane)) AppTheme.DrawChecker(g, dest.Intersect(pane), 8);
                using (ImageUtil.Pixelated(g))
                    g.DrawImage(bmp, new Rect(0, 0, _before.Width, _before.Height), dest);
                using (g.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(dest.X, dest.Y)))
                {
                    if (overlay)
                        for (int i = 0; i < _strokes.Length; i++)
                            if (i != _hover) DrawStroke(g, _strokes[i], hover: false);
                    // 올린 획은 두 칸 모두에 밝게(정리 전·후를 견주게), 지금 그림에서는 옛 선 픽셀도 밝힌다.
                    if (_hover >= 0 && _hover < _strokes.Length)
                    {
                        if (overlay)
                            foreach (var p in _strokes[_hover].Pixels)
                                g.FillRectangle(HoverFill, new Rect(p.X, p.Y, 1, 1));
                        DrawStroke(g, _strokes[_hover], hover: true);
                    }
                }
            }
        }

        private void DrawStroke(DrawingContext g, StrokeView s, bool hover)
        {
            if (!s.Built)
            {
                s.Straight = BuildGeometry(s, LineShapeKind.Straight);
                s.Arc = BuildGeometry(s, LineShapeKind.Arc);
                s.Built = true;
            }
            bool excluded = _excluded.Contains(s.Id);
            double px = 1 / _zoom;
            double width = (hover ? 2.6 : 1.6) * px;
            var dash = excluded ? new DashStyle([2, 1.5], 0) : null;
            byte alpha = excluded && !hover ? ExcludedAlpha : (byte)255;
            var halo = new Pen(HaloBrush, width + 2 * px, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            foreach (var (geometry, color) in new[] { (s.Straight, StraightColor), (s.Arc, ArcColor) })
            {
                if (geometry is null) continue;
                if (!excluded) g.DrawGeometry(null, halo, geometry);
                g.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), width, dash, PenLineCap.Round, PenLineJoin.Round), geometry);
            }
            if (excluded) return;
            double r = (hover ? 3.2 : 2.4) * px;
            var border = new Pen(HaloBrush, px);
            foreach (var c in s.Corners)
                g.DrawEllipse(CornerBrush, border, new Avalonia.Point(c.X, c.Y), r, r);
        }

        /// <summary>한 종류(직선·원호) 조각을 모은 도형(칸 좌표, 없으면 빈 도형이 아닌 null).</summary>
        private static Geometry? BuildGeometry(StrokeView s, LineShapeKind kind)
        {
            var pieces = s.Pieces.Where(p => p.Kind == kind && p.Points.Count > 1).ToList();
            if (pieces.Count == 0) return null;
            var geometry = new StreamGeometry();
            using var ctx = geometry.Open();
            foreach (var (_, pts) in pieces)
            {
                ctx.BeginFigure(new Avalonia.Point(pts[0].X, pts[0].Y), false);
                for (int i = 1; i < pts.Count; i++) ctx.LineTo(new Avalonia.Point(pts[i].X, pts[i].Y));
                ctx.EndFigure(false);
            }
            return geometry;
        }
    }
}
