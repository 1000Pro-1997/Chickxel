using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

/// <summary>
/// 그룹 자동 배치 설정: 방향(가로·세로·격자 + 열 수)·맞춤·배치·간격·여백·순서·숨긴 부위·영역. 아래 미리 보기에 지금 프레임 부위 그림을 놓아 보인다
/// (프로젝트는 바꾸지 않음). 적용 = OK(<see cref="Layout"/>), 이미 켜져 있으면 "자동 배치 끄기" = No.
/// </summary>
public sealed class GroupLayoutDialog : DialogBase
{
    private readonly TabStrip _direction = new(TabStripKind.Segmented, UiStrings.S_GroupLayoutDialog__direction_6B1CAE4C99, UiStrings.S_GroupLayoutDialog__direction_E3EF05F240, UiStrings.S_GroupLayoutDialog__direction_9FCD02FD15);
    private readonly NumericUpDown _columns;
    private readonly StackPanel _columnsRow;
    private readonly TextBlock _alignLabel = FieldLabel("");
    private readonly TabStrip _align = new(TabStripKind.Segmented, UiStrings.S_GroupLayoutDialog__align_485FD29A3F, UiStrings.S_GroupLayoutDialog__align_5ABA2EF80F, UiStrings.S_GroupLayoutDialog__align_2EA62D4A63);
    private readonly TextBlock _distLabel = FieldLabel("");
    private readonly TabStrip _distribution = new(TabStripKind.Segmented, UiStrings.S_GroupLayoutDialog__distribution_B9E0583737, UiStrings.S_GroupLayoutDialog__distribution_B09A8FB8A9, UiStrings.S_GroupLayoutDialog__distribution_8B75602905, UiStrings.S_GroupLayoutDialog__distribution_3B6B9AD168);
    private readonly NumericUpDown _gap, _margin;
    private readonly TabStrip _order = new(TabStripKind.Segmented, UiStrings.S_GroupLayoutDialog__order_76C685F2DF, UiStrings.S_GroupLayoutDialog__order_BBF74458D7);
    private readonly CheckBox _includeHidden;
    private readonly CheckBox _wholeCanvas;
    private readonly NumericUpDown _ax, _ay, _aw, _ah;
    private readonly GroupLayoutPreview _preview;

