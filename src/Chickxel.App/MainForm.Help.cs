using Avalonia.VisualTree;
using Chickxel.App.Localization;

namespace Chickxel.App;

public sealed partial class MainForm
{
    /// <summary>대상 도움말 고르는 중 점 아래 대상을 두르는 노란 테두리(<see cref="_helpPicker"/> 위, 가이드의 찾은 곳 표시와 같은 색).</summary>
    private readonly Border _helpBox = new()
    {
        BorderBrush = HelpWindow.Yellow,
        BorderThickness = new Thickness(2),
        CornerRadius = new CornerRadius(4),
        IsHitTestVisible = false,
        IsVisible = false,
    };

    /// <summary>대상 도움말 겹판(<see cref="_helpPicker"/>)에 테두리를 얹고 누르기·움직이기를 받는다.</summary>
    private void AttachHelpPicker()
    {
        _helpPicker.Child = new Canvas { Children = { _helpBox } };
        _helpPicker.PointerPressed += OnHelpPickerPressed;
        _helpPicker.PointerReleased += OnHelpPickerReleased;
        _helpPicker.PointerMoved += (_, e) => ShowHelpBox(HelpTargetAt(e.GetPosition(_helpPicker))?.Bounds);
        _helpPicker.PointerExited += (_, _) => ShowHelpBox(null);
    }

    private void ToggleHelpPicker()
    {
        if (_helpPicker.IsVisible)
        {
            StopHelpPicker();
            return;
        }
        _helpPreviousMessage = _stMessage.Text;
        _helpPreviousFocus = FocusManager?.GetFocusedElement() as Control;
        _helpPicker.IsVisible = true;
        _helpPicker.Focus();
        ShowMessage(UiStrings.Help_SelectHint);
    }

    private void StopHelpPicker()
    {
        if (!_helpPicker.IsVisible) return;
        _helpPicker.IsVisible = false;
        _helpClickPending = false;
        ShowHelpBox(null);
        _helpPreviousFocus?.Focus();
        _helpPreviousFocus = null;
        ShowMessage(_helpPreviousMessage ?? "");
        _helpPreviousMessage = null;
    }

