using System.Drawing;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 렌더 함수의 <c>ctx</c>에 넘길 프레임 정보. 기본 효과가 <see cref="AnimatedEffect"/>에서 받는 값과 같다:
/// <c>ctx.t</c> = <see cref="Time"/>, <c>ctx.progress</c> = <see cref="Progress"/>, <c>ctx.frame</c> = <see cref="FrameIndex"/>,
/// <c>ctx.step</c> = <see cref="FrameStep"/>, <c>ctx.span</c> = <see cref="FrameSpan"/>, <c>ctx.pivot</c> = <see cref="Pivot"/>(그림 좌표로 바꿔서),
/// <c>ctx.frameSource(i)</c> = <see cref="FrameSource"/>, <c>ctx.renderedFrameSource(i)</c> = <see cref="RenderedFrameSource"/>, <c>ctx.slice</c> = <see cref="Slice"/>,
/// 다른 레이어 함수(<c>ctx.layerBelow</c> 등) = <see cref="Layers"/>, <c>ctx.color</c> 등의 그라데이션 = <see cref="Gradients"/>·<see cref="GradientTime"/>.
/// </summary>
public sealed record ScriptFrameContext
{
    /// <summary>움직이지 않는 효과나 첫 프레임: 진행 0, 피벗은 원본 가운데, 다른 프레임 없음.</summary>
    public static ScriptFrameContext Still { get; } = new();

    /// <summary>범위 안 진행(0~1, 반복과 빠르기 곡선 전).</summary>
    public double Time { get; init; }

    /// <summary>반복을 넣고 빠르기 곡선은 넣기 전 진행(0~1, <see cref="AnimatedEffect.Phase"/>) — <c>ctx.phase</c>.</summary>
    public double Phase { get; init; }

    /// <summary>반복과 빠르기 곡선을 넣은 진행(0~1).</summary>
    public double Progress { get; init; }

    /// <summary>그리는 프레임 번호.</summary>
    public int FrameIndex { get; init; }

    /// <summary>범위 첫 프레임부터 몇 프레임째인지(0부터).</summary>
    public int FrameStep { get; init; }

    /// <summary>범위 길이 − 1.</summary>
    public int FrameSpan { get; init; }

    /// <summary>피벗(효과 원본 왼쪽 위 기준 px). null이면 원본 가운데.</summary>
    public PointF? Pivot { get; init; }

    /// <summary>프레임 번호 → 그 프레임의 효과 원본(효과 없이, 원본과 같은 크기, 빈 셀이면 null). null이면 다른 프레임을 주지 않는다.</summary>
    public Func<int, PixelImage?>? FrameSource { get; init; }

    /// <summary>효과 원본의 9-slice 테두리(px, 원본 가장자리부터). 없거나 맞지 않으면(<see cref="Edit.NineSlice.Applies"/>) null.</summary>
    public SpriteBorder? Slice { get; init; }

    /// <summary>다른 레이어(<c>ctx.layerBelow</c>·<c>ctx.layerEffectsAt</c> 등). null이면 프로젝트 밖 그리기 — 그림 함수는 null, 이름 함수는 빈 배열.</summary>
    public ScriptLayerContext? Layers { get; init; }

    /// <summary>
    /// 그릴 때만: 프레임 번호와 그 프레임 원본(<see cref="FrameSource"/>) → 그 프레임에 이 레이어의 다른(미루지 않는) 효과를 입힌 그림과 원본 대비 위치
    /// (<see cref="AnimatedEffect.RenderedFrameSource"/>). 미루는 스크립트(<c>deferred: true</c>)에만 채운다. null이면 원본 그대로(위치 0, 0)를 준다.
    /// </summary>
    public Func<int, PixelImage, (PixelImage Image, int OffsetX, int OffsetY)>? RenderedFrameSource { get; init; }

    /// <summary>색 설정 슬롯별 그라데이션(<see cref="LayerEffect.ColorGradients"/>, 키는 <see cref="ScriptColorSlots"/>). 없으면 빈 표(모두 단색).</summary>
    public IReadOnlyDictionary<string, EffectColorGradient> Gradients { get; init; } = System.Collections.Immutable.ImmutableDictionary<string, EffectColorGradient>.Empty;

    /// <summary>
    /// 움직이는 그라데이션의 시간: 효과의 범위 안 진행(<see cref="AnimatedEffect.Time"/> — 효과의 반복·곡선 전, 움직이지 않는 스크립트도 같음).
    /// 그라데이션 자신의 곡선·반복은 샘플러가 넣는다(<see cref="Edit.EffectGradientSampler"/>).
    /// </summary>
    public double GradientTime { get; init; }

    /// <summary>
    /// 효과 만들기 미리보기·검증의 <paramref name="step"/>번째 칸 진행(올릴 때 미리보기의 그 칸과 같음): <paramref name="steps"/>칸(선언의 <c>previewFrames</c>)에 고르게 나눈 선형 진행.
    /// </summary>
    public static ScriptFrameContext PreviewStep(int step, int steps)
    {
        int span = steps - 1;
        double t = span == 0 ? 0 : step / (double)span;
        return new ScriptFrameContext { Time = t, Progress = t, FrameIndex = step, FrameStep = step, FrameSpan = span };
    }

    /// <summary>그릴 때 채운 <paramref name="effect"/> 사본(<see cref="AnimatedEffect.AtFrame"/>)의 프레임 정보.</summary>
    public static ScriptFrameContext From(AnimatedEffect effect) => new()
    {
        Time = effect.Time,
        Phase = effect.Phase,
        Progress = effect.Progress,
        FrameIndex = effect.FrameIndex,
        FrameStep = effect.FrameStep,
        FrameSpan = effect.FrameSpan,
        Pivot = effect.PivotPoint,
        FrameSource = effect.FrameSource,
        RenderedFrameSource = effect.RenderedFrameSource,
        Gradients = effect.ColorGradients,
        GradientTime = effect.Time,
        Slice = (effect as ScriptEffect)?.Layers?.Slice,
        Layers = (effect as ScriptEffect)?.Layers,
    };
}