    public GroupLayoutDialog(SpriteProject project, Layer group, int frameIndex)
        : base(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_330CFAC850,group.Name), UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_11FBBFBF67)
    {
        var current = group.AutoLayout;
        var l = current ?? new GroupLayout();
        _direction.SelectedIndex = (int)l.Direction;
        _columns = Ui.Num(1, GroupLayout.MaxColumns, l.Columns, 56).Presets(1, 2, 3, 4, 5, 6, 8, 10, 12, 16).Unit(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_4B983CA7BF).RecentKey("layout.columns");
        _align.SelectedIndex = (int)l.Align;
        _distribution.SelectedIndex = (int)l.Distribution;
        _distribution.SetTip(3, UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_29C26D42F5);
        _gap = Ui.Num(0, GroupLayout.MaxSpacing, l.Gap, 56).Presets(Ui.Spacings).Unit("px").RecentKey("layout.gap");
        _margin = Ui.Num(0, GroupLayout.MaxSpacing, l.Margin, 56).Presets(Ui.Spacings).Unit("px").RecentKey("layout.margin");
        _order.SelectedIndex = (int)l.Order;
        _order.SetTip(0, UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_AE6AA2A01D).SetTip(1, UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_9566846ECA);
        _includeHidden = Ui.Check(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_6754C5A624, l.IncludeHidden)
            .Tip(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_FC0D1D2082);
        var area = l.Area ?? project.Bounds;
        _wholeCanvas = Ui.Check(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_2586C42060, l.Area is null);
        _ax = Ui.Num(-PixelImage.MaxSize, PixelImage.MaxSize, area.X, 56).Presets(Ui.Offsets).Unit("px").RecentKey("layout.area.pos");
        _ay = Ui.Num(-PixelImage.MaxSize, PixelImage.MaxSize, area.Y, 56).Presets(Ui.Offsets).Unit("px").RecentKey("layout.area.pos");
        _aw = Ui.Num(1, PixelImage.MaxSize, area.Width, 56).Presets(Ui.SpriteSizes).Unit("px").RecentKey("layout.area.size");
        _ah = Ui.Num(1, PixelImage.MaxSize, area.Height, 56).Presets(Ui.SpriteSizes).Unit("px").RecentKey("layout.area.size");
        _preview = new GroupLayoutPreview(project, group, frameIndex) { Width = 340, Height = 190 };

        _columnsRow = Ui.Row(Ui.Label(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_4B983CA7BF), _columns);
        int parts = project.ChildCount(group);
        Body.Children.Add(Note(string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_6446F8431F,parts)));
        Body.Children.Add(Ui.Row(FieldLabel(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_9331E25300), _direction, _columnsRow));
        Body.Children.Add(Ui.Row(_alignLabel, _align));
        Body.Children.Add(Ui.Row(_distLabel, _distribution));
        Body.Children.Add(Ui.Row(FieldLabel(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_3C32EBCE78), _gap, Ui.Label("px"), Ui.Label(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_BE647B487C), _margin, Ui.Label("px")));
        Body.Children.Add(Ui.Row(FieldLabel(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_915D00697D), _order));
        Body.Children.Add(Ui.Row(FieldLabel(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_639D409AD1), _wholeCanvas, Ui.Label("X"), _ax, Ui.Label("Y"), _ay, Ui.Label("W"), _aw, Ui.Label("H"), _ah));
        Body.Children.Add(Ui.Row(FieldLabel(""), _includeHidden));
        Body.Children.Add(new Border
        {
            Child = _preview,
            BorderBrush = AppTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(3, 6, 3, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        Body.Children.Add(Note(current is null
            ? UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_878F07851A
            : UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_FA3F69CD5B));
        if (current is not null)
        {
            var off = AddButton(UiStrings.S_GroupLayoutDialog_GroupLayoutDialog_27D562E065, DialogResult.No);
            off.Width = 120;
        }

        _direction.SelectionChanged += (_, _) => Refresh();
        _align.SelectionChanged += (_, _) => Refresh();
        _distribution.SelectionChanged += (_, _) => Refresh();
        _order.SelectionChanged += (_, _) => Refresh();
        foreach (var n in new[] { _columns, _gap, _margin, _ax, _ay, _aw, _ah }) n.ValueChanged += (_, _) => Refresh();
        _includeHidden.IsCheckedChanged += (_, _) => Refresh();
        _wholeCanvas.IsCheckedChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>고른 설정.</summary>
    public GroupLayout Layout => new GroupLayout
    {
        Direction = (GroupLayoutDirection)Math.Max(0, _direction.SelectedIndex),
        Columns = (int)_columns.NumValue(),
        Align = (LayoutAlign)Math.Max(0, _align.SelectedIndex),
        Distribution = (LayoutDistribution)Math.Max(0, _distribution.SelectedIndex),
        Gap = (int)_gap.NumValue(),
        Margin = (int)_margin.NumValue(),
        Order = (GroupLayoutOrder)Math.Max(0, _order.SelectedIndex),
        IncludeHidden = _includeHidden.On(),
        Area = _wholeCanvas.On() ? null : new Rectangle((int)_ax.NumValue(), (int)_ay.NumValue(), (int)_aw.NumValue(), (int)_ah.NumValue()),
    }.Normalized();

    private void Refresh()
    {
        var dir = (GroupLayoutDirection)Math.Max(0, _direction.SelectedIndex);
        _columnsRow.IsVisible = dir == GroupLayoutDirection.Grid;
        (_alignLabel.Text, _distLabel.Text) = dir switch
        {
            GroupLayoutDirection.Row => (UiStrings.S_GroupLayoutDialog_Refresh_79769D8A13, UiStrings.S_GroupLayoutDialog_Refresh_8D2CA7DA17),
            GroupLayoutDirection.Column => (UiStrings.S_GroupLayoutDialog_Refresh_432A5531EE, UiStrings.S_GroupLayoutDialog_Refresh_2B870264D5),
            _ => (UiStrings.S_GroupLayoutDialog_Refresh_576E2DD3BA, UiStrings.S_GroupLayoutDialog_Refresh_310506C3FB),
        };
        var (start, end) = dir switch
        {
            GroupLayoutDirection.Row => (UiStrings.S_GroupLayoutDialog_Refresh_2661884A8A, UiStrings.S_GroupLayoutDialog_Refresh_43F30CF9CE),
            GroupLayoutDirection.Column => (UiStrings.S_GroupLayoutDialog_Refresh_EBE829199A, UiStrings.S_GroupLayoutDialog_Refresh_D30DD7ACE5),
            _ => (UiStrings.S_GroupLayoutDialog_Refresh_B49F4A4010, UiStrings.S_GroupLayoutDialog_Refresh_2B96211826),
        };
        _align.SetTip(0, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_GroupLayoutDialog_Refresh_6AC90345A7,start)).SetTip(1, UiStrings.S_GroupLayoutDialog_Refresh_10B14D798B).SetTip(2, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_GroupLayoutDialog_Refresh_6AC90345A7,end));
        foreach (var n in new[] { _ax, _ay, _aw, _ah }) n.IsEnabled = !_wholeCanvas.On();
        _preview.Layout = Layout;
    }

    private static TextBlock FieldLabel(string text)
    {
        var t = Ui.Label(text);
        t.Width = 74;
        return t;
    }

    private static TextBlock Note(string text)
    {
        var t = Ui.Label(text);
        t.TextWrapping = TextWrapping.Wrap;
        t.MaxWidth = 520;
        t.HorizontalAlignment = HorizontalAlignment.Left;
        t.Foreground = AppTheme.DimTextBrush;
        return t;
    }

    /// <summary>
    /// 자동 배치 중인 그룹의 부위를 손으로 옮기기 전에 묻는다. 자동 배치가 아니면 바로 true, 확인하면 true(끄기는 부르는 쪽이 옮기기와 함께), 취소하면 false.
    /// </summary>
    public static bool ConfirmManualMove(Visual? owner, Layer part)
        => !part.IsAutoLaidOut
           || Ui.Ask(owner, UiStrings.S_GroupLayoutDialog_ConfirmManualMove_3813FDBEE5, string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_GroupLayoutDialog_ConfirmManualMove_B57E70811F,part.Parent!.Name),
               MessageButtons.OKCancel, MessageIcon.Question) == DialogResult.OK;
}

