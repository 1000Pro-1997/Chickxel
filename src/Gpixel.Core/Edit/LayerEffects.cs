using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>효과가 그림 밖으로 넓히는 여백(px, 왼쪽·위·오른쪽·아래).</summary>
public readonly record struct EffectPadding(int Left, int Top, int Right, int Bottom)
{
    public bool IsNone => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}

/// <summary>효과가 캔버스 밖으로 나간 부분을 담도록 캔버스를 늘리는 방식.</summary>
public enum EffectCanvasExpandMode
{
    /// <summary>필요한 방향만 필요한 픽셀 수만큼 늘린다.</summary>
    Minimum,
    /// <summary>필요한 크기를 담는 범위에서 원래 캔버스 가로세로 비율을 유지한다.</summary>
    KeepRatio,
    /// <summary>원래 캔버스의 중심을 유지하며 사방에 같은 여백을 더한다.</summary>
    KeepCenter,
}

/// <summary>효과를 담을 새 캔버스 크기와 그 안에서 원래 캔버스 왼쪽 위가 놓일 자리.</summary>
public readonly record struct EffectCanvasExpansion(int Width, int Height, int OffsetX, int OffsetY, bool RatioPreserved)
{
    public bool IsNone(int oldWidth, int oldHeight)
        => Width == oldWidth && Height == oldHeight && OffsetX == 0 && OffsetY == 0;

    /// <summary>
    /// 원래 <paramref name="width"/>×<paramref name="height"/> 캔버스와 효과 결과 <paramref name="effectBounds"/>를 모두 담는 가장 작은 확대.
    /// 비율 유지 결과가 이미지 최대 크기를 넘으면 효과를 자르지 않는 최소 확대를 돌려주고 <see cref="RatioPreserved"/>는 false다.
    /// </summary>
    public static EffectCanvasExpansion FromBounds(int width, int height, Rectangle effectBounds, EffectCanvasExpandMode mode)
    {
        if (width < 1 || height < 1 || width > PixelImage.MaxSize || height > PixelImage.MaxSize)
            throw new ArgumentOutOfRangeException(nameof(width), Gpixel.Core.Localization.DomainStrings.FormatCanvas_InvalidSize(max: PixelImage.MaxSize, width: width, height: height));
        var required = effectBounds.IsEmpty
            ? new Rectangle(0, 0, width, height)
            : Rectangle.Union(new Rectangle(0, 0, width, height), effectBounds);
        long left = Math.Max(0L, -(long)required.Left), top = Math.Max(0L, -(long)required.Top);
        long right = Math.Max(0L, (long)required.Right - width), bottom = Math.Max(0L, (long)required.Bottom - height);
        long minimumWidth = width + left + right, minimumHeight = height + top + bottom;
        if (minimumWidth > PixelImage.MaxSize || minimumHeight > PixelImage.MaxSize)
            throw new InvalidOperationException(Gpixel.Core.Localization.DomainStrings.FormatCanvas_EffectTooLarge(width: minimumWidth, height: minimumHeight, max: PixelImage.MaxSize));

        int resultWidth = (int)minimumWidth, resultHeight = (int)minimumHeight;
        bool kept = mode == EffectCanvasExpandMode.KeepRatio;
        if (mode == EffectCanvasExpandMode.KeepCenter)
        {
            long margin = Math.Max(Math.Max(left, right), Math.Max(top, bottom));
            if (width + 2 * margin > PixelImage.MaxSize || height + 2 * margin > PixelImage.MaxSize)
                throw new InvalidOperationException(Gpixel.Core.Localization.DomainStrings.FormatCanvas_EffectTooLarge(
                    width: width + 2 * margin, height: height + 2 * margin, max: PixelImage.MaxSize));
            left = top = margin;
            resultWidth = width + (int)(2 * margin);
            resultHeight = height + (int)(2 * margin);
        }
        if (kept)
        {
            double scale = Math.Max((double)resultWidth / width, (double)resultHeight / height);
            int ratioWidth = Math.Max(resultWidth, (int)Math.Ceiling(width * scale - 1e-9));
            int ratioHeight = Math.Max(resultHeight, (int)Math.Ceiling(height * scale - 1e-9));
            if (ratioWidth <= PixelImage.MaxSize && ratioHeight <= PixelImage.MaxSize)
            {
                int extraX = ratioWidth - resultWidth, extraY = ratioHeight - resultHeight;
                left += extraX / 2;
                top += extraY / 2;
                resultWidth = ratioWidth;
                resultHeight = ratioHeight;
            }
            else
            {
                kept = false;
            }
        }
        return new EffectCanvasExpansion(resultWidth, resultHeight, (int)left, (int)top, kept);
    }
}

/// <summary>
/// 레이어 효과(<see cref="LayerEffect"/>) 그리기. 잔상을 뺀 효과는 목록 순서대로 적용하고, 잔상은 다른 효과가 각 프레임에 적용된 모습을 현재 그림 뒤에 합성한다. 꺼진 효과는 건너뛴다.
/// 모양은 알파가 0이 아닌 픽셀이다. 바깥 외곽선·그림자·반사·터지기·늘었다 줄기는 그림 밖으로 번지므로 <see cref="Render(PixelImage, IReadOnlyList{LayerEffect})"/>는 그만큼(<see cref="Padding"/>)
/// 넓힌 이미지와 원래 그림의 왼쪽 위가 놓인 자리를 돌려준다. 이미지 밖은 투명으로 본다.
/// 그리기 함수는 받은 목록을 프레임 범위(<see cref="LayerEffect.Frames"/>)와 관계없이 그린다 — 프레임 하나를 그릴 때는 부르는 쪽이
/// <see cref="At"/>으로 그 프레임에 입힐 효과만 골라(움직이는 효과는 그 프레임의 진행을 채워) 넘긴다.
/// </summary>
public static class LayerEffects
{
    /// <summary>켜진 효과가 하나라도 있는지(어느 프레임이든).</summary>
    public static bool AnyEnabled(IReadOnlyList<LayerEffect> effects)
    {
        for (int i = 0; i < effects.Count; i++)
            if (effects[i].Enabled) return true;
        return false;
    }

