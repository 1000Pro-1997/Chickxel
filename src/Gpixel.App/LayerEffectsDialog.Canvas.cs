using Gpixel.Core.Edit;
using Gpixel.Core.Model;
using Gpixel.App.Localization;

namespace Gpixel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>
    /// 늘린 뒤 모든 프레임에서 이 레이어의 켜진 효과 결과가 차지하는 캔버스 좌표 범위.
    /// 실제 불투명 픽셀만 재므로 한 번 확대한 뒤 같은 효과로 다시 확대해도 더 커지지 않는다.
    /// </summary>
    private Rectangle EffectOutputBounds(IReadOnlyList<LayerEffect> all)
    {
        var bounds = new Rectangle(0, 0, _project.Width, _project.Height);
        int ox = _layer.IsPart ? _layer.PartOffset.X : 0;
        int oy = _layer.IsPart ? _layer.PartOffset.Y : 0;
        for (int frame = 0; frame < VirtualCount; frame++)
        {
            var (source, effects) = AnimateAt(frame, all);
            if (source is null || !LayerEffects.AnyEnabled(effects)) continue;
            var (rendered, dx, dy) = LayerEffects.Render(source, effects);
            var opaque = rendered.GetOpaqueBounds();
            if (opaque.IsEmpty) continue;
            opaque.Offset(ox + dx, oy + dy);
            bounds = Rectangle.Union(bounds, opaque);
        }
        return bounds;
    }

    /// <summary>지금 효과·프레임 늘리기와 선택한 방식으로 확인 때 적용할 캔버스 확대.</summary>
    private EffectCanvasExpansion CanvasExpansion(IReadOnlyList<LayerEffect> all)
        => EffectCanvasExpansion.FromBounds(_project.Width, _project.Height, EffectOutputBounds(all), s_canvasExpandMode);

    /// <summary>캔버스 확대 체크 아래에 계산된 결과 크기를 표시한다.</summary>
    private void SyncCanvasExpansion()
    {
        if (!_expandCanvas.On()) return;
        try
        {
            var plan = CanvasExpansion(VirtualEffects());
            _expandNote.Foreground = AppTheme.DimTextBrush;
            _expandNote.Text = plan.IsNone(_project.Width, _project.Height)
                ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_BDEB632BC8,_project.Width,_project.Height)
                : $"{_project.Width}×{_project.Height} → {plan.Width}×{plan.Height}"
                  + (s_canvasExpandMode == EffectCanvasExpandMode.KeepRatio && !plan.RatioPreserved ? UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_6BBDE7B81F : "");
            ToolTip.SetTip(_expandNote, plan.IsNone(_project.Width, _project.Height)
                ? UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_EFFF97903C
                : string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_AA4018CCC5,plan.OffsetX,plan.OffsetY));
        }
        catch (InvalidOperationException ex)
        {
            _expandNote.Foreground = AppTheme.WarningBrush;
            _expandNote.Text = ex.Message;
            ToolTip.SetTip(_expandNote, ex.Message);
        }
    }

    /// <summary>그림 크기는 그대로 두고 계획한 크기·자리로 프로젝트 캔버스를 늘린다.</summary>
    private void ApplyCanvasExpansion(EffectCanvasExpansion plan)
        => _project.ResizeCanvasAndScale(plan.Width, plan.Height,
            new Rectangle(plan.OffsetX, plan.OffsetY, _project.Width, _project.Height));

    private static string CanvasExpansionSummary(int oldWidth, int oldHeight, EffectCanvasExpansion plan)
        => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Canvas_CanvasExpansionSummary_332F7D70DF,oldWidth,oldHeight,plan.Width,plan.Height);
}
