using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>켜진 효과 중 캔버스 늘리기(<see cref="LayerEffect.ExpandCanvas"/>)를 켠 것: 없음 / 일부 / 전부.</summary>
    private enum GrowScope { None, Some, All }

    private static GrowScope GrowScopeOf(IReadOnlyList<LayerEffect> effects)
    {
        bool any = false, all = true;
        foreach (var e in effects)
        {
            if (!e.Enabled) continue;
            if (e.ExpandCanvas) any = true;
            else all = false;
        }
        return !any ? GrowScope.None : all ? GrowScope.All : GrowScope.Some;
    }

    /// <summary>효과 결과 <paramref name="image"/>(원래 그림 대비 자리 <paramref name="x"/>, <paramref name="y"/>)의 불투명 범위(원래 그림 기준). 없으면 <see cref="Rectangle.Empty"/>.</summary>
    private static Rectangle OpaqueAt(PixelImage image, int x, int y)
    {
        var opaque = image.GetOpaqueBounds();
        if (opaque.IsEmpty) return Rectangle.Empty;
        opaque.Offset(x, y);
        return opaque;
    }

    /// <summary>
    /// 프레임 하나에서 캔버스 늘리기를 켠 효과만 남긴 결과(<see cref="LayerEffects.ExpandingOnly"/> — 끈 효과는 캔버스에 잘린 채로 남음)의 불투명 범위
    /// (원래 그림 기준, <paramref name="effects"/> = 그 프레임에 맞춘 목록). 남은 켜진 효과가 없으면 <see cref="Rectangle.Empty"/>. 어느 스레드에서든.
    /// </summary>
    private static Rectangle GrowingOutput(PixelImage source, IReadOnlyList<LayerEffect> effects, EffectPadding padding, Layer? layer, CancellationToken token)
    {
        var growing = LayerEffects.ExpandingOnly(effects);
        if (!LayerEffects.AnyEnabled(growing)) return Rectangle.Empty;
        var (image, x, y) = LayerEffects.Render(source, growing, padding, layer, token);
        return OpaqueAt(image, x, y);
    }

    /// <summary>
    /// 늘린 뒤 모든 프레임에서 이 레이어의 캔버스 늘리기를 켠 켜진 효과만 남긴 결과가 차지하는 캔버스 좌표 범위(<see cref="WaitOutputBounds"/>가 그리던 것이 없을 때).
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
            var opaque = GrowingOutput(source, effects, LayerEffects.Padding(effects, source.Width, source.Height), null, CancellationToken.None);
            if (opaque.IsEmpty) continue;
            opaque.Offset(ox, oy);
            bounds = Rectangle.Union(bounds, opaque);
        }
        return bounds;
    }

    /// <summary>
    /// 지금 효과·프레임 늘리기와 선택한 방식으로 확인 때 적용할 캔버스 확대(<see cref="WaitOutputBounds"/>). 캔버스 늘리기를 켠 켜진 효과가 없으면
    /// 지금 캔버스 그대로(<see cref="EffectCanvasExpansion.IsNone"/>).
    /// </summary>
    private EffectCanvasExpansion CanvasExpansion(IReadOnlyList<LayerEffect> all)
        => GrowScopeOf(all) == GrowScope.None
            ? new EffectCanvasExpansion(_project.Width, _project.Height, 0, 0, true)
            : EffectCanvasExpansion.FromBounds(_project.Width, _project.Height, WaitOutputBounds(all), s_canvasExpandMode);

    /// <summary>
    /// 모든 프레임에서 캔버스 늘리기를 켠 효과만 남긴 결과 범위. 작업 스레드가 다 그렸으면 그 결과를 쓰고, 아직 그리는 중이면 덮개(퍼센트)를 띄우고 끝날 때까지 기다린다 —
    /// 그리던 것을 이어 그리므로 처음부터 다시 그리지 않는다(예전에는 확인을 누르면 모든 프레임을 화면 스레드에서 다시 그려 몇 초씩 멈췄다).
    /// 그리던 것이 없으면 덮개를 띄우고 작업 스레드에서 모두 그려 잰다.
    /// </summary>
    private Rectangle WaitOutputBounds(IReadOnlyList<LayerEffect> all)
    {
        if (RenderedOutputBounds(grow: true) is { } done) return done;
        if (_renders is { } job && !job.Cts.IsCancellationRequested)
        {
            WorkOverlay.Run(this, UiStrings.Busy_EffectBounds, progress =>
            {
                using var stage = progress.Begin(job.Count);
                int reported = 0;
                while (!job.Task.IsCompleted)
                {
                    job.Task.Wait(30);
                    int finished = job.Count - Volatile.Read(ref job.Remaining);
                    stage.Step(finished - reported);
                    reported = finished;
                }
            });
            if (RenderedOutputBounds(grow: true) is { } bounds) return bounds;
        }
        return WorkOverlay.Run(this, UiStrings.Busy_EffectBounds, _ => EffectOutputBounds(all));
    }

    /// <summary>모든 프레임에 공통인 전·후 미리보기 영역과 확인 후 캔버스(원래 캔버스 좌표).</summary>
    private Rectangle _previewBounds, _previewCanvas;

    /// <summary>
    /// 모든 프레임의 결과로 확인 때 적용할 캔버스를 계산하고, 미리보기 배율·위치를 고정한다. 아직 모든 프레임을 그리지 않았으면
    /// 지금 자리·배율을 그대로 둔다(다 그리면 다시 부름). 늘릴 캔버스는 캔버스 늘리기를 켠 효과만 남긴 결과로 재고,
    /// 끈 효과가 (늘린) 캔버스 밖으로 나가는 부분은 잘릴 것이므로 미리보기 바깥에 흐리게 남긴다.
    /// </summary>
    private void SyncCanvasExpansion()
    {
        SyncExpandControls();
        if (RenderedOutputBounds(grow: false) is not { } output || RenderedOutputBounds(grow: true) is not { } grow) return;
        var scope = GrowScopeOf(VirtualEffects());
        var currentCanvas = new Rectangle(0, 0, _project.Width, _project.Height);
        _previewBounds = _previewCanvas = currentCanvas;
        try
        {
            if (scope != GrowScope.None)
            {
                var plan = EffectCanvasExpansion.FromBounds(_project.Width, _project.Height, grow, s_canvasExpandMode);
                _previewCanvas = new Rectangle(-plan.OffsetX, -plan.OffsetY, plan.Width, plan.Height);
                _previewBounds = _previewCanvas;
                _expandNote.Foreground = AppTheme.DimTextBrush;
                _expandNote.Text = plan.IsNone(_project.Width, _project.Height)
                    ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_BDEB632BC8, _project.Width, _project.Height)
                    : $"{_project.Width}×{_project.Height} → {plan.Width}×{plan.Height}"
                      + (s_canvasExpandMode == EffectCanvasExpandMode.KeepRatio && !plan.RatioPreserved ? UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_6BBDE7B81F : "");
                ToolTip.SetTip(_expandNote, plan.IsNone(_project.Width, _project.Height)
                    ? UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_EFFF97903C
                    : string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_LayerEffectsDialog_Canvas_SyncCanvasExpansion_AA4018CCC5, plan.OffsetX, plan.OffsetY));
            }
            if (scope != GrowScope.All && Rectangle.Union(_previewCanvas, output) is var shown
                && shown.Width <= PixelImage.MaxSize && shown.Height <= PixelImage.MaxSize)
            {
                // 캔버스 늘리기를 끈 효과가 잘릴 부분은 캔버스 바깥에 흐리게 남기되, 프레임마다 배율이 흔들리지 않는다.
                _previewBounds = shown;
            }
        }
        catch (InvalidOperationException ex)
        {
            _expandNote.Foreground = AppTheme.WarningBrush;
            _expandNote.Text = ex.Message;
            ToolTip.SetTip(_expandNote, ex.Message);
        }
    }

    /// <summary>늘리는 방식 고르기·안내 글은 캔버스 늘리기를 켠 켜진 효과가 있을 때만 쓴다.</summary>
    private void SyncExpandControls()
    {
        bool any = GrowScopeOf(VirtualEffects()) != GrowScope.None;
        _expandMode.IsEnabled = any;
        _expandNote.IsVisible = any;
    }

    /// <summary>그림 크기는 그대로 두고 계획한 크기·자리로 프로젝트 캔버스를 늘린다.</summary>
    private void ApplyCanvasExpansion(EffectCanvasExpansion plan)
        => _project.ResizeCanvasAndScale(plan.Width, plan.Height,
            new Rectangle(plan.OffsetX, plan.OffsetY, _project.Width, _project.Height));

    private static string CanvasExpansionSummary(int oldWidth, int oldHeight, EffectCanvasExpansion plan)
        => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Canvas_CanvasExpansionSummary_332F7D70DF,oldWidth,oldHeight,plan.Width,plan.Height);
}
