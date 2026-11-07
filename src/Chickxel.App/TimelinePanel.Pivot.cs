using Chickxel.App.Localization;
using Chickxel.Core.Model;

namespace Chickxel.App;

// 타임라인의 피벗 관련: 왼쪽 위 캔버스 칩(아이콘 = 캔버스 크기 모드, 글자 = 캔버스 피벗 대상), 프레임 메뉴의 피벗 항목(피벗 대상에),
// 셀·프레임 머리글의 피벗 표시, 캔버스 피벗 대표 레이어(이름 앞 왕관·레이어 메뉴).
public sealed partial class TimelinePanel
{
    /// <summary>캔버스 칩의 글자("캔버스 W×H")를 눌렀다: 피벗 도구로 캔버스 피벗을 고치게 해 달라는 요청.</summary>
    public event EventHandler? CanvasPivotRequested;

    private void RequestCanvasPivot() => CanvasPivotRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>프레임 메뉴의 피벗 항목: 피벗 대상(지금 레이어 또는 캔버스 피벗)의 그 프레임 피벗을 기본값으로 · 그 프레임만 따로.</summary>
    private void AddFramePivotItems(List<Control> items, int frame)
    {
        items.Add(Item(UiStrings.S_TimelinePanel_AddFrameTail_30B4C69887, () => _ctx.ResetPivotFrame(frame)));
        items.Add(Item(UiStrings.S_TimelinePanel_AddFrameTail_8439D90B67, () => _ctx.KeepPivotFrame(frame)));
    }

    /// <summary>레이어 메뉴: 이 레이어를 캔버스 피벗 대표 레이어로 고르기, 이미 대표 레이어면 풀기.</summary>
    private MenuItem PivotLayerItem(SpriteProject p, Layer layer)
        => ReferenceEquals(p.PivotLayer, layer)
            ? Item(UiStrings.Timeline_PivotLayerClear, () => _ctx.SetPivotLayer(null))
            : Item(UiStrings.Timeline_PivotLayerSet, () => _ctx.SetPivotLayer(layer));

    private sealed partial class TimelineGrid
    {
        /// <summary>캔버스 칩에서 마우스가 가리키는 곳: 아이콘(캔버스 크기 모드) · 글자(캔버스 피벗 대상).</summary>
        private enum ChipPart { None, Size, Pivot }

        /// <summary>캔버스 칩 왼쪽 아이콘 자리 폭.</summary>
        private const int ChipIconW = 20;
        /// <summary>대표 레이어 이름 앞 왕관 자리 폭.</summary>
        private const int CrownW = 14;
        private static readonly IBrush CrownBrush = AppTheme.Brush(Color.FromRgb(255, 196, 64));

        /// <summary>왼쪽 위 모서리(프레임 머리글 줄)의 캔버스 칩 자리(이름 칸이 좁으면 칩도 좁아져 경계선 띠와 겹치지 않음).</summary>
        private Rect CanvasChipRect => new(4, TagH + 5, Math.Min(150, NameW - 4 - DividerGrab - 4), HeadH - 10);
        private ChipPart _canvasChipHover;

        private ChipPart CanvasChipAt(Avalonia.Point pos)
        {
            var r = CanvasChipRect;
            if (!r.Contains(pos)) return ChipPart.None;
            return pos.X < r.X + ChipIconW ? ChipPart.Size : ChipPart.Pivot;
        }

        private bool IsCanvasChipAt(Avalonia.Point pos) => CanvasChipAt(pos) != ChipPart.None;

        /// <summary>캔버스 칩 위의 도움말 풍선(아이콘 = 캔버스 크기, 글자 = 캔버스 피벗).</summary>
        private string? CanvasChipTip => _canvasChipHover switch
        {
            ChipPart.Size => UiStrings.Timeline_CanvasSizeTip,
            ChipPart.Pivot => UiStrings.Timeline_CanvasPivotTip,
            _ => null,
        };

        /// <summary>
        /// 캔버스 칩: 아이콘(누르면 캔버스 크기 모드) + "캔버스 W×H" 글자(누르면 캔버스 피벗 대상). 마우스가 올라간 쪽만 밝게,
        /// 캔버스 피벗 대상인 동안은 글자 쪽을 강조 테두리로.
        /// </summary>
        private void DrawCanvasChip(DrawingContext g, SpriteProject p)
        {
            var r = CanvasChipRect;
            var icon = new Rect(r.X, r.Y, Math.Min(ChipIconW, r.Width), r.Height);
            var text = new Rect(icon.Right, r.Y, Math.Max(0, r.Width - icon.Width), r.Height);
            bool target = Ctx.IsPivotCanvasTarget;
            if (_canvasChipHover == ChipPart.Size) g.FillRectangle(GhostHoverBrush, icon, 3);
            if (target) g.DrawRectangle(ChipAccentBrush, ChipAccentPen, text.Deflate(0.5), 3, 3);
            else if (_canvasChipHover == ChipPart.Pivot) g.FillRectangle(GhostHoverBrush, text, 3);
            VectorIcons.Draw(g, VectorIcon.Maximize2, new Rect(r.X + 3, r.Y, 14, r.Height), 11,
                _canvasChipHover == ChipPart.Size ? AppTheme.TextBrush : AppTheme.DimTextBrush);
            Text(g, string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.Timeline_CanvasSize, p.Width, p.Height),
                new Rect(text.X + 2, r.Y, Math.Max(0, text.Width - 4), r.Height),
                target || _canvasChipHover == ChipPart.Pivot ? AppTheme.TextBrush : AppTheme.DimTextBrush, small: true);
        }

        /// <summary>프레임 머리글의 피벗 표시: 캔버스 피벗 대상일 때만, 대상(캔버스 자신 또는 대표 레이어)이 그 프레임에 따로 둔 피벗이 있으면.</summary>
        private bool HeaderPivotMark(int frame) => Ctx.IsPivotCanvasTarget && Ctx.PivotTargetOwnsFrame(frame);

        /// <summary>셀의 피벗 표시: 그 레이어가 그 프레임에 따로 둔 피벗(부위는 위치도)이 있으면 — 부위는 늘, 일반 레이어·그룹은 지금 레이어 행만.</summary>
        private bool CellPivotMark(SpriteProject p, int layer, int frame)
            => (p.Layers[layer].IsPart || layer == Ctx.CurrentLayer) && p.HasLayerFrameOverride(p.Layers[layer], frame);

        /// <summary>캔버스 피벗 대표 레이어 표시: 이름 앞 작은 왕관(채운 금색).</summary>
        private static void DrawCrown(DrawingContext g, double x, double y, double height)
            => VectorIcons.Draw(g, VectorIcon.Crown, new Rect(x, y, CrownW - 2, height), 11, CrownBrush, strokeWidth: 1.5, filled: true);
    }
}