    private void OnHelpPickerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        var point = e.GetCurrentPoint(_helpPicker);
        if (!point.Properties.IsLeftButtonPressed && !point.Properties.IsRightButtonPressed) return;
        _helpPickedPage = point.Properties.IsLeftButtonPressed ? HelpTargetAt(e.GetPosition(_helpPicker))?.Target : null;
        _helpClickPending = true;
        e.Pointer.Capture(_helpPicker);
    }

    private void OnHelpPickerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = true;
        if (!_helpClickPending) return;
        e.Pointer.Capture(null);
        var target = _helpPickedPage;
        bool cancelled = e.InitialPressMouseButton == MouseButton.Right;
        StopHelpPicker();
        if (cancelled) return;
        if (target is null)
        {
            ShowMessage(UiStrings.Help_NoTarget);
            return;
        }
        HelpWindow.Open(this).ShowPage(target.Value);
    }

    /// <summary>대상 테두리를 <paramref name="bounds"/>(<see cref="_helpPicker"/> 좌표)에 보인다. null이면 숨긴다.</summary>
    private void ShowHelpBox(Rect? bounds)
    {
        if (bounds is not { Width: > 0, Height: > 0 } r)
        {
            _helpBox.IsVisible = false;
            return;
        }
        // 대상 바로 바깥을 두르되 창 밖으로 나가지 않게.
        r = r.Inflate(1).Intersect(new Rect(_helpPicker.Bounds.Size));
        Canvas.SetLeft(_helpBox, r.X);
        Canvas.SetTop(_helpBox, r.Y);
        _helpBox.Width = r.Width;
        _helpBox.Height = r.Height;
        _helpBox.IsVisible = true;
    }

    /// <summary>
    /// 점(<see cref="_helpPicker"/> 좌표) 아래의 대상 도움말 자리와 그 대상의 테두리(같은 좌표). 대상 테두리와 누른 결과가 모두 이것으로 정해진다.
    /// 겹판 아래에서 실제로 그려진 맨 위 컨트롤부터 바깥쪽 부모로 올라가며 처음 자리가 정해진 컨트롤을 쓴다(가장 안쪽 대상이 이김).
    /// </summary>
    private (HelpTarget Target, Rect Bounds)? HelpTargetAt(Avalonia.Point position)
    {
        if (_helpPicker.GetVisualParent() is not Visual surface || _helpPicker.TranslatePoint(position, surface) is not { } at) return null;
        var hit = surface.GetVisualAt(at, v => !ReferenceEquals(v, _helpPicker));
        for (var v = hit; v is not null && !ReferenceEquals(v, surface); v = v.GetVisualParent())
        {
            if (v is not Control c) continue;
            if (_timeline.HelpTargetFor(c, _helpPicker.TranslatePoint(position, c) ?? default) is { } area)
                return HelpBounds(c, area.Area) is { } b ? (area.Target, b) : null;
            var target = _toolOptions.HelpTargetFor(c) ?? _colors.HelpTargetFor(c) ?? _preview.HelpTargetFor(c) ?? MainHelpTarget(c);
            if (target is { } t) return HelpBounds(c, new Rect(c.Bounds.Size)) is { } bounds ? (t, bounds) : null;
        }
        return null;
    }

    /// <summary><paramref name="area"/>(<paramref name="visual"/> 좌표)를 <see cref="_helpPicker"/> 좌표로 옮기고, 잘라 그리는 부모(스크롤 등) 밖은 뺀다.</summary>
    private Rect? HelpBounds(Visual visual, Rect area)
    {
        if (visual.TranslatePoint(area.Position, _helpPicker) is not { } at) return null;
        var rect = new Rect(at, area.Size);
        for (var p = visual.GetVisualParent(); p is not null && !ReferenceEquals(p, _helpPicker.GetVisualParent()); p = p.GetVisualParent())
        {
            if (p.ClipToBounds && p.TranslatePoint(default, _helpPicker) is { } origin)
                rect = rect.Intersect(new Rect(origin, p.Bounds.Size));
        }
        return rect;
    }

    /// <summary>메인 창이 직접 가진 컨트롤(도구 막대·위 막대·탭·캔버스·상태 표시줄)의 가이드 자리. 모르는 컨트롤이면 null.</summary>
    private HelpTarget? MainHelpTarget(Control c)
    {
        if (c is ToolSlotButton slot && _toolSlots.Contains(slot)) return new(HelpTarget.ToolPage(slot.Current));
        if (ReferenceEquals(c, _tbSave)) return new("files-save");
        if (ReferenceEquals(c, _tbRevert)) return new("files-revert");
        if (ReferenceEquals(c, _tbFiles) || ReferenceEquals(c, _browser)) return new("screen-file-browser");
        if (ReferenceEquals(c, _cbZoom) || ReferenceEquals(c, _cbFit)) return new("screen-top-bar", "screen-top-bar-2");
        if (ReferenceEquals(c, _cbGrid)) return new("screen-top-bar", "screen-top-bar-3");
        if (ReferenceEquals(c, _cbTiles)) return new("screen-top-bar", "screen-top-bar-4");
        if (ReferenceEquals(c, _cbTiled)) return new("screen-top-bar", "screen-top-bar-5");
        if (ReferenceEquals(c, _cbOnion)) return new("screen-top-bar", "screen-top-bar-6");
        if (ReferenceEquals(c, _cbEffects)) return new("screen-top-bar", "screen-canvas-effects-toggle");
        if (ReferenceEquals(c, _cbPivot)) return new("screen-top-bar");
        if (ReferenceEquals(c, _panes[0].Strip) || ReferenceEquals(c, _panes[1].Strip)) return new("screen-tabs");
        if (ReferenceEquals(c, _start)) return new("start-first-run");
        if (ReferenceEquals(c, _paneArea)) return new("screen-canvas");
        if (ReferenceEquals(c, _helpMenuRow) || ReferenceEquals(c, _helpContextBar)) return new("screen-top-bar");
        // 상태 표시줄 칸 = 가이드 표의 그림 번호 줄(①~⑦).
        if (IsStatusCell(c, _stPos)) return new("screen-status-bar", Anchor: "①");
        if (IsStatusCell(c, _stColor)) return new("screen-status-bar", Anchor: "②");
        if (IsStatusCell(c, _stSize)) return new("screen-status-bar", Anchor: "③");
        if (IsStatusCell(c, _stFrame)) return new("screen-status-bar", Anchor: "④");
        if (IsStatusCell(c, _stZoom)) return new("screen-status-bar", Anchor: "⑤");
        if (IsStatusCell(c, _stDirty) || ReferenceEquals(c, _stDirtyDot)) return new("screen-status-bar", Anchor: "⑥");
        if (ReferenceEquals(c, _stMessage)) return new("screen-status-bar", Anchor: "⑦");
        if (ReferenceEquals(c, _helpStatus)) return new("screen-status-bar");
        return null;
    }

    /// <summary>상태 표시줄 글자 또는 그 칸(테두리 상자, "수정됨"은 점과 묶은 줄도).</summary>
    private static bool IsStatusCell(Control c, TextBlock label)
    {
        for (Control? p = label; p is not null and not DockPanel; p = p.Parent as Control)
            if (ReferenceEquals(c, p)) return true;
        return false;
    }
}

/// <summary>
/// 대상 도움말(F1)이 열 가이드 자리: 쪽 id + 그 쪽의 화면 그림(<see cref="Shot"/>) 또는 자리 글(<see cref="Anchor"/>).
/// 자리 글은 소제목·표 줄 첫 칸·목록 항목·문단을 찾는 글이다(<see cref="HelpWindow"/>가 찾음). 가이드가 앱 문자열을 그대로 쓰는 곳은
/// 그 앱 문자열(지금 언어)을, 아니면 영어 가이드 글을 적는다(한국어 화면은 영어 쪽에서 찾은 같은 자리로 간다). 못 찾으면 쪽 맨 위.
/// </summary>
internal readonly record struct HelpTarget(string PageId, string? Shot = null, string? Anchor = null)
{
    /// <summary>도구마다 자세히 설명하는 가이드 쪽.</summary>
    public static string ToolPage(ToolKind tool) => tool switch
    {
        ToolKind.Pencil => "tools-pencil",
        ToolKind.GradientPencil => "tools-gradient-pencil",
        ToolKind.Eraser => "tools-eraser",
        ToolKind.Fill => "tools-fill",
        ToolKind.Gradient => "tools-gradient",
        ToolKind.Picker => "tools-picker",
        ToolKind.Shape => "tools-shape",
        ToolKind.Text => "tools-text",
        ToolKind.Select or ToolKind.EllipseSelect or ToolKind.Lasso or ToolKind.MagicWand => "tools-select",
        ToolKind.Move => "tools-move",
        ToolKind.Dodge => "tools-dodge",
        ToolKind.Dither => "tools-dither",
        ToolKind.ReplaceBrush => "tools-replace-brush",
        ToolKind.Pivot => "tools-pivot",
        ToolKind.Slice => "tools-nine-slice",
        _ => throw new ArgumentOutOfRangeException(nameof(tool)),
    };
}
