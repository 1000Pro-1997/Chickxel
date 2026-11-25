using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 합성 순서표(RenderPlan) 만들기: 지금 프로젝트 상태를 담아 다른 스레드에서도 그릴 수 있게 한다.
// 동기 합성(Flatten·FlattenInto·FlattenSubtree·FlattenDressed·EffectSource)도 모두 이 순서표로 그린다.
// 레이어 덩어리 = 부모 자신의 그림(자기 셀 효과까지, 맨 아래) + 자식 덩어리들(아래 → 위). 자손까지 효과(IncludeChildren)가 이 프레임에 켜져 있거나
// 불투명도가 255보다 작은 부모는 덩어리를 먼저 합친 뒤(BlockStep) 효과·불투명도를 입히고, 아니면 단계를 바깥 목록에 그대로 둔다(빠른 길).
// 셀을 고르는 함수(cels: 레이어, 프레임 번호 → 셀)는 스크립트 효과가 보는 다른 레이어(레이어 고르기 설정으로 고른 것)·다른 프레임 원본에도 같이 쓴다(보이기·걸러내기와 관계없이 그 레이어의 셀).
public sealed partial class SpriteProject
{
    /// <summary>덩어리 순서표를 만드는 동안의 설정: 프레임, 보이는지(자기 보이기만 — 조상은 걸어 내려가며 거름), 그릴 자기 셀, 효과가 보는 셀, 자식 표, 안쪽(겹침 규칙)인지.</summary>
    private readonly record struct BlockPass(int Frame, Func<Layer, bool> Shows, Func<Layer, PixelImage?> CelOf, Func<Layer, int, PixelImage?> Cels,
        Dictionary<Layer, List<Layer>> Children, bool Nested);

    /// <summary>
    /// 레이어 덩어리 하나를 <paramref name="steps"/>에 넣는다(안 보이면 자손째 뺌). 자식이 없으면 자기 셀(모든 효과·불투명도). 자식이 있으면 자기 셀(자기 셀 효과)과
    /// 자식 덩어리들을, 자손까지 효과가 켜졌거나 불투명도 &lt; 255거나 <paramref name="forceComposite"/>면 덩어리 단계 하나로 합쳐 넣는다.
    /// <paramref name="opacity"/>를 주면 그 레이어 불투명도 대신 쓴다.
    /// </summary>
    private void EmitBlock(List<RenderStep> steps, Layer layer, BlockPass pass, bool forceComposite = false, byte? opacity = null)
    {
        if (!pass.Shows(layer)) return;
        byte alpha = opacity ?? layer.Opacity;
        if (!pass.Children.TryGetValue(layer, out var kids))
        {
            if (pass.CelOf(layer) is { } only) steps.Add(CelStepOf(layer, pass.Frame, only, alpha, pass.Cels, pass.Nested, layer.Effects));
            return;
        }
        var (own, subtree) = layer.SplitEffects();
        if (pass.Nested) subtree = LayerEffects.WithoutRenderedLayerReaders(subtree);
        bool composite = forceComposite || alpha < 255 || LayerEffects.AnyActive(subtree, pass.Frame);
        var target = composite ? new List<RenderStep>() : steps;
        if (pass.CelOf(layer) is { } cel) target.Add(CelStepOf(layer, pass.Frame, cel, 255, pass.Cels, pass.Nested, own));
        foreach (var kid in kids) EmitBlock(target, kid, pass);
        if (composite && target.Count > 0) steps.Add(SubtreeStepOf(layer, target, subtree, pass.Frame, pass.Cels, alpha, pass.Nested));
    }

    /// <summary>덩어리 단계: 안쪽 단계 <paramref name="inner"/>를 합친 그림에 자손까지 효과 <paramref name="effects"/>를 입혀 <paramref name="opacity"/>로.</summary>
    private BlockStep SubtreeStepOf(Layer layer, IReadOnlyList<RenderStep> inner, IReadOnlyList<LayerEffect> effects, int frameIndex,
        Func<Layer, int, PixelImage?> cels, byte opacity, bool nested)
        => new(layer, inner, effects, frameIndex, _frames.Count, SubtreeEffectPivot(layer, frameIndex), SourcesOf(layer, effects, frameIndex, nested, subtree: true),
            ScriptLayersOf(layer, frameIndex, effects, cels, nested, subtree: true), FrameTagFor(effects, frameIndex), opacity, _width, _height);

