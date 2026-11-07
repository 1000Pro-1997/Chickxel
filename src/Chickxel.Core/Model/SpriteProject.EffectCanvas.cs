using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 효과 결과에 맞춰 캔버스 늘리기: 효과가 캔버스 밖으로 그리는 범위를 재고, 고른 방식으로 캔버스를 늘린다(AI 도구·효과 하나의 '캔버스 늘리기…').
public sealed partial class SpriteProject
{
    /// <summary>
    /// 레이어 <paramref name="layer"/>의 효과 결과가 모든 프레임에서 놓이는 범위(캔버스 좌표, 효과를 그린 불투명 픽셀만 — 지금 캔버스는 넣지 않음).
    /// <paramref name="throughEffect"/>가 있으면 그 번호까지의 효과만 쓰고(뒤 효과는 빼고, 그 효과가 앞 효과 결과 위에 그리므로 앞은 둠), 없으면 모든 효과.
    /// <paramref name="expandingOnly"/>면 캔버스 늘리기를 끈 효과(<see cref="LayerEffect.ExpandCanvas"/>)는 끈 셈으로 잰다(효과 창 확인과 같음).
    /// 켜진 효과가 어느 프레임에도 없으면 null, 켜진 효과는 있지만 그린 픽셀이 없으면 <see cref="Rectangle.Empty"/>.
    /// </summary>
    public Rectangle? EffectOutputBounds(Layer layer, int? throughEffect = null, bool expandingOnly = true)
    {
        IReadOnlyList<LayerEffect> list = throughEffect is { } last ? [.. layer.Effects.Take(last + 1)] : layer.Effects;
        bool any = false;
        var bounds = Rectangle.Empty;
        for (int frame = 0; frame < _frames.Count; frame++)
        {
            if (EffectSource(layer, frame) is not { } source) continue;
            var effects = EffectsAt(layer, list, frame, ActiveKey(layer), null);
            if (expandingOnly) effects = LayerEffects.ExpandingOnly(effects);
            if (!LayerEffects.AnyEnabled(effects)) continue;
            any = true;
            var (rendered, dx, dy) = LayerEffects.Render(source, effects);
            var opaque = rendered.GetOpaqueBounds();
            if (opaque.IsEmpty) continue;
            var at = layer.IsPart ? GetPartOffset(layer, frame) : Point.Empty;
            opaque.Offset(at.X + dx, at.Y + dy);
            bounds = bounds.IsEmpty ? opaque : Rectangle.Union(bounds, opaque);
        }
        return any ? bounds : null;
    }

    /// <summary>
    /// 효과 범위 <paramref name="effectBounds"/>(캔버스 좌표)가 들어가도록 <paramref name="mode"/>로 늘린 캔버스 계획(그림 크기는 그대로). 이미 들어가면
    /// <see cref="EffectCanvasExpansion.IsNone"/>인 계획. 너무 커지면 <see cref="ArgumentOutOfRangeException"/>·<see cref="InvalidOperationException"/>(<see cref="EffectCanvasExpansion.FromBounds"/>).
    /// </summary>
    public EffectCanvasExpansion PlanEffectCanvas(Rectangle effectBounds, EffectCanvasExpandMode mode)
        => EffectCanvasExpansion.FromBounds(_width, _height, Rectangle.Union(new Rectangle(0, 0, _width, _height), effectBounds), mode);

    /// <summary>계획 <paramref name="plan"/>대로 캔버스를 늘린다(그림 크기는 그대로, 모든 레이어·부위·피벗이 함께 옮겨짐 — 실행취소 한 단계 <paramref name="label"/>). 바뀔 것이 없으면 false.</summary>
    public bool ApplyEffectCanvas(EffectCanvasExpansion plan, string? label = null)
    {
        int width = _width, height = _height;
        if (plan.IsNone(width, height)) return false;
        Edit(label ?? DomainStrings.Project_ExpandCanvasForEffect, ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Layers | ProjectChange.Settings,
            () => ResizeCanvasAndScale(plan.Width, plan.Height, new Rectangle(plan.OffsetX, plan.OffsetY, width, height)));
        return true;
    }
}
