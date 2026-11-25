using Chickxel.App.Localization;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

// 타임라인의 효과 넣기·늘리기: 효과 목록 패널(EffectListPanel)에서 끌어 온 효과를 칸(레이어 × 프레임)에 효과 창 없이 넣기(끄는 동안 넣은 모습을 미리 보임),
// 효과 상자 오른쪽 끝을 마지막 프레임 너머로 끌어 마지막 프레임과 링크된 프레임을 더하며 범위 늘리기.
public sealed partial class TimelinePanel
{
    /// <summary>
    /// 끄는 중 넣은 모습 미리보기: 효과를 잠시 더해 둔 프로젝트·레이어, 미리보기 전 효과, 그 자리(레이어 번호, 프레임 범위). 없으면 null.
    /// 미리 더한 효과는 실행취소 기록·수정됨 표시 없이 들어가고(<see cref="SpriteProject.PreviewLayerEffects"/>), 타임라인 효과 줄은 미리보기 전 그대로 그린다
    /// (줄이 늘었다 줄었다 하며 포인터 아래 칸이 바뀌지 않게 — <see cref="ShownEffectCount"/>).
    /// </summary>
    private (SpriteProject Project, Layer Layer, LayerEffect[] Original, (int Layer, int From, int To) Target)? _dropPreview;

    /// <summary>효과 목록에서 끄는 중 넣은 모습을 미리 보이는 중인지(효과가 아직 들어가지 않음 — 튜토리얼이 확정된 추가와 가름).</summary>
    internal bool PreviewingEffectDrop => _dropPreview is not null;

    /// <summary>
    /// 효과 목록 패널에서 끄는 중: 포인터 아래 칸에 넣을 자리(레이어 행 × 프레임 범위)를 보이고, 캔버스·미리보기에는 그 자리에 효과를 넣은 모습을 미리 보인다
    /// (자리가 바뀌면 다시, 칸 밖이면 되돌림). 놓을 수 있는 자리면 true.
    /// </summary>
    public bool ShowEffectDrop(PointerEventArgs e, IReadOnlyList<LayerEffect> effects, EffectRecipe? template)
    {
        var target = _grid.ShowEffectDrop(e.GetPosition(_grid));
        PreviewDrop(target, effects, template);
        return target is not null;
    }

    /// <summary>효과 목록 패널 끌기가 끝남(놓았거나 취소): 넣을 자리 표시와 미리보기를 지운다.</summary>
    public void HideEffectDrop()
    {
        _grid.ShowEffectDrop(null);
        EndDropPreview();
    }

    /// <summary>효과 목록 패널에서 놓음: 미리보기를 되돌리고, 포인터 아래 칸이면 그 레이어에 그 프레임(범위)으로 넣는다(<see cref="AddEffects"/>), 칸 밖이면 아무것도 하지 않는다.</summary>
    public void DropEffects(PointerEventArgs e, IReadOnlyList<LayerEffect> effects, EffectRecipe? template)
    {
        var target = _grid.EffectDropAt(e.GetPosition(_grid));
        HideEffectDrop();
        if (target is { } t) AddEffects(t.Layer, t.From, t.To, effects, template);
    }

    /// <summary>넣을 자리 <paramref name="target"/>에 효과를 넣은 모습을 미리 보인다(같은 자리면 그대로, 자리가 없으면 되돌리기만).</summary>
    private void PreviewDrop((int Layer, int From, int To)? target, IReadOnlyList<LayerEffect> effects, EffectRecipe? template)
    {
        if (target == _dropPreview?.Target && ReferenceEquals(P, _dropPreview?.Project)) return;
        EndDropPreview();
        if (target is not { } t || P is not { } p || t.Layer >= p.Layers.Count || effects.Count == 0) return;
        var layer = p.Layers[t.Layer];
        var original = layer.Effects.ToArray();
        var range = FrameRange.Of(t.From, t.To, p.Frames.Count);
        var added = template?.PreviewEffects(range) ?? Placed(effects, range);
        _dropPreview = (p, layer, original, t);
        p.PreviewLayerEffects(t.Layer, [.. original, .. added]);
    }

    /// <summary>넣은 모습 미리보기를 미리보기 전 효과로 되돌린다(없으면 아무것도 안 함).</summary>
    private void EndDropPreview()
    {
        if (_dropPreview is not { } d) return;
        _dropPreview = null;
        if (d.Project.IndexOf(d.Layer) is var index && index >= 0) d.Project.EndLayerEffectsPreview(index, d.Original, commit: false);
    }

