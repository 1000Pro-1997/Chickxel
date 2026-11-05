using Chickxel.App.Localization;

namespace Chickxel.App;

public sealed partial class MainForm
{
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
        _helpPickedPage = point.Properties.IsLeftButtonPressed ? HelpPageAt(e.GetPosition(_helpPicker)) : null;
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
        HelpWindow.Open(this).ShowPage(target.Value.PageId, target.Value.Shot, target.Value.ToolRow);
    }

    private HelpTarget? HelpPageAt(Avalonia.Point position)
    {
        bool Hit(Control control)
        {
            if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0) return false;
            var origin = control.TranslatePoint(new Avalonia.Point(0, 0), _helpPicker);
            return origin is { } point && new Rect(point, control.Bounds.Size).Contains(position);
        }

        if (Hit(_tbSave)) return new("files-save");
        if (Hit(_tbRevert)) return new("files-revert");
        if (Hit(_tbFiles) || Hit(_browser)) return new("screen-file-browser");
        foreach (var slot in _toolSlots)
            if (Hit(slot)) return new("tools-list", ToolRow: ToolRow(slot.Current));
        if (Hit(_cbZoom) || Hit(_cbFit)) return new("screen-top-bar", "screen-top-bar-2");
        if (Hit(_cbGrid)) return new("screen-top-bar", "screen-top-bar-3");
        if (Hit(_cbTiles)) return new("screen-top-bar", "screen-top-bar-4");
        if (Hit(_cbTiled)) return new("screen-top-bar", "screen-top-bar-5");
        if (Hit(_cbOnion)) return new("screen-top-bar", "screen-top-bar-6");
        if (Hit(_cbEffects)) return new("screen-top-bar", "screen-canvas-effects-toggle");
        if (Hit(_cbPivot)) return new("screen-top-bar");
        if (Hit(_colors)) return new("screen-palette");
        if (Hit(_preview)) return new("screen-preview");
        if (Hit(_toolOptions)) return new("screen-tool-settings");
        if (Hit(_timeline)) return new("screen-timeline");
        if (Hit(_panes[0].Strip) || Hit(_panes[1].Strip)) return new("screen-tabs");
        if (Hit(_start)) return new("start-first-run");
        if (Hit(_paneArea)) return new("screen-canvas");
        if (Hit(_helpMenuRow) || Hit(_helpContextBar)) return new("screen-top-bar");
        if (Hit(_helpStatus)) return new("screen-status-bar");
        return null;
    }

    // 도구 목록 표의 행 순서와 대응한다.
    private static int ToolRow(ToolKind tool) => tool switch
    {
        ToolKind.Pencil => 0,
        ToolKind.GradientPencil => 1,
        ToolKind.Eraser => 2,
        ToolKind.Fill => 3,
        ToolKind.Gradient => 4,
        ToolKind.Picker => 5,
        ToolKind.Shape => 6,
        ToolKind.Text => 7,
        ToolKind.Select or ToolKind.EllipseSelect => 8,
        ToolKind.Lasso => 9,
        ToolKind.MagicWand => 10,
        ToolKind.Move => 11,
        ToolKind.Dodge => 12,
        ToolKind.Dither => 13,
        ToolKind.ReplaceBrush => 14,
        ToolKind.Pivot => 15,
        ToolKind.Slice => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(tool)),
    };

    private readonly record struct HelpTarget(string PageId, string? Shot = null, int? ToolRow = null);
}