/// <summary>자동 배치 미리 보기: 캔버스(체커)·배치 영역·여백 안쪽 선과, 배치한 자리에 지금 프레임 부위 그림(순서 번호). 배치에서 빠진(숨긴) 부위는 제자리에 흐리게.</summary>
internal sealed class GroupLayoutPreview : Control
{
    private static readonly IPen AreaPen = AppTheme.Pen(AppTheme.AccentColor, 1, new Avalonia.Media.Immutable.ImmutableDashStyle([3, 3], 0));
    private static readonly IPen InnerPen = AppTheme.Pen(AppTheme.DimTextColor, 1, new Avalonia.Media.Immutable.ImmutableDashStyle([2, 2], 0));
    private static readonly IPen PartPen = AppTheme.Pen(AppTheme.AccentColor);
    private static readonly IPen CanvasPen = AppTheme.Pen(AppTheme.BorderColor);
    private static readonly IBrush EmptyPartBrush = AppTheme.Brush(Color.FromArgb(60, AppTheme.AccentColor.R, AppTheme.AccentColor.G, AppTheme.AccentColor.B));
    private static readonly IBrush BadgeBrush = AppTheme.Brush(Color.FromArgb(200, 20, 20, 24));

    private readonly SpriteProject _project;
    private readonly Layer _group;
    private readonly Dictionary<Layer, WriteableBitmap> _bitmaps = new();
    private GroupLayout _layout = new();