    /// <summary>타임라인에 줄로 그릴 효과 수: 넣은 모습을 미리 보는 레이어는 미리보기 전 효과 수.</summary>
    private int ShownEffectCount(Layer layer)
        => _dropPreview is { } d && ReferenceEquals(d.Layer, layer) ? d.Original.Length : layer.Effects.Count;

    /// <summary>목록의 효과를 프레임 <paramref name="range"/>에 넣을 모양으로(첫 효과는 클리핑 없음).</summary>
    private static LayerEffect[] Placed(IReadOnlyList<LayerEffect> effects, FrameRange? range)
        => [.. effects.Select((effect, index) => effect with { Frames = range, ClipToPrevious = index > 0 && effect.ClipToPrevious })];

    /// <summary>효과 목록 패널에서 두 번 누름: 지금 레이어의 지금 칸(그 칸이 든 셀 묶음이면 그 프레임 범위)에 넣는다.</summary>
    public void AddEffectsAtCurrent(IReadOnlyList<LayerEffect> effects, EffectRecipe? template)
    {
        if (P is not { } p || p.Layers.Count == 0 || p.Frames.Count == 0) return;
        int layer = Math.Clamp(_ctx.CurrentLayer, 0, p.Layers.Count - 1), frame = Math.Clamp(_ctx.CurrentFrame, 0, p.Frames.Count - 1);
        var (from, to) = _grid.EffectDropFrames(layer, frame);
        AddEffects(layer, from, to, effects, template);
    }

