using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 합성 순서표(RenderPlan) 만들기: 지금 프로젝트 상태를 담아 다른 스레드에서도 그릴 수 있게 한다.
// 동기 합성(Flatten·FlattenInto·FlattenGroup·FlattenOutfit·EffectSource)도 모두 이 순서표로 그린다.
// 셀을 고르는 함수(cels: 레이어, 프레임 번호 → 셀)는 스크립트 효과가 보는 다른 레이어(레이어 고르기 설정으로 고른 것)·다른 프레임 원본에도 같이 쓴다(보이기·걸러내기와 관계없이 그 레이어의 셀).
public sealed partial class SpriteProject
{
    /// <summary>
    /// 프레임 합성 순서표(<see cref="FlattenInto"/>와 같은 결과). <paramref name="layerFilter"/>·<paramref name="celMap"/>은 만드는 동안 부른다.
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
        var steps = new List<RenderStep>();
        foreach (var layer in _layers)
        {
            if (layer.IsGroup)
            {
                if (layer.HasActiveEffectsAt(frameIndex) && (includeHidden || layer.Visible)
                    && GroupStepOf(layer, frameIndex, part => includeHidden || part.Visible ? CelOf(part) : null, cels, layer.Opacity) is { } group)
                    steps.Add(group);
                continue;
            }
            if (layer.Parent is { IsGroup: true } pg && pg.HasActiveEffectsAt(frameIndex)) continue; // 그룹 차례에 함께 그린다
            if (!includeHidden && !(layer.Visible && (layer.Parent?.Visible ?? true))) continue;
            if (CelOf(layer) is { } cel) steps.Add(CelStepOf(layer, frameIndex, cel, DrawOpacity(layer), cels));
        }
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>그룹 하나의 보이는 부위 합성 순서표(<see cref="FlattenGroup"/>와 같은 결과).</summary>
    public RenderPlan PlanGroup(Layer group, int frameIndex, Skin? skin = null)
    {
        if (!group.IsGroup) throw new ArgumentException(DomainStrings.Project_NotGroupLayer, nameof(group));
        Func<Layer, int, PixelImage?> cels = skin is null ? DefaultCels : (layer, f) => GetSkinCel(layer, _frames[f], skin);
        PixelImage? CelOf(Layer part) => part.Visible ? cels(part, frameIndex) : null;
        var steps = new List<RenderStep>();
        if (group.HasActiveEffectsAt(frameIndex))
        {
            if (GroupStepOf(group, frameIndex, CelOf, cels, group.Opacity) is { } step) steps.Add(step);
        }
        else
        {
            foreach (var part in ChildrenOf(group))
                if (CelOf(part) is { } cel) steps.Add(CelStepOf(part, frameIndex, cel, DrawOpacity(part), cels));
        }
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>
    /// 셀 하나의 썸네일 순서표: 그룹이면 그 그룹 합성(그릴 부위 셀이 하나도 없으면 null), 일반 레이어면 효과를 입힌 셀(셀 크기로 잘림 —
    /// <see cref="LayerEffects.RenderClipped"/>, 셀이 없어도 <see cref="HasContentEffect(Layer, int)"/>면 빈 그림에), 부위면 셀 그대로(부위 화면에서 고치는 그림이라 효과 없이). 빈 셀이면 null.
    /// </summary>
    public RenderPlan? PlanCel(int layerIndex, int frameIndex)
    {
        var layer = _layers[layerIndex];
        var frame = _frames[frameIndex];
        if (layer.IsGroup)
            return ChildrenOf(layer).Any(c => GetCel(c, frame) is not null) ? PlanGroup(layer, frameIndex) : null;
        if (RenderCel(layer, frameIndex) is not { } cel) return null;
        IReadOnlyList<LayerEffect> effects = layer.IsPart ? [] : layer.Effects;
        var step = new CelStep(layer, cel, effects, frameIndex, _frames.Count, EffectPivot(layer, frameIndex),
            layer.IsPart ? null : SourcesOf(layer, effects, frameIndex, nested: false),
            layer.IsPart ? null : ScriptLayersOf(layer, frameIndex, effects, DefaultCels, nested: false), 255, 0, 0, clip: true);
        return new RenderPlan(cel.Width, cel.Height, [step]);
    }

    /// <summary>
    /// 레이어 <paramref name="layer"/>의 효과 목록 <paramref name="effects"/>(null이면 지금 효과)에 든 스크립트가 프레임 <paramref name="frameIndex"/>에서 볼
    /// 배치·다른 레이어(<see cref="ScriptLayerContext"/> — 지금 프로젝트 상태를 담으므로 다른 스레드에서 그려도 된다). 다른 레이어나 <c>ctx.slice</c>를 읽는
    /// 켜진 스크립트 효과가 없으면 null. <see cref="LayerEffects.WithScriptLayers"/>로 그릴 효과 목록에 넣는다(효과 창 미리보기처럼 순서표 밖에서 그릴 때).
    /// </summary>
    public ScriptLayerContext? ScriptLayersAt(Layer layer, int frameIndex, IReadOnlyList<LayerEffect>? effects = null)
        => ScriptLayersOf(layer, Math.Clamp(frameIndex, 0, _frames.Count - 1), effects ?? layer.Effects, DefaultCels, nested: false);

    /// <summary>
    /// 다른 레이어나 9-slice 테두리를 읽는 스크립트 효과(<see cref="LayerEffects.UsesOtherLayers"/>)가 어느 레이어에든 있는지 — 있으면 레이어 하나의 셀·효과·이름·순서나
    /// 테두리가 바뀔 때 다른 레이어의 결과도 바뀔 수 있으므로, 결과를 기억하는 쪽은 넓게 무효로 한다.
    /// </summary>
    public bool UsesOtherLayers => _layers.Any(l => LayerEffects.UsesOtherLayers(l.Effects));

    /// <summary>
    /// 그룹 효과의 원본 순서표(효과 없이, 보이는 부위를 부위 불투명도·부위 효과로 합침 — <see cref="EffectSource"/>).
    /// <paramref name="nested"/>면 다른 레이어를 효과까지 그리는 안쪽이다(겹침 규칙 — <see cref="ScriptLayerContext"/>).
    /// </summary>
    private RenderPlan GroupSourcePlan(Layer group, int frameIndex, Func<Layer, int, PixelImage?>? cels = null, bool nested = false)
    {
        cels ??= DefaultCels;
        var steps = new List<RenderStep>();
        foreach (var part in ChildrenOf(group))
        {
            if (!part.Visible || cels(part, frameIndex) is not { } cel) continue;
            steps.Add(CelStepOf(part, frameIndex, cel, part.Opacity, cels, nested));
        }
        return new RenderPlan(_width, _height, steps);
    }

    /// <summary>그릴 셀(<see cref="RenderCel"/> — 지금 보이는 셀, 없으면 원본 없이 그리는 효과가 켜진 일반 레이어의 빈 그림).</summary>
    private PixelImage? DefaultCels(Layer layer, int frameIndex) => RenderCel(layer, frameIndex);

    /// <summary>합성할 때의 불투명도: 부위면 그룹 × 부위.</summary>
    private static byte DrawOpacity(Layer layer)
        => layer.IsPart ? (byte)((layer.Opacity * (layer.Parent?.Opacity ?? 255) + 127) / 255) : layer.Opacity;

    /// <summary>
    /// 셀 하나를 자기 효과까지 입혀 (부위면 부위 위치)에 합성하는 단계. <paramref name="cels"/>는 스크립트가 보는 다른 레이어·다른 프레임 셀,
    /// <paramref name="nested"/>면 다른 레이어를 효과까지 그리는 안쪽이라 다른 레이어를 효과까지 읽는 효과를 건너뛴다(겹침 규칙).
    /// </summary>
    private CelStep CelStepOf(Layer layer, int frameIndex, PixelImage cel, byte opacity, Func<Layer, int, PixelImage?> cels, bool nested = false)
    {
        var effects = nested ? LayerEffects.WithoutRenderedLayerReaders(layer.Effects) : layer.Effects;
        int x = layer.IsPart ? layer.PartOffset.X : 0, y = layer.IsPart ? layer.PartOffset.Y : 0;
        return new CelStep(layer, cel, effects, frameIndex, _frames.Count, EffectPivot(layer, frameIndex), SourcesOf(layer, effects, frameIndex, nested),
            ScriptLayersOf(layer, frameIndex, effects, cels, nested), opacity, x, y);
    }

    /// <summary>이 프레임에 효과가 켜진 그룹 단계(그릴 부위 셀이 없으면 null). <paramref name="celOf"/>는 그릴 부위 셀(보이기·걸러내기 반영).</summary>
    private GroupStep? GroupStepOf(Layer group, int frameIndex, Func<Layer, PixelImage?> celOf, Func<Layer, int, PixelImage?> cels, byte opacity,
        bool nested = false)
    {
        var parts = new List<CelStep>();
        foreach (var part in ChildrenOf(group))
            if (celOf(part) is { } cel) parts.Add(CelStepOf(part, frameIndex, cel, part.Opacity, cels, nested));
        if (parts.Count == 0) return null;
        var effects = nested ? LayerEffects.WithoutRenderedLayerReaders(group.Effects) : group.Effects;
        return new GroupStep(group, parts, effects, frameIndex, _frames.Count, EffectPivot(group, frameIndex), SourcesOf(group, effects, frameIndex, nested),
            ScriptLayersOf(group, frameIndex, effects, cels, nested), opacity, _width, _height);
    }

    /// <summary>
    /// 잔상이 볼 다른 프레임 원본(<see cref="EffectFrameSource"/>와 같음 — 지금 보이는 셀)을 담는다. 잔상이 없으면 null.
    /// 그룹은 이 프레임의 잔상이 실제로 볼 프레임만 부위 합성 순서표를 만든다.
    /// </summary>
    private FrameSources? SourcesOf(Layer layer, IReadOnlyList<LayerEffect> effects, int frameIndex, bool nested)
    {
        if (!LayerEffects.UsesOtherFrames(effects)) return null;
        int n = _frames.Count;
        var pivots = new PointF[n];
        for (int f = 0; f < n; f++) pivots[f] = EffectPivot(layer, f);
        if (!layer.IsGroup)
        {
            var cels = new PixelImage?[n];
            for (int f = 0; f < n; f++) cels[f] = RenderCel(layer, f);
            return new FrameSources(layer, cels, null, pivots);
        }
        var plans = new RenderPlan?[n];
        // 다른 프레임을 쓰는 스크립트 효과는 어느 프레임을 볼지 모르므로 모든 프레임의 원본을 준비한다
        if (effects.Any(e => e is ScriptEffect { Enabled: true } && e.AppliesTo(frameIndex) && EffectRegistry.Of(e)!.UsesOtherFrames(e)))
            for (int f = 0; f < n; f++) plans[f] = GroupSourcePlan(layer, f, null, nested);
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
                if ((uint)f < (uint)n) plans[f] ??= GroupSourcePlan(layer, f, null, nested);
            }
        }
        return new FrameSources(layer, null, plans, pivots);
    }

    /// <summary>
    /// 레이어 <paramref name="layer"/>의 효과 <paramref name="effects"/>에 든 스크립트가 프레임 <paramref name="frameIndex"/>에서 볼 배치·다른 레이어를 담는다
    /// (<see cref="ScriptLayerContext"/> — 고른 레이어의 셀은 <paramref name="cels"/>로 고른다). 다른 레이어나 <c>ctx.slice</c>를 읽는 켜진 스크립트 효과가 없으면 null,
    /// 다른 레이어 함수를 쓰는 효과가 없으면 테두리만, 효과까지 읽는 효과가 없거나 <paramref name="nested"/>면 고른 레이어의 효과 그리기 단계는 만들지 않는다.
    /// 레이어는 그 효과들의 레이어 고르기 설정(<c>type: "layer"</c>)에 적은 이름만 담는다(<see cref="ChosenLayerNames"/>).
    /// </summary>
    private ScriptLayerContext? ScriptLayersOf(Layer layer, int frameIndex, IReadOnlyList<LayerEffect> effects, Func<Layer, int, PixelImage?> cels, bool nested)
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
        var (border, size) = layer.IsPart ? (layer.PartBorder, layer.PartSize) : (Sprite.Border, Size);
        SpriteBorder? slice = NineSlice.Applies(border, size.Width, size.Height) ? border : null;
        var origin = layer.IsPart ? layer.PartOffset : Point.Empty;
        var named = new Dictionary<string, LayerNeighbor>(StringComparer.Ordinal);
        var effectNames = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (chosen is not null)
        {
            foreach (var (name, found) in NearestLayers(layer, chosen))
            {
                effectNames[name] = ActiveEffectNames(found, frameIndex);
                if (!(nested && found.IsGroup)) named[name] = NeighborOf(found, frameIndex, cels, rendered);
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
    /// <paramref name="layer"/> 자신과 그 레이어를 담은 그룹은 찾지 않는다(자기 그림은 img, 담은 그룹 그림에는 자기가 들어 있음).
    /// </summary>
    private Dictionary<string, Layer> NearestLayers(Layer layer, IReadOnlySet<string> names)
    {
        int me = IndexOfReference(_layers, layer);
        var nearest = new Dictionary<string, (int Distance, Layer Layer)>(StringComparer.Ordinal);
        for (int i = 0; i < _layers.Count; i++)
        {
            var candidate = _layers[i];
            if (ReferenceEquals(candidate, layer) || ReferenceEquals(candidate, layer.Parent) || !names.Contains(candidate.Name)) continue;
            int distance = Math.Abs(i - me);
            if (!nearest.TryGetValue(candidate.Name, out var known) || distance < known.Distance) nearest[candidate.Name] = (distance, candidate);
        }
        return nearest.ToDictionary(p => p.Key, p => p.Value.Layer, StringComparer.Ordinal);
    }

    /// <summary>스크립트가 보는 이웃 레이어 하나: 효과 없는 원본과(<paramref name="withEffects"/>면) 효과까지 그리는 단계(불투명도 255, 겹침 규칙 적용).</summary>
    private LayerNeighbor NeighborOf(Layer neighbor, int frameIndex, Func<Layer, int, PixelImage?> cels, bool withEffects)
    {
        if (neighbor.IsGroup)
        {
            var source = GroupSourcePlan(neighbor, frameIndex, cels, nested: true);
            var step = withEffects ? GroupStepOf(neighbor, frameIndex, part => part.Visible ? cels(part, frameIndex) : null, cels, 255, nested: true) : null;
            return new LayerNeighbor(neighbor, frameIndex, null, Point.Empty, source, step);
        }
        var cel = cels(neighbor, frameIndex);
        var rendered = withEffects && cel is not null ? CelStepOf(neighbor, frameIndex, cel, 255, cels, nested: true) : null;
        return new LayerNeighbor(neighbor, frameIndex, cel, neighbor.IsPart ? neighbor.PartOffset : Point.Empty, null, rendered);
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
