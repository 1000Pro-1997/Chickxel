using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 메인 창의 대상 도움말(F1): 고르기는 <see cref="HelpPicker"/>가 하고, 메인 창은 안내 글을 상태 표시줄에 보이며(<see cref="IHelpPickerHost"/>)
/// 직접 가진 컨트롤의 자리를 준다(<see cref="IHelpTargetSource"/>). 패널(도구 설정·색·미리보기·타임라인)은 스스로 자리를 준다.
/// </summary>
public sealed partial class MainForm : IHelpPickerHost, IHelpTargetSource
{
    void IHelpPickerHost.HelpPickerStarted()
    {
        _helpPreviousMessage = _stMessage.Text;
        ShowMessage(UiStrings.Help_SelectHint);
    }

    void IHelpPickerHost.HelpPickerStopped(bool noTarget)
    {
        ShowMessage(noTarget ? UiStrings.Help_NoTarget : _helpPreviousMessage ?? "");
        _helpPreviousMessage = null;
    }

    (HelpTarget Target, Rect Area)? IHelpTargetSource.HelpTargetFor(Control c, Avalonia.Point local) => HelpTargets.Whole(c, MainHelpTarget(c));

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