    /// <summary>프레임 <paramref name="frameIndex"/>에 입혀질(켜져 있고 범위 안인) 효과가 하나라도 있는지.</summary>
    public static bool AnyActive(IReadOnlyList<LayerEffect> effects, int frameIndex)
    {
        for (int i = 0; i < effects.Count; i++)
            if (effects[i].Enabled && effects[i].AppliesTo(frameIndex)) return true;
        return false;
    }

    /// <summary>켜진 효과 중 다른 프레임 그림을 보는 효과(잔상)가 있는지 — 있을 때만 <see cref="At"/>에 다른 프레임 원본을 넘긴다.</summary>
    public static bool UsesOtherFrames(IReadOnlyList<LayerEffect> effects)
    {
        for (int i = 0; i < effects.Count; i++)
            if (effects[i] is AfterimageEffect { Enabled: true }) return true;
        return false;
    }

    /// <summary>
    /// 프레임 <paramref name="frameIndex"/>(프레임 <paramref name="frameCount"/>개 중)에 그릴 효과: 범위 밖 효과를 빼고(순서 그대로, 꺼진 효과도 남음 — 그리기가 건너뜀),
    /// 움직이는 효과(<see cref="AnimatedEffect"/>)는 그 프레임의 진행과 <paramref name="pivot"/>(효과 원본 왼쪽 위 기준 px), 다른 프레임 원본
    /// <paramref name="frameSource"/>(프레임 번호 → 그 프레임 효과 원본, 잔상용 — <see cref="AnimatedEffect.FrameSource"/>)를 채운 사본으로 바꾼다.
    /// 움직이는 색 그라데이션이 있는 다른 효과도 범위 안 진행을 채운 사본으로 바꾼다.
    /// 바꿀 것이 없으면 <paramref name="effects"/> 그대로(복사 없음).
    /// </summary>
    public static IReadOnlyList<LayerEffect> At(IReadOnlyList<LayerEffect> effects, int frameIndex, int frameCount, PointF? pivot = null,
        Func<int, PixelImage?>? frameSource = null, Func<int, PointF?>? framePivot = null)
        => Resolve(effects, frameIndex, frameCount, pivot, frameSource, inRangeOnly: true, framePivot);

    /// <summary>
    /// 프레임 범위와 관계없이 모든 효과를 프레임 <paramref name="frameIndex"/>로 움직인 목록(범위 밖이면 가까운 끝의 모습) — 효과 창 미리보기용.
    /// </summary>
    public static IReadOnlyList<LayerEffect> Animate(IReadOnlyList<LayerEffect> effects, int frameIndex, int frameCount, PointF? pivot = null,
        Func<int, PixelImage?>? frameSource = null)
        => Resolve(effects, frameIndex, frameCount, pivot, frameSource, inRangeOnly: false);

