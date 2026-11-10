using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.App.Localization;

namespace Chickxel.App;

public sealed partial class LayerEffectsDialog
{
    /// <summary>효과 결과 <paramref name="image"/>(원래 그림 대비 자리 <paramref name="x"/>, <paramref name="y"/>)의 불투명 범위(원래 그림 기준). 없으면 <see cref="Rectangle.Empty"/>.</summary>
    private static Rectangle OpaqueAt(PixelImage image, int x, int y)
    {
        var opaque = image.GetOpaqueBounds();
        if (opaque.IsEmpty) return Rectangle.Empty;
        opaque.Offset(x, y);
        return opaque;
    }

    /// <summary>
    /// '캔버스 늘리기…'로 정한 확대: 확인·영구 적용할 때 효과와 함께 실행취소 한 단계로 캔버스를 늘린다. 없으면 캔버스는 그대로이고 밖으로 나간 효과 결과는 잘린다.
    /// </summary>
    private EffectCanvasExpansion? _canvasPlan;

    /// <summary>
    /// '캔버스 늘리기…'(독·타임라인 효과 줄의 단추와 같은 작은 창): 고른 줄(묶음 머리면 묶음 끝 효과)까지의 결과를 창에서 고친 효과로 모든 프레임에서 잰다.
    /// 방식을 고르는 동안 미리보기가 그 캔버스를 보이고, 늘리기를 누르면 그 확대를 정해 둔다(프로젝트는 확인할 때 바뀜). 창을 취소하면 앞서 정한 것 그대로.
    /// </summary>
    private void GrowCanvas()
    {
        if (SelectedRow is not { } row) return;
        var all = VirtualEffects();
        IReadOnlyList<LayerEffect> through = row.Last >= all.Count - 1 ? all : all.Take(row.Last + 1).ToArray();
        if (!LayerEffects.AnyEnabled(through))
        {
            Ui.Info(this, UiStrings.EffectCanvas_NoOutput);
            return;
        }
        var bounds = MeasureOutput(through, all);
        var staged = _canvasPlan;
        var plan = EffectCanvasDialog.Ask(this, _project, bounds, row.Recipe?.Name ?? _effects[row.First].DisplayName, SetCanvasPlan);
        SetCanvasPlan(plan ?? staged);
    }

    /// <summary>늘릴 캔버스를 정하고(null = 늘리지 않음) 안내 글·미리보기를 맞춘다.</summary>
    private void SetCanvasPlan(EffectCanvasExpansion? plan)
    {
        _canvasPlan = plan;
        _growNote.IsVisible = _dropGrow.IsVisible = plan is not null;
        if (plan is { } p)
            _growNote.Text = string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectCanvas_Staged, _project.Width, _project.Height, p.Width, p.Height);
        SyncCanvasExpansion();
        RefreshPreview();
    }

    /// <summary>
    /// 모든 프레임에서 <paramref name="through"/>(<paramref name="all"/>의 앞부분)의 결과가 차지하는 캔버스 좌표 범위(지금 캔버스 포함). 뒤에 켜진 효과가 없으면
    /// 미리보기가 그린 결과를 쓰고, 아직 그리는 중이면 덮개(퍼센트)를 띄우고 끝날 때까지 기다린다(처음부터 다시 그리지 않음). 아니면 덮개를 띄우고 작업 스레드에서 잰다.
    /// </summary>
    private Rectangle MeasureOutput(IReadOnlyList<LayerEffect> through, IReadOnlyList<LayerEffect> all)
    {
        if (all.Skip(through.Count).All(e => !e.Enabled))
        {
            if (RenderedOutputBounds() is { } done) return done;
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
                if (RenderedOutputBounds() is { } bounds) return bounds;
            }
        }
        return WorkOverlay.Run(this, UiStrings.Busy_EffectBounds, _ => EffectOutputBounds(through));
    }

    /// <summary>모든 프레임에서 이 레이어에 <paramref name="effects"/>를 입힌 결과가 차지하는 캔버스 좌표 범위(지금 캔버스 포함, 불투명 픽셀만). 어느 스레드에서든.</summary>
    private Rectangle EffectOutputBounds(IReadOnlyList<LayerEffect> effects)
    {
        var bounds = new Rectangle(0, 0, _project.Width, _project.Height);
        for (int frame = 0; frame < VirtualCount; frame++)
        {
            var (source, list) = AnimateAt(frame, effects);
            if (source is null || !LayerEffects.AnyEnabled(list)) continue;
            var (image, x, y) = LayerEffects.Render(source, list, LayerEffects.Padding(list, source.Width, source.Height), _layer, CancellationToken.None);
            var opaque = OpaqueAt(image, x, y);
            if (opaque.IsEmpty) continue;
            var at = _layer.IsPart ? _project.GetPartOffset(_layer, frame) : Point.Empty;
            opaque.Offset(at.X, at.Y);
            bounds = Rectangle.Union(bounds, opaque);
        }
        return bounds;
    }

    /// <summary>모든 프레임에 공통인 전·후 미리보기 영역과 확인 후 캔버스(원래 캔버스 좌표).</summary>
    private Rectangle _previewBounds, _previewCanvas;

    /// <summary>
    /// 미리보기의 확인 후 캔버스(지금 캔버스, 또는 '캔버스 늘리기…'로 정한 확대)와 배율·위치를 정한다. 그 밖으로 나가는 효과 결과는 잘릴 것이므로
    /// 다 그린 모든 프레임의 결과 범위까지 미리보기 바깥에 흐리게 남긴다(프레임마다 배율이 흔들리지 않게). 아직 다 그리지 않았으면 다 그린 뒤 다시 부른다.
    /// </summary>
    private void SyncCanvasExpansion()
    {
        _previewCanvas = _canvasPlan is { } plan
            ? new Rectangle(-plan.OffsetX, -plan.OffsetY, plan.Width, plan.Height)
            : new Rectangle(0, 0, _project.Width, _project.Height);
        if (RenderedOutputBounds() is not { } output)
        {
            // 아직 그리는 중: 지금 배율을 두되 캔버스는 다 보이게
            if (!_previewBounds.Contains(_previewCanvas)) _previewBounds = _previewCanvas;
            return;
        }
        var shown = Rectangle.Union(_previewCanvas, output);
        _previewBounds = shown.Width <= PixelImage.MaxSize && shown.Height <= PixelImage.MaxSize ? shown : _previewCanvas;
    }

    /// <summary>그림 크기는 그대로 두고 계획한 크기·자리로 프로젝트 캔버스를 늘린다.</summary>
    private void ApplyCanvasExpansion(EffectCanvasExpansion plan)
        => _project.ResizeCanvasAndScale(plan.Width, plan.Height,
            new Rectangle(plan.OffsetX, plan.OffsetY, _project.Width, _project.Height));

    private static string CanvasExpansionSummary(int oldWidth, int oldHeight, EffectCanvasExpansion plan)
        => string.Format(global::System.Globalization.CultureInfo.CurrentCulture,UiStrings.S_LayerEffectsDialog_Canvas_CanvasExpansionSummary_332F7D70DF,oldWidth,oldHeight,plan.Width,plan.Height);
}