    public GroupLayoutPreview(SpriteProject project, Layer group, int frameIndex)
    {
        _project = project;
        _group = group;
        int f = Math.Clamp(frameIndex, 0, project.Frames.Count - 1);
        foreach (var part in project.ChildrenOf(group))
            if (project.GetCel(part, project.Frames[f]) is { } cel) _bitmaps[part] = ImageUtil.ToBitmap(cel);
    }

    public GroupLayout Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        foreach (var bmp in _bitmaps.Values) bmp.Dispose();
        _bitmaps.Clear();
    }

    public override void Render(DrawingContext g)
    {
        var view = new Rect(Bounds.Size);
        g.FillRectangle(AppTheme.BackBrush, view);
        var placed = _project.ComputeGroupLayout(_group, _layout);
        var order = new Dictionary<Layer, (Point Offset, int Number)>();
        for (int i = 0; i < placed.Count; i++) order[placed[i].Part] = (placed[i].Offset, i + 1);
        var parts = _project.ChildrenOf(_group); // 아래 → 위(그리는 순서)
        var canvas = _project.Bounds;
        var area = _layout.Area ?? canvas;
        var bounds = canvas;
        bounds = Rectangle.Union(bounds, area);
        foreach (var part in parts)
            bounds = Rectangle.Union(bounds, new Rectangle(order.TryGetValue(part, out var o) ? o.Offset : part.PartOffset, part.PartSize));
        const double pad = 8;
        double fit = Math.Min((view.Width - 2 * pad) / bounds.Width, (view.Height - 2 * pad) / bounds.Height);
        double scale = fit >= 1 ? Math.Floor(fit) : fit;
        double ox = Math.Floor((view.Width - bounds.Width * scale) / 2) - bounds.X * scale;
        double oy = Math.Floor((view.Height - bounds.Height * scale) / 2) - bounds.Y * scale;
        Rect ToView(Rectangle r) => new(ox + r.X * scale, oy + r.Y * scale, r.Width * scale, r.Height * scale);

        var canvasRect = ToView(canvas);
        using (g.PushClip(canvasRect)) AppTheme.DrawChecker(g, canvasRect, Math.Clamp(scale * 2, 4, 8));
        g.DrawRectangle(null, CanvasPen, canvasRect.Inflate(0.5));
        if (_layout.Area is not null) g.DrawRectangle(null, AreaPen, ToView(area).Deflate(0.5));
        if (_layout.Margin > 0)
        {
            int m = _layout.Margin;
            var inner = new Rectangle(area.X + m, area.Y + m, area.Width - 2 * m, area.Height - 2 * m);
            if (inner.Width > 0 && inner.Height > 0) g.DrawRectangle(null, InnerPen, ToView(inner).Deflate(0.5));
        }
        foreach (var part in parts)
        {
            bool isPlaced = order.TryGetValue(part, out var at);
            var r = ToView(new Rectangle(isPlaced ? at.Offset : part.PartOffset, part.PartSize));
            using (g.PushOpacity(isPlaced ? 1 : 0.35))
            {
                if (_bitmaps.TryGetValue(part, out var bmp))
                    using (ImageUtil.Pixelated(g))
                        g.DrawImage(bmp, new Rect(0, 0, part.PartSize.Width, part.PartSize.Height), r);
                else g.FillRectangle(EmptyPartBrush, r);
                g.DrawRectangle(null, PartPen, r.Deflate(0.5));
            }
        }
        // 순서 번호는 그림 위에 따로(겹친 부위에 가리지 않게).
        foreach (var (part, offset) in placed)
        {
            var r = ToView(new Rectangle(offset, part.PartSize));
            var text = order[part].Number.ToString();
            var size = AppTheme.MeasureText(text, AppTheme.SmallFontSize, bold: true);
            var badge = new Rect(r.X + 1, r.Y + 1, size.Width + 6, size.Height + 2);
            if (badge.Width > r.Width + 2 || badge.Height > r.Height + 2) continue;
            g.DrawRectangle(BadgeBrush, null, badge, 2, 2);
            AppTheme.DrawText(g, text, new Avalonia.Point(badge.X + 3, badge.Y + 1), AppTheme.TextBrush, AppTheme.SmallFontSize, bold: true);
        }
    }
}