    /// <summary>
    /// 프레임 합성 순서표(<see cref="FlattenInto"/>와 같은 결과). <paramref name="layerFilter"/>·<paramref name="celMap"/>은 만드는 동안 부른다.
    /// <paramref name="layerFilter"/>는 레이어마다 자기 그림만 거른다(자식은 따로 거름).
    /// 스크립트 효과가 보는 다른 레이어(<see cref="ScriptLayerContext"/>)는 <paramref name="layerFilter"/>와 관계없이 이 프레임 셀(<paramref name="celMap"/>을 거친 것)을 본다.
    /// </summary>
    public RenderPlan PlanFrame(int frameIndex, bool includeHidden = false, Func<Layer, bool>? layerFilter = null,
        Func<Layer, PixelImage, PixelImage>? celMap = null)
    {
        PixelImage? Mapped(Layer layer)
        {
            var cel = RenderCel(layer, frameIndex);
            return cel is not null && celMap is not null ? celMap(layer, cel) : cel;
        }
        PixelImage? CelOf(Layer layer) => layerFilter is not null && !layerFilter(layer) ? null : Mapped(layer);
        Func<Layer, int, PixelImage?> cels = celMap is null ? DefaultCels : (layer, f) => f == frameIndex ? Mapped(layer) : RenderCel(layer, f);
        var pass = new BlockPass(frameIndex, layer => includeHidden || layer.Visible, CelOf, cels, ChildMap(), false);
        var steps = new List<RenderStep>();
        foreach (var layer in _layers)
            if (layer.Parent is null) EmitBlock(steps, layer, pass);
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>
    /// 레이어 하나의 덩어리(자기 그림 + 보이는 자손) 합성 순서표(<see cref="FlattenSubtree"/>와 같은 결과 — 그 레이어 자신의 보이기는 무시, 자손까지 효과·불투명도 포함).
    /// <paramref name="skins"/>에 든 레이어는 그 스킨 셀로 그린다(나머지는 지금 스킨).
    /// </summary>
    public RenderPlan PlanSubtree(Layer layer, int frameIndex, IReadOnlyDictionary<Layer, Skin>? skins = null)
    {
        var cels = DressedCels(skins);
        var pass = new BlockPass(frameIndex, l => ReferenceEquals(l, layer) || l.Visible, l => cels(l, frameIndex), cels, ChildMap(), false);
        var steps = new List<RenderStep>();
        EmitBlock(steps, layer, pass);
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>
    /// 스킨을 바꿔 입힌 프레임 합성 순서표(지금 스킨은 바꾸지 않음): <paramref name="skins"/>에 든 레이어는 그 스킨 셀로, 나머지는 지금 스킨으로.
    /// <paramref name="excluded"/>에 든 레이어는 자손째 뺀다(숨긴 것처럼). 보이기·불투명도·효과는 <see cref="PlanFrame"/>과 같다.
    /// </summary>
    public RenderPlan PlanDressed(int frameIndex, IReadOnlyDictionary<Layer, Skin>? skins, IReadOnlyCollection<Layer>? excluded = null)
    {
        var cels = DressedCels(skins);
        bool Shows(Layer layer) => layer.Visible && !(excluded is { Count: > 0 } && excluded.Contains(layer));
        var pass = new BlockPass(frameIndex, Shows, l => cels(l, frameIndex), cels, ChildMap(), false);
        var steps = new List<RenderStep>();
        foreach (var layer in _layers)
            if (layer.Parent is null) EmitBlock(steps, layer, pass);
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>
    /// 스킨을 바꿔 입혀 합성한 새 이미지(<see cref="PlanDressed"/>). <paramref name="cache"/>를 주면 잔상 원본 등을 이웃 프레임과 나눠 쓴다(결과는 같다).
    /// </summary>
    public PixelImage FlattenDressed(int frameIndex, IReadOnlyDictionary<Layer, Skin>? skins, IReadOnlyCollection<Layer>? excluded = null, EffectRenderCache? cache = null)
        => PlanDressed(frameIndex, skins, excluded).Render(cache is null ? null : new RenderPlanOptions { Cache = cache });

    /// <summary>스킨 프리셋을 지금 착장 위에 입혀 합성한 새 이미지(<paramref name="preset"/> null = 지금 착장, 프리셋에 없는 레이어는 지금 스킨 — <see cref="FlattenDressed"/>).</summary>
    public PixelImage FlattenSkinPreset(int frameIndex, SkinPreset? preset, IReadOnlyCollection<Layer>? excluded = null, EffectRenderCache? cache = null)
        => FlattenDressed(frameIndex, DressOf(preset), excluded, cache);

    /// <summary>스킨 표로 셀 고르기: 표에 든 레이어는 그 스킨 자신의 셀(없으면 원본 없이 그리는 효과의 빈 그림), 나머지는 <see cref="DefaultCels"/>.</summary>
    private Func<Layer, int, PixelImage?> DressedCels(IReadOnlyDictionary<Layer, Skin>? skins)
    {
        if (skins is not { Count: > 0 }) return DefaultCels;
        return (layer, f) => skins.TryGetValue(layer, out var skin) && !layer.IsTilemap
            ? GetOwnCel(layer, _frames[f], skin) ?? (HasContentEffect(layer, f) ? BlankCel() : null)
            : RenderCel(layer, f);
    }

    /// <summary>
    /// 셀 하나의 순서표: 일반 레이어면 효과(자기 셀 효과)를 입힌 셀(셀 크기로 잘림 — <see cref="LayerEffects.RenderClipped"/>, 셀이 없어도 <see cref="HasContentEffect(Layer, int)"/>면 빈 그림에),
    /// 자리 있는 레이어면 셀 그대로(자기 화면에서 고치는 그림이라 효과 없이). 자기 셀이 비면 null(자식이 있어도 — 덩어리는 <see cref="PlanSubtree"/>).
    /// </summary>
    public RenderPlan? PlanCel(int layerIndex, int frameIndex)
    {
        var layer = _layers[layerIndex];
        if (RenderCel(layer, frameIndex) is not { } cel) return null;
        return ClippedCelPlan(layer, frameIndex, cel, withEffects: !layer.IsPart);
    }

    /// <summary>
    /// 자리 있는 레이어 하나만 프레임에 보이는 모습 그대로 그린 프레임 크기 순서표(자기 화면에서 효과를 볼 때 — 캔버스가 자리만큼 옮겨 그린다): 셀에 자기 셀 효과를,
    /// 자신(자식이 있으면)과 조상 중 이 프레임에 자손까지 효과가 켜진 레이어마다 이 레이어만 든 덩어리에 그 효과를 차례로(안쪽부터) 입힌다
    /// (다른 레이어는 빼므로 다른 레이어를 보는 효과는 실제 합성과 다를 수 있음). 불투명도와 숨김은 보지 않는다(고치는 레이어라 늘 다 보이게).
    /// 셀이 없으면(원본 없이 그리는 효과도 없으면) 빈 순서표. 자리 있는 레이어가 아니면 <see cref="ArgumentException"/>.
    /// </summary>
    public RenderPlan PlanPartInFrame(Layer part, int frameIndex)
    {
        if (!part.IsPart) throw new ArgumentException(DomainStrings.Project_NotPartLayer, nameof(part));
        var steps = new List<RenderStep>();
        if (RenderCel(part, frameIndex) is { } cel)
        {
            RenderStep step = CelStepOf(part, frameIndex, cel, 255, DefaultCels);
            for (var a = HasChildren(part) ? part : part.Parent; a is not null; a = a.Parent)
            {
                var effects = SubtreeEffectsOf(a);
                if (LayerEffects.AnyActive(effects, frameIndex)) step = SubtreeStepOf(a, [step], effects, frameIndex, DefaultCels, 255, nested: false);
            }
            steps.Add(step);
        }
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>
    /// 레이어 <paramref name="layer"/>의 효과가 프레임 <paramref name="frameIndex"/>에서 그림을 옮겨 쌓은 피벗 이동(px, <see cref="LayerEffects.PivotShift"/> — 피벗 같이 옮기기가 켜진
    /// 효과가 알린 거리의 합, 범위 뒤 칸에 이어 가는 끝 모습(링크된 칸, 링크되지 않은 칸의 옮긴 자리) 포함): 자기 셀 효과의 이동에, 자신(자식이 있으면)과 조상들의
    /// 자손까지 효과의 이동을 더한다(그 효과가 이 레이어가 든 덩어리를 옮기므로). 캔버스는 피벗 표시를 정한 피벗 + 이 값에 그린다.
    /// 알릴 수 있는 효과가 없으면 그리지 않고 0, 있으면 그 효과를 그리므로 그만큼 걸린다.
    /// </summary>
    public PointF EffectPivotShift(Layer layer, int frameIndex)
    {
        if ((uint)frameIndex >= (uint)_frames.Count) return PointF.Empty;
        var shift = PivotShiftOf(layer, frameIndex, subtree: false);
        for (var a = HasChildren(layer) ? layer : layer.Parent; a is not null; a = a.Parent)
        {
            var more = PivotShiftOf(a, frameIndex, subtree: true);
            shift = new PointF(shift.X + more.X, shift.Y + more.Y);
        }
        return shift;
    }

    // 레이어 효과 한쪽(자기 셀 / 자손까지)만의 피벗 이동
    private PointF PivotShiftOf(Layer layer, int frameIndex, bool subtree)
    {
        if (!LayerEffects.MayMovePivot(subtree ? SubtreeEffectsOf(layer) : OwnEffectsOf(layer))) return PointF.Empty;
        var effects = subtree ? SubtreeEffectsAt(layer, frameIndex) : EffectsAt(layer, frameIndex);
        if (!LayerEffects.MayMovePivot(effects) || (subtree ? SubtreeEffectSource(layer, frameIndex) : EffectSource(layer, frameIndex)) is not { } source) return PointF.Empty;
        return LayerEffects.PivotShift(source, effects);
    }

    /// <summary>
    /// 셀 썸네일 순서표(타임라인 칸·스킨 설정 카드가 같이 씀): 고칠 그림 그대로(효과 없이 — 효과 모습은 캔버스·미리보기에서 본다). <paramref name="skin"/>을 주면 그 레이어의 그 스킨 셀,
    /// 없으면 지금 스킨. 다만 일반 레이어 셀에 칠한 픽셀이 없는데 효과가 그림을 만들면(글씨 효과 등) 효과까지 그린다. 빈 셀이면 null.
    /// <paramref name="withChildren"/>이고 자식이 있으면(접힌 부모 칸) 자기 그림과 보이는 자손 셀을 그리는 차례대로 제자리에 합친 캔버스 크기 그림(효과 없이, 불투명도는 곱해 반영 —
    /// 스킨은 이 레이어에만, 자손은 지금 스킨). 그릴 셀이 하나도 없으면 null. 자를 자리는 <see cref="ThumbnailBounds"/>(같은 <paramref name="withChildren"/>).
    /// </summary>
    public RenderPlan? PlanCelThumbnail(int layerIndex, int frameIndex, Skin? skin = null, bool withChildren = false)
    {
        var layer = _layers[layerIndex];
        var frame = _frames[frameIndex];
        if (withChildren && HasChildren(layer))
        {
            var steps = new List<RenderStep>();
            var children = ChildMap();
            void Visit(Layer l, int alpha)
            {
                bool root = ReferenceEquals(l, layer);
                if (!root && !l.Visible) return;
                int a = (alpha * l.Opacity + 127) / 255;
                if ((root && skin is not null ? GetOwnCel(l, frame, skin) : DefaultCels(l, frameIndex)) is { } cel) steps.Add(RawStep(l, frameIndex, cel, (byte)a));
                if (children.TryGetValue(l, out var kids))
                    foreach (var k in kids) Visit(k, a);
            }
            Visit(layer, 255);
            return steps.Count == 0 ? null : new RenderPlan(_width, _height, steps);
        }
        var own = skin is null ? RenderCel(layer, frameIndex) : GetOwnCel(layer, frame, skin) ?? (HasContentEffect(layer, frameIndex) ? BlankCel() : null);
        if (own is null) return null;
        return ClippedCelPlan(layer, frameIndex, own, withEffects: !layer.IsPart && !HasContent(own));
    }

    /// <summary>
    /// 셀 썸네일 그림(<see cref="PlanCelThumbnail"/>을 <see cref="ThumbnailBounds"/>로 자른 것 — 타임라인 칸과 같은 그림·자리). 빈 셀이면 null.
    /// 바로 그리므로 UI 스레드에서는 작은 그림·적은 칸에만(타임라인은 같은 순서표를 작업 스레드에서 그린다).
    /// </summary>
    public PixelImage? RenderCelThumbnail(int layerIndex, int frameIndex, Skin? skin = null, bool withChildren = false)
    {
        if (PlanCelThumbnail(layerIndex, frameIndex, skin, withChildren) is not { } plan) return null;
        var area = ThumbnailBounds(layerIndex, withChildren);
        return (area.IsEmpty ? plan : plan.Cropped(area)).Render();
    }

    /// <summary>
    /// 효과를 끈 프레임 합성 순서표(캔버스에서 효과 보기를 끈 화면 — 고칠 그림 그대로): 보이는 레이어 셀을 그리는 차례대로 제자리에 불투명도(조상까지 곱한 값)로 겹치고
    /// 레이어 효과는 그리지 않는다. 다만 칠한 픽셀이 없는 일반 레이어 셀에 효과가 그림을 만들면(글씨 효과 등) 그 레이어는 효과까지
    /// (<see cref="PlanCelThumbnail"/>와 같은 규칙). <paramref name="layerFilter"/>가 false인 레이어는 자기 그림을 뺀다(자식은 따로 거름).
    /// </summary>
    public RenderPlan PlanFrameRaw(int frameIndex, Func<Layer, bool>? layerFilter = null)
    {
        var steps = new List<RenderStep>();
        var children = ChildMap();
        void Visit(Layer layer, int alpha)
        {
            if (!layer.Visible) return;
            int a = (alpha * layer.Opacity + 127) / 255;
            if ((layerFilter is null || layerFilter(layer)) && RenderCel(layer, frameIndex) is { } cel)
                steps.Add(!layer.IsPart && !HasContent(cel)
                    ? CelStepOf(layer, frameIndex, cel, (byte)a, DefaultCels)
                    : RawStep(layer, frameIndex, cel, (byte)a));
            if (children.TryGetValue(layer, out var kids))
                foreach (var k in kids) Visit(k, a);
        }
        foreach (var layer in _layers)
            if (layer.Parent is null) Visit(layer, 255);
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>효과 없이 셀 하나를 제자리(자리 있는 레이어면 그 자리)에 <paramref name="opacity"/>로 그리는 단계.</summary>
    private CelStep RawStep(Layer layer, int frameIndex, PixelImage cel, byte opacity)
    {
        var at = layer.IsPart ? GetPartOffset(layer, frameIndex) : Point.Empty;
        return new(layer, cel, [], frameIndex, _frames.Count, EffectPivot(layer, frameIndex), null, null, FrameTagFor([], frameIndex), opacity, at.X, at.Y);
    }

    /// <summary>
    /// 셀 <paramref name="cel"/>(레이어 <paramref name="layer"/>, 프레임 <paramref name="frameIndex"/>)을 셀 크기로 잘라 그리는 순서표: <paramref name="withEffects"/>면
    /// 그 프레임에 입혀지는 자기 셀 효과(링크된 칸의 효과 끝 모습 포함, 꺼진 효과 빼고)까지, 불투명도·위치 없이.
    /// </summary>
    private RenderPlan ClippedCelPlan(Layer layer, int frameIndex, PixelImage cel, bool withEffects)
    {
        IReadOnlyList<LayerEffect> effects = withEffects ? WithHeldEnds(layer, OwnEffectsOf(layer), frameIndex, ActiveKey(layer), DefaultCels, nested: false) : [];
        var step = new CelStep(layer, cel, effects, frameIndex, _frames.Count, EffectPivot(layer, frameIndex),
            withEffects ? SourcesOf(layer, effects, frameIndex, nested: false) : null,
            withEffects ? ScriptLayersOf(layer, frameIndex, effects, DefaultCels, nested: false) : null, FrameTagFor(effects, frameIndex), 255, 0, 0, clip: true);
        return new RenderPlan(cel.Width, cel.Height, [step]);
    }

    /// <summary>
    /// 레이어 <paramref name="layer"/>의 효과 목록 <paramref name="effects"/>(null이면 자기 셀 효과)에 든 스크립트가 프레임 <paramref name="frameIndex"/>에서 볼
    /// 배치·다른 레이어(<see cref="ScriptLayerContext"/> — 지금 프로젝트 상태를 담으므로 다른 스레드에서 그려도 된다). 다른 레이어나 <c>ctx.slice</c>를 읽는
    /// 켜진 스크립트 효과가 없으면 null. <see cref="LayerEffects.WithScriptLayers"/>로 그릴 효과 목록에 넣는다(효과 창 미리보기처럼 순서표 밖에서 그릴 때).
    /// <paramref name="subtree"/>면 자손까지 합친 그림(캔버스 크기, 캔버스 기준 배치)에 입히는 효과로 본다.
    /// </summary>
    public ScriptLayerContext? ScriptLayersAt(Layer layer, int frameIndex, IReadOnlyList<LayerEffect>? effects = null, bool subtree = false)
        => ScriptLayersOf(layer, Math.Clamp(frameIndex, 0, _frames.Count - 1), effects ?? (subtree ? SubtreeEffectsOf(layer) : OwnEffectsOf(layer)), DefaultCels,
            nested: false, subtree: subtree);

    /// <summary>
    /// 다른 레이어나 9-slice 테두리를 읽는 스크립트 효과(<see cref="LayerEffects.UsesOtherLayers"/>)가 어느 레이어에든 있는지 — 있으면 레이어 하나의 셀·효과·이름·순서나
    /// 테두리가 바뀔 때 다른 레이어의 결과도 바뀔 수 있으므로, 결과를 기억하는 쪽은 넓게 무효로 한다.
    /// </summary>
    public bool UsesOtherLayers => _layers.Any(l => LayerEffects.UsesOtherLayers(l.Effects));

    /// <summary>자손까지 효과의 피벗(캔버스 픽셀 좌표 — 덩어리 그림이 캔버스 크기라 <see cref="LayerPivotPixel"/>과 같음).</summary>
    public PointF SubtreeEffectPivot(Layer layer, int frameIndex) => LayerPivotPixel(layer, Math.Clamp(frameIndex, 0, _frames.Count - 1));

    /// <summary>
    /// 자손까지 효과의 원본 순서표(<see cref="SubtreeEffectSource"/> — 자기 그림(자기 셀 효과까지)과 보이는 자식 덩어리를 그 불투명도·효과로 합침, 이 레이어의
    /// 자손까지 효과·불투명도 없이). <paramref name="nested"/>면 다른 레이어를 효과까지 그리는 안쪽이다(겹침 규칙 — <see cref="ScriptLayerContext"/>).
    /// </summary>
    private RenderPlan SubtreeSourcePlan(Layer layer, int frameIndex, Func<Layer, int, PixelImage?>? cels = null, bool nested = false)
    {
        cels ??= DefaultCels;
        var pass = new BlockPass(frameIndex, l => l.Visible, l => cels(l, frameIndex), cels, ChildMap(), nested);
        var steps = new List<RenderStep>();
        if (cels(layer, frameIndex) is { } cel) steps.Add(CelStepOf(layer, frameIndex, cel, 255, cels, nested, OwnEffectsOf(layer)));
        if (pass.Children.TryGetValue(layer, out var kids))
            foreach (var kid in kids) EmitBlock(steps, kid, pass);
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>그릴 셀(<see cref="RenderCel"/> — 지금 보이는 셀, 없으면 원본 없이 그리는 효과가 켜진 일반 레이어의 빈 그림).</summary>
    private PixelImage? DefaultCels(Layer layer, int frameIndex) => RenderCel(layer, frameIndex);

    /// <summary>
    /// 셀 하나를 자기 셀 효과(<paramref name="own"/>, null = <see cref="OwnEffectsOf"/>)까지 입혀 (자리 있는 레이어면 그 자리)에 합성하는 단계.
    /// <paramref name="cels"/>는 스크립트가 보는 다른 레이어·다른 프레임 셀, <paramref name="nested"/>면 다른 레이어를 효과까지 그리는 안쪽이라 다른 레이어를 효과까지 읽는 효과를 건너뛴다(겹침 규칙).
    /// </summary>
    private CelStep CelStepOf(Layer layer, int frameIndex, PixelImage cel, byte opacity, Func<Layer, int, PixelImage?> cels, bool nested = false,
        IReadOnlyList<LayerEffect>? own = null)
    {
        own ??= OwnEffectsOf(layer);
        var effects = WithHeldEnds(layer, nested ? LayerEffects.WithoutRenderedLayerReaders(own) : own, frameIndex, ActiveKey(layer), cels, nested);
        var at = layer.IsPart ? GetPartOffset(layer, frameIndex) : Point.Empty;
        return new CelStep(layer, cel, effects, frameIndex, _frames.Count, EffectPivot(layer, frameIndex), SourcesOf(layer, effects, frameIndex, nested),
            ScriptLayersOf(layer, frameIndex, effects, cels, nested), FrameTagFor(effects, frameIndex), opacity, at.X, at.Y);
    }

    /// <summary>
    /// 잔상이 볼 다른 프레임 원본을 담는다(자기 셀 효과는 <see cref="EffectFrameSource"/>와 같음 — 지금 보이는 셀, <paramref name="subtree"/>면 덩어리 원본). 잔상이 없으면 null.
    /// 덩어리 원본은 이 프레임의 잔상이 실제로 볼 프레임만 합성 순서표를 만든다.
    /// </summary>
    private FrameSources? SourcesOf(Layer layer, IReadOnlyList<LayerEffect> effects, int frameIndex, bool nested, bool subtree = false)
    {
        if (!LayerEffects.UsesOtherFrames(effects)) return null;
        int n = _frames.Count;
        var pivots = new PointF[n];
        for (int f = 0; f < n; f++) pivots[f] = subtree ? SubtreeEffectPivot(layer, f) : EffectPivot(layer, f);
        if (!subtree)
        {
            var cels = new PixelImage?[n];
            for (int f = 0; f < n; f++) cels[f] = RenderCel(layer, f);
            return new FrameSources(layer, cels, null, pivots);
        }
        var plans = new RenderPlan?[n];
        // 다른 프레임을 쓰는 스크립트 효과는 어느 프레임을 볼지 모르므로 모든 프레임의 원본을 준비한다
        if (effects.Any(e => e is ScriptEffect { Enabled: true } && e.AppliesTo(frameIndex) && EffectRegistry.Of(e)!.UsesOtherFrames(e)))
            for (int f = 0; f < n; f++) plans[f] = SubtreeSourcePlan(layer, f, null, nested);
        foreach (var effect in effects)
        {
            if (effect is not AfterimageEffect { Enabled: true } a || !a.AppliesTo(frameIndex)) continue;
            var (from, to) = a.Frames is { } r ? (r.From, r.To) : (0, Math.Max(0, n - 1));
            int step = Math.Clamp(frameIndex - from, 0, to - from);
            for (int k = 1; k <= a.Count; k++)
            {
                int back = k * a.Interval;
                if (back > step) break;
                int f = from + step - back;
                if ((uint)f < (uint)n) plans[f] ??= SubtreeSourcePlan(layer, f, null, nested);
            }
        }
        return new FrameSources(layer, null, plans, pivots);
    }

    /// <summary>
    /// 레이어 <paramref name="layer"/>의 효과 <paramref name="effects"/>에 든 스크립트가 프레임 <paramref name="frameIndex"/>에서 볼 배치·다른 레이어를 담는다
    /// (<see cref="ScriptLayerContext"/> — 고른 레이어의 셀은 <paramref name="cels"/>로 고른다). 다른 레이어나 <c>ctx.slice</c>를 읽는 켜진 스크립트 효과가 없으면 null,
    /// 다른 레이어 함수를 쓰는 효과가 없으면 테두리만, 효과까지 읽는 효과가 없거나 <paramref name="nested"/>면 고른 레이어의 효과 그리기 단계는 만들지 않는다.
    /// 레이어는 그 효과들의 레이어 고르기 설정(<c>type: "layer"</c>)에 적은 이름만 담는다(<see cref="ChosenLayerNames"/>).
    /// <paramref name="subtree"/>면 덩어리 그림(캔버스 크기, 캔버스 테두리·원점 (0, 0))에 입히는 효과다.
    /// </summary>
    private ScriptLayerContext? ScriptLayersOf(Layer layer, int frameIndex, IReadOnlyList<LayerEffect> effects, Func<Layer, int, PixelImage?> cels, bool nested,
        bool subtree = false)
    {
        bool any = false, rendered = false;
        HashSet<string>? chosen = null;
        foreach (var e in effects)
        {
            if (e is not ScriptEffect { Enabled: true } script || !ScriptDefinition.UsesOtherLayers(script)) continue;
            any = true;
            if (!ScriptDefinition.ReadsOtherLayers(script)) continue;
            rendered |= !nested && ScriptDefinition.ReadsRenderedLayers(script);
            foreach (string name in ChosenLayerNames(script)) (chosen ??= new(StringComparer.Ordinal)).Add(name);
        }
        if (!any) return null;
        bool own = layer.IsPart && !subtree;
        var (border, size) = own ? (layer.PartBorder, layer.PartSize) : (Sprite.Border, Size);
        SpriteBorder? slice = NineSlice.Applies(border, size.Width, size.Height) ? border : null;
        var origin = own ? GetPartOffset(layer, frameIndex) : Point.Empty;
        var named = new Dictionary<string, LayerNeighbor>(StringComparer.Ordinal);
        var effectNames = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (chosen is not null)
        {
            foreach (var (name, found) in NearestLayers(layer, chosen))
            {
                effectNames[name] = ActiveEffectNames(found, frameIndex);
                if (!(nested && HasChildren(found))) named[name] = NeighborOf(found, frameIndex, cels, rendered);
            }
        }
        return new ScriptLayerContext(slice, origin, named, effectNames);
    }

    /// <summary>스크립트 효과의 레이어 고르기 설정(<c>type: "layer"</c>)에 적은 이름(비지 않은 것만, schema에 맞춘 값). 실행할 수 없는 스크립트면 없음.</summary>
    private static IEnumerable<string> ChosenLayerNames(ScriptEffect script)
    {
        if (Scripting.ScriptLibrary.Runnable(script.ScriptHash) is not { } program || !program.Settings.Any(s => s.Type == Scripting.ScriptSettingType.Layer))
            yield break;
        var values = program.NormalizeSettings(script.Settings);
        foreach (var setting in program.Settings)
            if (setting.Type == Scripting.ScriptSettingType.Layer && values[setting.Key] is string { Length: > 0 } name)
                yield return name;
    }

    /// <summary>
    /// 이름 <paramref name="names"/>마다 레이어 목록에서 가장 가까운 레이어 하나(같은 거리면 아래쪽 — 먼저 만난 것). 없는 이름은 빠진다.
    /// <paramref name="layer"/> 자신과 그 조상은 찾지 않는다(자기 그림은 img, 조상 덩어리 그림에는 자기가 들어 있음).
    /// </summary>
    private Dictionary<string, Layer> NearestLayers(Layer layer, IReadOnlySet<string> names)
    {
        int me = IndexOfReference(_layers, layer);
        var nearest = new Dictionary<string, (int Distance, Layer Layer)>(StringComparer.Ordinal);
        for (int i = 0; i < _layers.Count; i++)
        {
            var candidate = _layers[i];
            if (ReferenceEquals(candidate, layer) || IsAncestorOf(candidate, layer) || !names.Contains(candidate.Name)) continue;
            int distance = Math.Abs(i - me);
            if (!nearest.TryGetValue(candidate.Name, out var known) || distance < known.Distance) nearest[candidate.Name] = (distance, candidate);
        }
        return nearest.ToDictionary(p => p.Key, p => p.Value.Layer, StringComparer.Ordinal);
    }

    /// <summary>
    /// 스크립트가 보는 이웃 레이어 하나: 효과 없는 원본과(<paramref name="withEffects"/>면) 효과까지 그리는 단계(불투명도 255, 겹침 규칙 적용).
    /// 자식이 있는 레이어는 덩어리(자기 그림 + 보이는 자손) 그림이다.
    /// </summary>
    private LayerNeighbor NeighborOf(Layer neighbor, int frameIndex, Func<Layer, int, PixelImage?> cels, bool withEffects)
    {
        if (HasChildren(neighbor))
        {
            var source = SubtreeSourcePlan(neighbor, frameIndex, cels, nested: true);
            RenderStep? step = null;
            if (withEffects)
            {
                var list = new List<RenderStep>();
                var pass = new BlockPass(frameIndex, l => ReferenceEquals(l, neighbor) || l.Visible, l => cels(l, frameIndex), cels, ChildMap(), true);
                EmitBlock(list, neighbor, pass, forceComposite: true, opacity: 255);
                step = list.Count > 0 ? list[0] : null;
            }
            return new LayerNeighbor(neighbor, frameIndex, null, Point.Empty, source, step);
        }
        var cel = cels(neighbor, frameIndex);
        var rendered = withEffects && cel is not null ? CelStepOf(neighbor, frameIndex, cel, 255, cels, nested: true) : null;
        return new LayerNeighbor(neighbor, frameIndex, cel, neighbor.IsPart ? GetPartOffset(neighbor, frameIndex) : Point.Empty, null, rendered);
    }

    /// <summary>레이어 효과 중 프레임 <paramref name="frameIndex"/>에 켜진(켜져 있고 범위 안, 아는 효과) 것의 화면 이름(목록 순서).</summary>
    private static string[] ActiveEffectNames(Layer layer, int frameIndex)
        => [.. layer.Effects.Where(e => e.Enabled && e.AppliesTo(frameIndex) && EffectRegistry.Of(e) is not null).Select(e => e.DisplayName)];

    private static int IndexOfReference(IReadOnlyList<Layer> layers, Layer layer)
    {
        for (int i = 0; i < layers.Count; i++)
            if (ReferenceEquals(layers[i], layer)) return i;
        return -1;
    }
}