    /// <summary>
    /// 효과(<paramref name="effects"/> — 에셋이면 레시피 <paramref name="template"/>로 스크립트 원문까지)를 레이어 <paramref name="layer"/> 효과 목록 끝에
    /// 프레임 [<paramref name="from"/>, <paramref name="to"/>] 범위로 기본 설정 그대로 넣는다(효과 창 없이, 실행취소 한 단계 — 모든 프레임을 덮으면 "모든 프레임").
    /// 효과 줄을 펼치고 넣은 효과를 고른다. 레시피가 모든 프레임 정책이면 범위 대신 모든 프레임(<see cref="EffectRecipe.Instantiate"/>).
    /// </summary>
    private void AddEffects(int layer, int from, int to, IReadOnlyList<LayerEffect> effects, EffectRecipe? template)
    {
        if (P is not { } p || layer < 0 || layer >= p.Layers.Count || effects.Count == 0) return;
        var target = p.Layers[layer];
        var range = FrameRange.Of(from, to, p.Frames.Count);
        int first = target.Effects.Count;
        IReadOnlyList<LayerEffect> added = [];
        _ctx.Structural(() =>
        {
            added = template?.Instantiate(p, range) ?? Placed(effects, range);
            p.SetLayerEffects(layer, [.. target.Effects, .. added]);
        });
        if (target.Effects.Count != first + added.Count) return;
        if (target.EffectsCollapsed) p.SetLayerEffectsCollapsed(layer, false);
        _ctx.SetCurrent(layer, Math.Clamp(from, 0, p.Frames.Count - 1));
        _grid.SelectEffects(Enumerable.Range(first, added.Count).Select(i => (target, i)));
        var frames = added[0].Frames is { } r ? FramesText(Enumerable.Range(r.From, r.To - r.From + 1).ToList()) : UiStrings.S_TimelinePanel_ShowCelMenu_1631708D81;
        Say(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectList_Added,
            string.Join(", ", added.Select(e => e.DisplayName).Distinct()), target.Name, frames));
    }

    /// <summary>
    /// 효과 상자 오른쪽 끝을 마지막 프레임 너머로 끌어 놓음: 마지막 프레임 뒤에 그 프레임 사본 <paramref name="extra"/>개를 더하고
    /// (모든 레이어의 셀이 마지막 프레임 셀과 링크 — 프레임 늘리기 "마지막 프레임으로", 마지막 프레임까지 걸친 태그·다른 효과 범위도 함께 늘어남),
    /// 그 효과 범위를 [<paramref name="from"/>, 새 마지막 프레임]으로 한다(실행취소 한 단계).
    /// </summary>
    private void ExtendEffectPastEnd(Layer layer, int effectIndex, int from, int extra)
    {
        if (P is not { } p || p.IndexOf(layer) is var index && index < 0 || effectIndex >= layer.Effects.Count || extra < 1 || p.Frames.Count == 0) return;
        int last = p.Frames.Count - 1;
        var extension = new FrameExtension(last, last, FrameExtension.ClampCount(1, extra + 1), FrameExtendMode.HoldLast);
        _ctx.Structural(() => p.Edit(UiStrings.S_LayerEffectsDialog_Extend_EndPreview_E092F138F7,
            ProjectChange.Layers | ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
            {
                p.ExtendFrames(extension);
                var list = layer.Effects.ToArray();
                list[effectIndex] = list[effectIndex] with { Frames = FrameRange.Of(from, p.Frames.Count - 1, p.Frames.Count) };
                p.SetLayerEffects(index, list);
            }));
        _grid.SelectEffects([(layer, effectIndex)]);
        Say(string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectList_Extended, p.Frames.Count, extension.Added));
    }

    private sealed partial class TimelineGrid
    {
        /// <summary>효과 목록 패널에서 끄는 중 넣을 자리(레이어 번호, 프레임 범위), 아니면 null.</summary>
        private (int Layer, int From, int To)? _effectDrop;

        /// <summary>
        /// 효과 상자 오른쪽 끝을 마지막 프레임 너머로 끄는 중이면 더할 프레임 수(0 = 아님): 그만큼 "다음 프레임" 칸 자리에 링크 표시 칸을 보이고,
        /// 놓으면 <see cref="TimelinePanel.ExtendEffectPastEnd"/>.
        /// </summary>
        private int _fxExtra;

        /// <summary>마지막 프레임 뒤에 붙일 칸을 미리 보이는 중인지("다음 프레임" 단추 끌기 · 효과 범위를 끝 너머로 끌기).</summary>
        private bool GhostPreview => _ghostDragShown || _fxExtra > 0;

        /// <summary>점 <paramref name="pos"/>(없으면 지움)에 맞춰 넣을 자리 표시를 바꾸고 그 자리(놓을 수 없으면 null)를 돌려준다.</summary>
        public (int Layer, int From, int To)? ShowEffectDrop(Avalonia.Point? pos)
        {
            var target = pos is { } p ? EffectDropAt(p) : null;
            if (target != _effectDrop)
            {
                _effectDrop = target;
                InvalidateVisual();
            }
            return target;
        }

        /// <summary>점 아래 칸(레이어 본줄·효과 줄 × 프레임 열)에 효과를 넣을 자리: 그 레이어와 프레임 범위(<see cref="EffectDropFrames"/>). 칸 밖이면 null.</summary>
        public (int Layer, int From, int To)? EffectDropAt(Avalonia.Point pos)
        {
            if (Ctx.Project is null || pos.X < NameW || pos.X >= ClientW || pos.Y < TagH + HeadH || pos.Y >= ClientH) return null;
            int l = LayerAt(pos.Y), f = FrameAt(pos.X);
            if (l < 0 || f < 0) return null;
            var (from, to) = EffectDropFrames(l, f);
            return (l, from, to);
        }

        /// <summary>
        /// 레이어 <paramref name="layer"/> 프레임 <paramref name="frame"/>에 넣을 범위: 그 칸이 고른 셀 묶음(두 프레임 이상, 떨어진 칸 묶음 제외) 안이면 그 묶음의 프레임 범위,
        /// 아니면 그 프레임 하나.
        /// </summary>
        public (int From, int To) EffectDropFrames(int layer, int frame)
        {
            var (a, b) = Range;
            return _kind == SelKind.Cels && !ScatteredCels && a >= 0 && a < b && frame >= a && frame <= b && SelectedRowLayers().Contains(layer)
                ? (a, b)
                : (frame, frame);
        }

        /// <summary>넣을 자리: 그 레이어 행(효과 줄 포함) × 프레임 범위를 옅은 파란 칸과 테두리로.</summary>
        private void DrawEffectDrop(DrawingContext g, SpriteProject p)
        {
            if (_effectDrop is not { } d || d.Layer >= p.Layers.Count || d.To >= p.Frames.Count) return;
            double x0 = FrameX(d.From) + 1, x1 = FrameX(d.To) + FrameW(d.To) - 1, y0 = LayerY(d.Layer) + 1;
            g.DrawRectangle(MarqueeBrush, AccentPen, new Rect(x0, y0, Math.Max(0, x1 - x0), RowFull(p.Layers[d.Layer]) - 2), 3, 3);
        }
    }
}