    private static IReadOnlyList<LayerEffect> Resolve(IReadOnlyList<LayerEffect> effects, int frameIndex, int frameCount, PointF? pivot,
        Func<int, PixelImage?>? frameSource, bool inRangeOnly, Func<int, PointF?>? framePivot = null)
    {
        bool preservePositions = inRangeOnly && effects.Any(e => e.ClipToPrevious);
        Func<int, PixelImage, (PixelImage Image, int OffsetX, int OffsetY)>? rendered = null;
        if (frameSource is not null && effects.Any(e => e is AfterimageEffect { Enabled: true } && (!inRangeOnly || e.AppliesTo(frameIndex)))
            && effects.Any(e => e.Enabled && e is not AfterimageEffect))
        {
            var others = effects.Where(e => e is not AfterimageEffect).ToArray();
            rendered = (f, raw) =>
            {
                var previous = At(others, f, frameCount, framePivot?.Invoke(f) ?? pivot);
                return Render(raw, previous, Padding(others, raw.Width, raw.Height));
            };
        }
        List<LayerEffect>? list = null;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            LayerEffect? r = inRangeOnly && !e.AppliesTo(frameIndex)
                ? preservePositions ? e with { Enabled = false } : null
                : e is AfterimageEffect a
                    ? a.AtFrame(frameIndex, frameCount, pivot, frameSource) with
                    {
                        RenderedFrameSource = a.ClipToPrevious && rendered is not null
                            ? ClippedGhostSource(effects, i, frameCount, framePivot, pivot)
                            : rendered,
                    }
                    : e is AnimatedEffect animated ? animated.AtFrame(frameIndex, frameCount, pivot) : e;
            if (r is { Enabled: true } && r is not AnimatedEffect && r.HasAnimatedColorGradient())
            {
                var (from, to) = r.Frames is { } range ? (range.From, range.To) : (0, Math.Max(0, frameCount - 1));
                r = r with { GradientTime = Math.Clamp((double)(frameIndex - from) / Math.Max(1, to - from), 0, 1) };
            }
            if (list is null && !ReferenceEquals(r, e))
            {
                list = new List<LayerEffect>(effects.Count);
                for (int k = 0; k < i; k++) list.Add(effects[k]);
            }
            if (list is not null && r is not null) list.Add(r);
        }
        return list ?? effects;
    }

    private static Func<int, PixelImage, (PixelImage Image, int OffsetX, int OffsetY)>? ClippedGhostSource(
        IReadOnlyList<LayerEffect> effects, int index, int frameCount, Func<int, PointF?>? framePivot, PointF? pivot)
    {
        int anchor = index - 1;
        while (anchor >= 0 && effects[anchor].ClipToPrevious) anchor--;
        if (anchor < 0 || effects[anchor] is AfterimageEffect) return null;
        var group = effects.Take(anchor + 1).Where(e => e is not AfterimageEffect).ToArray();
        var before = group[..^1];
        return (frame, raw) =>
        {
            var framePosition = framePivot?.Invoke(frame) ?? pivot;
            var padding = Padding(group, raw.Width, raw.Height);
            var (image, dx, dy) = Render(raw, At(group, frame, frameCount, framePosition), padding);
            if (ReferenceEquals(image, raw))
                return (new PixelImage(raw.Width, raw.Height), 0, 0);
            var (prior, _, _) = Render(raw, At(before, frame, frameCount, framePosition), padding);
            for (int i = 0; i < image.Pixels.Length; i++)
                if (image.Pixels[i] == prior.Pixels[i]) image.Pixels[i] = Color32.Transparent;
            return (image, dx, dy);
        };
    }

    /// <summary>
    /// 영구 적용할 효과 번호(목록 순서): 고른 효과(꺼져 있어도)와 그보다 위(앞)의 켜진 효과 모두.
    /// 잔상을 고르면 앞뒤의 켜진 효과까지 구워야 각 프레임의 완성된 모습이 잔상에도 남는다. 고른 것이 없으면 빈 목록.
    /// </summary>
    public static IReadOnlyList<int> BakeSet(IReadOnlyList<LayerEffect> effects, IEnumerable<int> selected)
    {
        var chosen = selected.Where(i => i >= 0 && i < effects.Count).ToHashSet();
        if (chosen.Count == 0) return [];
        var group = new HashSet<int>();
        foreach (int selectedIndex in chosen)
        {
            int first = selectedIndex;
            while (first > 0 && effects[first].ClipToPrevious) first--;
            int lastInGroup = first;
            while (lastInGroup + 1 < effects.Count && effects[lastInGroup + 1].ClipToPrevious) lastInGroup++;
            for (int i = first; i <= lastInGroup; i++) group.Add(i);
        }
        int last = chosen.Any(i => effects[i] is AfterimageEffect) ? effects.Count - 1 : group.Max();
        return Enumerable.Range(0, last + 1).Where(i => group.Contains(i) || effects[i].Enabled).ToList();
    }

    /// <summary>
    /// <paramref name="width"/>×<paramref name="height"/> 그림에 켜진 효과들이 그림 밖으로 넓힐 수 있는 최대 여백(차례대로 쌓임: 외곽선 뒤 그림자면 외곽선까지 그림자가 진다).
    /// 프레임 범위·진행은 보지 않으므로 어느 프레임의 효과 묶음 여백보다도 작지 않다(효과를 빼면 여백은 줄기만 함) — 모든 프레임 같은 크기로 넓힐 때 쓴다.
    /// 터지기는 가로·세로 거리(세로는 중력 방향만큼 추가), 늘었다 줄기는 그때까지 넓힌 크기 × 세기만큼, 반사는 비치는 쪽으로 간격 + 그때까지 넓힌 높이%(+ 물결 폭만큼 양옆) 넓힌다.
    /// 흔들기·둥실둥실·녹아내리기·회전·충격파 고리는 <see cref="MotionPadding.Of"/>(세기·높이·거리·돌린 그림·반지름)만큼 넓힌다. 홀로그램은 좌우로 글리치 최대 밀림,
    /// 테두리 빛이면 사방으로 1px 더.
    /// 넓힌 크기가 <see cref="PixelImage.MaxSize"/>를 넘는 축은 여백 없이(그 축은 이미지 끝에서 잘림).
    /// </summary>
    public static EffectPadding Padding(IReadOnlyList<LayerEffect> effects, int width, int height)
    {
        int l = 0, t = 0, r = 0, b = 0;
        foreach (var e in effects)
        {
            if (e.ClipToPrevious && e is not ReflectionEffect) continue;
            if (!e.Enabled) continue;
            switch (e)
            {
                case OutlineEffect { Inside: false } o:
                    l += o.Thickness;
                    t += o.Thickness;
                    r += o.Thickness;
                    b += o.Thickness;
                    break;
                case ShadowEffect s:
                    l = Math.Max(l, l - s.OffsetX);
                    r = Math.Max(r, r + s.OffsetX);
                    t = Math.Max(t, t - s.OffsetY);
                    b = Math.Max(b, b + s.OffsetY);
                    break;
                case ExplodeEffect x:
                    l += x.DistanceX;
                    r += x.DistanceX;
                    t += x.DistanceY + Math.Max(0, -x.Gravity);
                    b += x.DistanceY + Math.Max(0, x.Gravity);
                    break;
                case SquashEffect q:
                {
                    // 기준점에서 그림 끝까지는 많아야 지금 크기(기준점이 그림 가운데면 절반) — 축별 세기만큼 자란다.
                    double sx = q.WidthAmount / 100.0, sy = q.HeightAmount / 100.0;
                    int cw = width + l + r, ch = height + t + b;
                    int half = sx == 0 ? 0 : (int)Math.Ceiling(cw * sx / 2) + 1, full = sx == 0 ? 0 : (int)Math.Ceiling(cw * sx) + 1;
                    int halfH = sy == 0 ? 0 : (int)Math.Ceiling(ch * sy / 2) + 1, fullH = sy == 0 ? 0 : (int)Math.Ceiling(ch * sy) + 1;
                    (int gx, int up, int down) = q.Anchor switch
                    {
                        SquashAnchor.BottomCenter => (half, fullH, 0),
                        SquashAnchor.Middle => (half, halfH, halfH),
                        _ => (full, fullH, fullH),
                    };
                    l += gx;
                    r += gx;
                    t += up;
                    b += down;
                    break;
                }
                case ReflectionEffect m:
                {
                    // 그림(지금까지 넓힌 크기)의 높이 × 높이%만큼 간격 너머에 비친다 — 보이는 영역이 아무리 커도 그 안
                    int reach = m.Gap + ReflectionRender.RowsFor(height + t + b, m.Height);
                    if (m.Above) t += reach;
                    else b += reach;
                    l += m.Wave;
                    r += m.Wave;
                    break;
                }
                case HologramEffect g:
                {
                    // 글리치 조각이 좌우로 최대 밀림만큼, 테두리 빛이 사방 1px(밀린 조각 둘레까지)
                    int edge = g.EdgeGlow ? 1 : 0;
                    l += g.GlitchReach + edge;
                    r += g.GlitchReach + edge;
                    t += edge;
                    b += edge;
                    break;
                }
                case ShakeEffect or BobEffect or MeltEffect or RotateEffect or ShockwaveEffect:
                {
                    var (gl, gt, gr, gb) = MotionPadding.Of(e, width + l + r, height + t + b);
                    l += gl;
                    t += gt;
                    r += gr;
                    b += gb;
                    break;
                }
            }
        }
        var p = new EffectPadding(l, t, r, b);
        if (width + p.Left + p.Right > PixelImage.MaxSize) p = p with { Left = 0, Right = 0 };
        if (height + p.Top + p.Bottom > PixelImage.MaxSize) p = p with { Top = 0, Bottom = 0 };
        return p;
    }

    /// <summary>
    /// 효과를 입힌 그림. 켜진 효과가 없으면 <paramref name="source"/> 그대로(복사 없음, 위치 0,0).
    /// 있으면 여백만큼 넓힌 새 이미지와, 원래 그림 왼쪽 위를 기준으로 그 이미지의 왼쪽 위 위치(여백만큼 음수)를 돌려준다.
    /// </summary>
    public static (PixelImage Image, int OffsetX, int OffsetY) Render(PixelImage source, IReadOnlyList<LayerEffect> effects)
        => Render(source, effects, Padding(effects, source.Width, source.Height));

    /// <summary>
    /// 여백을 정해 두고 효과를 입힌 그림(<paramref name="padding"/>은 <paramref name="effects"/>의 여백 이상 — 프레임마다 효과가 달라도 같은 크기로).
    /// 여백이 없고 켜진 효과도 없으면 <paramref name="source"/> 그대로(복사 없음, 위치 0,0).
    /// </summary>
    public static (PixelImage Image, int OffsetX, int OffsetY) Render(PixelImage source, IReadOnlyList<LayerEffect> effects, EffectPadding padding)
    {
        if (padding.IsNone && !AnyEnabled(effects)) return (source, 0, 0);
        var image = new PixelImage(source.Width + padding.Left + padding.Right, source.Height + padding.Top + padding.Bottom);
        image.Blit(source, padding.Left, padding.Top);
        Apply(image, effects, new Rectangle(padding.Left, padding.Top, source.Width, source.Height));
        return (image, -padding.Left, -padding.Top);
    }

    /// <summary>효과를 입힌 그림을 원래 크기로 자른 것(밖으로 번진 부분은 버림). 켜진 효과가 없으면 <paramref name="source"/> 그대로.</summary>
    public static PixelImage RenderClipped(PixelImage source, IReadOnlyList<LayerEffect> effects)
    {
        if (!AnyEnabled(effects)) return source;
        var (image, x, y) = Render(source, effects);
        return image.Crop(new Rectangle(-x, -y, source.Width, source.Height));
    }

    /// <summary>
    /// 켜진 효과를 차례로 <paramref name="image"/>에 바로 입힌다(크기는 그대로 — 번질 자리는 부르는 쪽이 미리 넓혀 둔다).
    /// <paramref name="source"/> = 효과 원본(셀, 그룹이면 캔버스)이 이 이미지 안에 놓인 자리: 터지기의 조각 격자·무작위와 직접 중심·피벗의 기준이다.
    /// 클리핑된 반사는 기준 효과의 픽셀만 뒤집어 원본 그림 뒤에 놓는다(이동한 픽셀은 기준 영역 밖에도 남는다).
    /// </summary>
    public static void Apply(PixelImage image, IReadOnlyList<LayerEffect> effects, Rectangle source)
    {
        List<AfterimageEffect>? clippedAfterimages = null;
        bool[]? clipMask = null;
        for (int index = 0; index < effects.Count; index++)
        {
            var effect = effects[index];
            if (effect is AfterimageEffect after)
            {
                if (after.ClipToPrevious && effect.Enabled && clipMask is not null)
                    (clippedAfterimages ??= []).Add(after);
                if (!effect.ClipToPrevious) clipMask = null;
                continue;
            }
            if (!effect.Enabled)
            {
                if (!effect.ClipToPrevious) clipMask = null;
                continue;
            }
            if (effect.ClipToPrevious)
            {
                if (clipMask is null) continue;
                if (effect is ReflectionEffect reflection)
                {
                    var reflected = new PixelImage(image.Width, image.Height);
                    for (int i = 0; i < image.Pixels.Length; i++)
                        if (clipMask[i]) reflected.Pixels[i] = image.Pixels[i];
                    ReflectionRender.Apply(reflected, reflection, source);
                    for (int i = 0; i < image.Pixels.Length; i++)
                        if (!clipMask[i] && reflected.Pixels[i].A != 0)
                            image.Pixels[i] = Color32.BlendOver(reflected.Pixels[i], image.Pixels[i]);
                    continue;
                }
                var before = (Color32[])image.Pixels.Clone();
                ApplyOne(image, effect, source);
                for (int i = 0; i < before.Length; i++)
                    if (!clipMask[i]) image.Pixels[i] = before[i];
                continue;
            }

            bool hasClippedFollower = index + 1 < effects.Count && effects[index + 1].ClipToPrevious;
            Color32[]? anchorBefore = hasClippedFollower ? (Color32[])image.Pixels.Clone() : null;
            ApplyOne(image, effect, source);
            clipMask = anchorBefore is null ? null : ChangedPixels(anchorBefore, image.Pixels);
        }
        // 잔상은 다른 효과가 각 프레임에 적용된 모습 뒤에 합성한다. 목록 위치로 현재 프레임의 움직임을 다시 입히지 않는다.
        foreach (var effect in effects)
            if (effect is AfterimageEffect { Enabled: true, ClipToPrevious: false } after) AfterimageRender.Apply(image, after, source);
        if (clippedAfterimages is not null)
            foreach (var after in clippedAfterimages) AfterimageRender.Apply(image, after, source);
    }

    private static bool[] ChangedPixels(Color32[] before, Color32[] after)
    {
        var changed = new bool[before.Length];
        for (int i = 0; i < before.Length; i++) changed[i] = before[i] != after[i];
        return changed;
    }

    private static void ApplyOne(PixelImage image, LayerEffect effect, Rectangle source)
    {
        switch (effect)
        {
            case OutlineEffect o: Outline(image, o, source); break;
            case ShadowEffect s: Shadow(image, s, source); break;
            case RimLightEffect r: Rim(image, r, source); break;
            case ColorOverlayEffect c: Overlay(image, c, source); break;
            case ExplodeEffect x: Explode(image, x, source); break;
            case ShineEffect s: Shine(image, s, source); break;
            case SquashEffect q: Squash(image, q, source); break;
            case InnerGlowEffect g: InnerGlowRender.Apply(image, g, source); break;
            case PaletteSwapEffect p: PaletteSwapRender.Apply(image, p, source); break;
            case ColorAdjustEffect c: ColorAdjustRender.Apply(image, c); break;
            case ReflectionEffect m: ReflectionRender.Apply(image, m, source); break;
            case HitFlashEffect f: HitFlashRender.Apply(image, f, source); break;
            case BlinkEffect k: BlinkRender.Apply(image, k, source); break;
            case ShakeEffect k: ShakeRender.Apply(image, k); break;
            case BobEffect k: ShakeRender.Apply(image, k); break;
            case AppearEffect a: AppearRender.Apply(image, a, source); break;
            case FadeEffect f: FadeRender.Apply(image, f); break;
            case MeltEffect m: MeltRender.Apply(image, m, source); break;
            case RotateEffect q: RotateRender.Apply(image, q, source); break;
            case ColorCycleEffect c: ColorCycleRender.Apply(image, c, source); break;
            case ShockwaveEffect w: ShockwaveRender.Apply(image, w, source); break;
            case HologramEffect g: HologramRender.Apply(image, g, source); break;
        }
    }

    // ───────────── 효과 ─────────────

    private static void Outline(PixelImage image, OutlineEffect o, Rectangle source)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var shape = Mask(px);
        var grown = shape;
        var bounds = o.ColorGradients.ContainsKey(nameof(OutlineEffect.Color)) ? image.GetOpaqueBounds() : default;
        var color = EffectGradientSampler.For(o, nameof(OutlineEffect.Color), bounds, o.Color, source.X, source.Y);
        if (o.Inside)
        {
            for (int k = 0; k < o.Thickness; k++) grown = Erode(grown, w, h, o.Diagonal);
            for (int i = 0; i < px.Length; i++)
                if (shape[i] && !grown[i])
                {
                    var c = color.Sample(i % image.Width, i / image.Width);
                    px[i] = Mix(px[i], c, c.A);
                }
        }
        else
        {
            for (int k = 0; k < o.Thickness; k++) grown = Dilate(grown, w, h, o.Diagonal);
            for (int i = 0; i < px.Length; i++)
                if (grown[i] && !shape[i])
                    px[i] = color.Sample(i % image.Width, i / image.Width);
        }
    }

    /// <summary>그림자. 디더면 반투명 대신 색(불투명)을 밀도 무늬(4×4 Bayer, 효과 원본 왼쪽 위 기준)의 칸에만 깐다.</summary>
    private static void Shadow(PixelImage image, ShadowEffect s, Rectangle source)
    {
        int w = image.Width, h = image.Height, dx = s.OffsetX, dy = s.OffsetY;
        int density = s.Dither switch { ShadowDither.Light25 => 4, ShadowDither.Half50 => 8, ShadowDither.Dense75 => 12, _ => 0 };
        var solid = s.Color.WithAlpha(255);
        var bounds = s.ColorGradients.ContainsKey(nameof(ShadowEffect.Color)) ? image.GetOpaqueBounds() : default;
        var color = EffectGradientSampler.For(s, nameof(ShadowEffect.Color), bounds, s.Color, source.X, source.Y);
        var px = image.Pixels;
        var src = (Color32[])px.Clone();
        for (int y = 0; y < h; y++)
        {
            int sy = y - dy;
            if ((uint)sy >= (uint)h) continue;
            for (int x = 0; x < w; x++)
            {
                int sx = x - dx;
                if ((uint)sx >= (uint)w) continue;
                byte a = src[sy * w + sx].A;
                if (a == 0) continue;
                Color32 shadow;
                if (density == 0)
                {
                    var sampled = color.Sample(x, y);
                    shadow = sampled.WithAlpha((byte)((sampled.A * a + 127) / 255));
                }
                else if (Bayer.Threshold4(x - source.X, y - source.Y) < density)
                    shadow = color.HasGradient ? color.Sample(x, y) : solid;
                else continue;
                int i = y * w + x;
                px[i] = Color32.BlendOver(shadow, src[i]);
            }
        }
    }

    private static void Rim(PixelImage image, RimLightEffect r, Rectangle source)
    {
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var shape = Mask(px);
        var (vx, vy) = r.Vector;
        var bounds = r.ColorGradients.Count > 0 ? image.GetOpaqueBounds() : default;
        var light = EffectGradientSampler.For(r, nameof(RimLightEffect.Light), bounds, r.Light, source.X, source.Y);
        var shade = EffectGradientSampler.For(r, nameof(RimLightEffect.Shade), bounds, r.Shade, source.X, source.Y);
        bool Solid(int x, int y) => (uint)x < (uint)w && (uint)y < (uint)h && shape[y * w + x];
        bool Edge(int x, int y, int dx, int dy)
        {
            for (int distance = 1; distance <= r.Size; distance++)
                if ((dx != 0 && !Solid(x + dx * distance, y)) || (dy != 0 && !Solid(x, y + dy * distance)))
                    return true;
            return false;
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!shape[i]) continue;
                if ((r.Light.A != 0 || light.HasGradient) && Edge(x, y, vx, vy))
                {
                    var color = light.Sample(x, y);
                    px[i] = Mix(px[i], color, color.A);
                }
                else if ((r.Shade.A != 0 || shade.HasGradient) && Edge(x, y, -vx, -vy))
                {
                    var color = shade.Sample(x, y);
                    px[i] = Mix(px[i], color, color.A);
                }
            }
    }

    private static void Overlay(PixelImage image, ColorOverlayEffect c, Rectangle source)
    {
        int amount = (c.Amount * 255 + 50) / 100;
        if (amount == 0) return;
        var bounds = c.ColorGradients.ContainsKey(nameof(ColorOverlayEffect.Color)) ? image.GetOpaqueBounds() : default;
        var color = EffectGradientSampler.For(c, nameof(ColorOverlayEffect.Color), bounds, c.Color, source.X, source.Y);
        var px = image.Pixels;
        int w = image.Width;
        for (int i = 0; i < px.Length; i++)
        {
            if (px[i].A == 0) continue;
            var sampled = color.Sample(i % w, i / w);
            int strength = (amount * sampled.A + 127) / 255;
            if (strength != 0) px[i] = Blend(px[i], sampled, strength, c.Blend);
        }
    }

    // ───────────── 움직이는 효과 ─────────────

    private static void Explode(PixelImage image, ExplodeEffect e, Rectangle source)
    {
        double u = e.Phase, t = e.Progress;
        int f = e.FlashFrames, step = e.FrameStep;
        int flash = f > 0 && step >= 1 && step <= f ? (255 * (f - step + 1) + f / 2) / f : 0;
        if (u <= 0 && flash == 0) return; // 시작 = 원래 그림 그대로
        double fade = !e.Fade ? 1 : e.FadeStart >= 100 ? (u >= 1 ? 0 : 1) : Math.Clamp((1 - u) / (1 - e.FadeStart / 100.0), 0, 1);
        if (e.DistanceX == 0 && e.DistanceY == 0 && e.Gravity == 0 && flash == 0 && fade == 1 && (!e.Shrink || e.PieceSize == 1))
            return; // 움직임·시각 변화가 없으면 픽셀 배열을 복사하지 않는다
        var bounds = image.GetOpaqueBounds();
        if (bounds.IsEmpty) return;
        var flashColor = flash > 0
            ? EffectGradientSampler.For(e, nameof(ExplodeEffect.FlashColor), bounds, e.FlashColor, source.X, source.Y)
            : default;
        int w = image.Width, h = image.Height, n = e.PieceSize;
        var px = image.Pixels;
        var src = (Color32[])px.Clone();
        Array.Clear(px);

        if (fade > 0)
        {
            var (cx, cy) = e.Center switch
            {
                ExplodeCenter.Middle => (bounds.Left + bounds.Width / 2.0, bounds.Top + bounds.Height / 2.0),
                ExplodeCenter.BottomCenter => (bounds.Left + bounds.Width / 2.0, (double)bounds.Bottom),
                ExplodeCenter.Custom => (source.X + e.CenterX + 0.5, source.Y + e.CenterY + 0.5),
                _ => PivotIn(e.PivotPoint, source),
            };
            int size = e.Shrink ? Math.Max(1, (int)Math.Ceiling(n * (1 - u) - 1e-9)) : n, inset = (n - size) / 2;
            double spreadX = e.SpreadX / 100.0, spreadY = e.SpreadY / 100.0, fall = e.Gravity * u * u;
            // 바깥 조각일수록 멀리(중심 조각은 거리의 40%) — 그림이 통째로 부풀며 흩어진다
            double reach = Math.Max(1, Math.Max(Math.Max(Math.Abs(bounds.Left - cx), Math.Abs(bounds.Right - cx)), Math.Max(Math.Abs(bounds.Top - cy), Math.Abs(bounds.Bottom - cy))));
            // 조각 격자는 원본 왼쪽 위 기준 — 여백이 달라도(캔버스·부위 아틀라스) 같은 조각·같은 무작위
            int gx0 = FloorDiv(bounds.Left - source.X, n), gx1 = FloorDiv(bounds.Right - 1 - source.X, n);
            int gy0 = FloorDiv(bounds.Top - source.Y, n), gy1 = FloorDiv(bounds.Bottom - 1 - source.Y, n);
            for (int gy = gy0; gy <= gy1; gy++)
                for (int gx = gx0; gx <= gx1; gx++)
                {
                    int x0 = source.X + gx * n, y0 = source.Y + gy * n;
                    double vx = x0 + n / 2.0 - cx, vy = y0 + n / 2.0 - cy;
                    double angle = vx * vx + vy * vy < 1e-9 ? Random(e.Seed, gx, gy, 0) * 2 * Math.PI : Math.Atan2(vy, vx);
                    double jitter = Random(e.Seed, gx, gy, 1) * 2 - 1;
                    double radial = 0.4 + 0.6 * Math.Min(1, Math.Sqrt(vx * vx + vy * vy) / reach);
                    double speedRandom = Random(e.Seed, gx, gy, 2);
                    double angleX = angle + jitter * spreadX * Math.PI / 2;
                    double travelX = e.DistanceX * radial * (1 - spreadX * 0.6 * speedRandom) * t;
                    int dx = e.DistanceX == 0 ? 0 : (int)Math.Round(Math.Cos(angleX) * travelX, MidpointRounding.AwayFromZero);
                    double angleY = spreadX == spreadY ? angleX : angle + jitter * spreadY * Math.PI / 2;
                    double travelY = spreadX == spreadY && e.DistanceX == e.DistanceY ? travelX
                        : e.DistanceY * radial * (1 - spreadY * 0.6 * speedRandom) * t;
                    int dy = e.DistanceY == 0 ? (int)Math.Round(fall, MidpointRounding.AwayFromZero)
                        : (int)Math.Round(Math.Sin(angleY) * travelY + fall, MidpointRounding.AwayFromZero);
                    for (int yy = y0 + inset; yy < y0 + inset + size; yy++)
                    {
                        int ty = yy + dy;
                        if ((uint)yy >= (uint)h || (uint)ty >= (uint)h) continue;
                        for (int xx = x0 + inset; xx < x0 + inset + size; xx++)
                        {
                            int tx = xx + dx;
                            if ((uint)xx >= (uint)w || (uint)tx >= (uint)w) continue;
                            var c = src[yy * w + xx];
                            if (c.A == 0) continue;
                            if (flash > 0)
                            {
                                var sampled = flashColor.Sample(xx, yy);
                                c = Mix(c, sampled, flashColor.HasGradient ? (flash * sampled.A + 127) / 255 : flash);
                            }
                            if (fade < 1)
                            {
                                byte a = (byte)Math.Round(c.A * fade);
                                if (a == 0) continue;
                                c = c.WithAlpha(a);
                            }
                            int i = ty * w + tx;
                            px[i] = Color32.BlendOver(px[i], c);
                        }
                    }
                }
        }
        if (e.KeepOriginal)
            for (int i = 0; i < px.Length; i++)
                if (src[i].A != 0) px[i] = Color32.BlendOver(px[i], src[i]);
    }

    private static void Shine(PixelImage image, ShineEffect s, Rectangle source)
    {
        int amount = (s.Strength * 255 + 50) / 100;
        if (amount == 0) return;
        var bounds = image.GetOpaqueBounds();
        if (bounds.IsEmpty) return;
        var color = EffectGradientSampler.For(s, nameof(ShineEffect.Color), bounds, s.Color, source.X, source.Y);
        double angle = s.Angle * Math.PI / 180, dx = Math.Cos(angle), dy = -Math.Sin(angle); // y는 아래로
        double Along(double x, double y) => x * dx + y * dy;
        double l = bounds.Left + 0.5, r = bounds.Right - 0.5, top = bounds.Top + 0.5, bottom = bounds.Bottom - 0.5;
        double s0 = Math.Min(Math.Min(Along(l, top), Along(r, top)), Math.Min(Along(l, bottom), Along(r, bottom)));
        double s1 = Math.Max(Math.Max(Along(l, top), Along(r, top)), Math.Max(Along(l, bottom), Along(r, bottom)));
        double half = s.Width / 2.0, reach = half + s.Softness;
        // 띠 가운데: 진행 0 = 첫 픽셀보다 (반폭 + 부드러움 + 1) 앞, 1 = 마지막 픽셀보다 그만큼 뒤 — 양 끝에서는 닿는 픽셀이 없다
        double from = s0 - reach - 1, to = s1 + reach + 1, c = from + (to - from) * s.Progress;
        int w = image.Width;
        var px = image.Pixels;
        for (int y = bounds.Top; y < bounds.Bottom; y++)
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                int i = y * w + x;
                if (px[i].A == 0) continue;
                double d = Math.Abs(Along(x + 0.5, y + 0.5) - c);
                if (d > reach) continue;
                int k = d <= half ? amount : amount * (s.Softness + 1 - (int)Math.Ceiling(d - half)) / (s.Softness + 1);
                if (k > 0)
                {
                    var sampled = color.Sample(x, y);
                    px[i] = Screen(px[i], sampled, color.HasGradient ? (k * sampled.A + 127) / 255 : k);
                }
            }
    }

    private static void Squash(PixelImage image, SquashEffect q, Rectangle source)
    {
        double f = q.Wobble(q.Progress);
        if (Math.Abs(f) < 1e-9) return;
        var bounds = image.GetOpaqueBounds();
        if (bounds.IsEmpty) return;
        double sx = 1 + q.WidthAmount / 100.0 * f, sy = 1 - q.HeightAmount / 100.0 * f;
        var (ax, ay) = q.Anchor switch
        {
            SquashAnchor.BottomCenter => (bounds.Left + bounds.Width / 2.0, (double)bounds.Bottom),
            SquashAnchor.Middle => (bounds.Left + bounds.Width / 2.0, bounds.Top + bounds.Height / 2.0),
            _ => PivotIn(q.PivotPoint, source),
        };
        int w = image.Width, h = image.Height;
        var px = image.Pixels;
        var src = (Color32[])px.Clone();
        Array.Clear(px);
        // 결과가 놓일 수 있는 자리만(기준점에서 그림 끝까지 × 배율, 한 칸 여유)
        int x0 = Math.Max(0, (int)Math.Floor(ax + (bounds.Left - ax) * sx) - 1), x1 = Math.Min(w, (int)Math.Ceiling(ax + (bounds.Right - ax) * sx) + 1);
        int y0 = Math.Max(0, (int)Math.Floor(ay + (bounds.Top - ay) * sy) - 1), y1 = Math.Min(h, (int)Math.Ceiling(ay + (bounds.Bottom - ay) * sy) + 1);
        for (int y = y0; y < y1; y++)
        {
            int iy = (int)Math.Floor(ay + (y + 0.5 - ay) / sy);
            if (iy < bounds.Top || iy >= bounds.Bottom) continue;
            for (int x = x0; x < x1; x++)
            {
                int ix = (int)Math.Floor(ax + (x + 0.5 - ax) / sx);
                if (ix < bounds.Left || ix >= bounds.Right) continue;
                px[y * w + x] = src[iy * w + ix];
            }
        }
    }

    /// <summary>피벗 자리(이미지 기준): 없으면 원본 가운데.</summary>
    internal static (double X, double Y) PivotIn(PointF? pivot, Rectangle source)
        => pivot is { } p ? (source.X + p.X, source.Y + p.Y) : (source.X + source.Width / 2.0, source.Y + source.Height / 2.0);

    internal static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    /// <summary>시드·조각 자리·용도로 정해지는 0 이상 1 미만의 수(늘 같음).</summary>
    internal static double Random(int seed, int x, int y, int salt)
    {
        uint h = (uint)seed * 0x9E3779B1u ^ (uint)x * 0x85EBCA77u ^ (uint)y * 0xC2B2AE3Du ^ (uint)(salt + 1) * 0x27D4EB2Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return h / 4294967296.0;
    }

    /// <summary>스크린(밝게 더하기)을 <paramref name="amount"/>/255만큼(알파는 그대로).</summary>
    internal static Color32 Screen(Color32 dst, Color32 color, int amount)
    {
        byte S(byte d, byte c)
        {
            int screen = 255 - (255 - d) * (255 - c) / 255;
            return (byte)(d + ((screen - d) * amount + 127) / 255);
        }
        return new Color32(S(dst.R, color.R), S(dst.G, color.G), S(dst.B, color.B), dst.A);
    }

    // ───────────── 도우미 ─────────────

    /// <summary>알파가 0이 아닌 픽셀.</summary>
    internal static bool[] Mask(Color32[] px)
    {
        var mask = new bool[px.Length];
        for (int i = 0; i < px.Length; i++) mask[i] = px[i].A != 0;
        return mask;
    }

    /// <summary>한 칸 넓히기(4방향 = 십자, 8방향 = 모서리 포함).</summary>
    internal static bool[] Dilate(bool[] src, int w, int h, bool diagonal)
    {
        var dst = new bool[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                dst[i] = src[i] || Any(src, w, h, x, y, diagonal);
            }
        return dst;
    }

    /// <summary>한 칸 깎기: 이웃(4/8방향) 중 하나라도 비었거나 이미지 밖이면 빠진다.</summary>
    internal static bool[] Erode(bool[] src, int w, int h, bool diagonal)
    {
        var dst = new bool[src.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                dst[i] = src[i] && All(src, w, h, x, y, diagonal);
            }
        return dst;
    }

    private static readonly (int X, int Y)[] Cross = [(-1, 0), (1, 0), (0, -1), (0, 1)];
    private static readonly (int X, int Y)[] Square = [(-1, 0), (1, 0), (0, -1), (0, 1), (-1, -1), (1, -1), (-1, 1), (1, 1)];

    private static bool Any(bool[] m, int w, int h, int x, int y, bool diagonal)
    {
        foreach (var (dx, dy) in diagonal ? Square : Cross)
        {
            int nx = x + dx, ny = y + dy;
            if ((uint)nx < (uint)w && (uint)ny < (uint)h && m[ny * w + nx]) return true;
        }
        return false;
    }

    private static bool All(bool[] m, int w, int h, int x, int y, bool diagonal)
    {
        foreach (var (dx, dy) in diagonal ? Square : Cross)
        {
            int nx = x + dx, ny = y + dy;
            if ((uint)nx >= (uint)w || (uint)ny >= (uint)h || !m[ny * w + nx]) return false;
        }
        return true;
    }

    /// <summary><paramref name="dst"/>의 RGB를 섞기 방법대로 <paramref name="color"/>와 섞은 결과 쪽으로 <paramref name="amount"/>/255만큼(알파는 그대로).</summary>
    internal static Color32 Blend(Color32 dst, Color32 color, int amount, EffectBlend blend)
    {
        switch (blend)
        {
            case EffectBlend.Screen:
                return Screen(dst, color, amount);
            case EffectBlend.Multiply:
                var m = new Color32((byte)((dst.R * color.R + 127) / 255), (byte)((dst.G * color.G + 127) / 255), (byte)((dst.B * color.B + 127) / 255));
                return Mix(dst, m, amount);
            default:
                return Mix(dst, color, amount);
        }
    }

    /// <summary><paramref name="dst"/>의 RGB를 <paramref name="color"/> 쪽으로 <paramref name="amount"/>/255만큼(알파는 그대로).</summary>
    internal static Color32 Mix(Color32 dst, Color32 color, int amount)
    {
        if (amount <= 0) return dst;
        int keep = 255 - amount;
        byte M(byte d, byte c) => (byte)((d * keep + c * amount + 127) / 255);
        return new Color32(M(dst.R, color.R), M(dst.G, color.G), M(dst.B, color.B), dst.A);
    }
}
