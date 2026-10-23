namespace Gpixel.Core.Model;

/// <summary>
/// 레이어 효과 하나(비파괴): 셀 픽셀은 그대로 두고 합성할 때 레이어(그룹이면 그 그룹의 합성)의 그림에 입힌다.
/// 값 객체(불변 record)라 실행취소 기록은 목록 참조만 담는다. 바꿀 때는 <c>with</c>로 새 값을 만들어
/// <see cref="SpriteProject.SetLayerEffects"/>로 넣는다. 모든 효과는 픽셀 단위(번짐·안티에일리어싱 없음)다.
/// </summary>
public abstract record LayerEffect
{
    /// <summary>꺼진 효과는 목록에 남아 있지만 그리지 않는다.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 효과가 입혀질 프레임 범위(프레임 번호, 양 끝 포함). null = 모든 프레임(나중에 늘어난 프레임까지).
    /// 태그(<see cref="AnimationTag"/>)처럼 프레임 번호 기준이다: 범위 안에 프레임을 넣으면 늘어나고, 범위 앞에 넣으면 뒤로 밀리며,
    /// 프레임을 지우면 줄어든다(범위의 프레임을 모두 지우면 효과도 지워진다). 프레임 순서 바꾸기는 범위를 그대로 둔다
    /// (그 자리에 온 프레임에 입혀짐 — 영상 편집의 클립처럼 시간 위치 기준). 프레임 편집은 <see cref="SpriteProject"/>가 맞춘다.
    /// </summary>
    public FrameRange? Frames { get; init; }

    /// <summary>
    /// 바로 앞의 클리핑되지 않은 효과가 바꾼 픽셀을 기준으로 이 효과를 적용한다.
    /// 연속된 클리핑 효과는 같은 기준 효과의 영역을 공유한다. 반사·잔상은 기준 픽셀을 이동한 결과도 표시한다.
    /// </summary>
    public bool ClipToPrevious { get; init; }

    /// <summary>각 색 설정의 그라데이션. 키는 색 속성 이름(목록 색은 인덱스 포함); 없는 키는 단색 설정을 쓴다.</summary>
    public IReadOnlyDictionary<string, EffectColorGradient> ColorGradients { get; init; }
        = System.Collections.Immutable.ImmutableDictionary<string, EffectColorGradient>.Empty;

    /// <summary>Render-only progress of an animated color gradient (0 at first frame, 1 at last); never saved.</summary>
    internal double GradientTime { get; init; }

    /// <summary>프레임 <paramref name="frameIndex"/>가 범위 안인지(켜짐 여부와 무관).</summary>
    public bool AppliesTo(int frameIndex) => Frames is not { } r || r.Contains(frameIndex);

    internal bool HasAnimatedColorGradient()
    {
        if (ColorGradients.Count == 0) return false;
        // The editor's dictionaries are concrete; do not box their enumerator on every static render.
        if (ColorGradients is Dictionary<string, EffectColorGradient> colors)
        {
            foreach (var entry in colors)
                if (entry.Value.Animated) return true;
            return false;
        }
        foreach (var entry in ColorGradients)
            if (entry.Value.Animated) return true;
        return false;
    }

    /// <summary>화면에 보일 이름.</summary>
    public abstract string DisplayName { get; }
}

/// <summary>프레임 번호 범위(양 끝 포함, 0 ≤ <see cref="From"/> ≤ <see cref="To"/>).</summary>
public readonly record struct FrameRange
{
    public FrameRange(int from, int to)
    {
        if (from < 0 || to < from) throw new ArgumentOutOfRangeException(nameof(from), Gpixel.Core.Localization.DomainStrings.FormatFrames_InvalidRange(from: from, to: to));
        From = from;
        To = to;
    }

    public int From { get; }
    public int To { get; }

    public bool Contains(int frameIndex) => frameIndex >= From && frameIndex <= To;

    /// <summary>
    /// 프레임 <paramref name="frameCount"/>개 기준으로 맞춘 범위: 범위 밖 번호는 안으로 당기고(<paramref name="from"/> &gt; <paramref name="to"/>면 서로 바꿈),
    /// 모든 프레임을 덮으면 null(= 모든 프레임).
    /// </summary>
    public static FrameRange? Of(int from, int to, int frameCount)
    {
        int last = Math.Max(0, frameCount - 1);
        if (from > to) (from, to) = (to, from);
        from = Math.Clamp(from, 0, last);
        to = Math.Clamp(to, from, last);
        return from == 0 && to == last ? null : new FrameRange(from, to);
    }

    public override string ToString() => From == To ? $"{From + 1}" : $"{From + 1}–{To + 1}";
}

/// <summary>외곽선: 그림 둘레에 한 색 선(바깥 = 그림 밖 투명 자리, 안쪽 = 그림 가장자리 픽셀).</summary>
public sealed record OutlineEffect : LayerEffect
{
    public const int MaxThickness = 3;

    private readonly int _thickness = 1;

    public Color32 Color { get; init; } = Color32.Black;

    /// <summary>두께 1~<see cref="MaxThickness"/>px.</summary>
    public int Thickness { get => _thickness; init => _thickness = Math.Clamp(value, 1, MaxThickness); }

    /// <summary>true = 8방향(모서리 포함, 네모난 선), false = 4방향(십자, 모서리가 비는 선).</summary>
    public bool Diagonal { get; init; }

    /// <summary>true = 안쪽(그림 가장자리 픽셀을 칠함, 크기 그대로), false = 바깥(그림 밖으로 두께만큼 커짐).</summary>
    public bool Inside { get; init; }

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_Outline;
}

/// <summary>
/// 그림자: 그림 모양 그대로(단단한 그림자) 한 색으로 (X, Y)px 옮겨 뒤에 깐다.
/// <see cref="Dither"/>를 고르면 반투명 대신 불투명한 색을 그 밀도의 Bayer 무늬 칸에만 깐다(색의 알파는 쓰지 않음).
/// </summary>
public sealed record ShadowEffect : LayerEffect
{
    public const int MaxOffset = 16;

    private readonly int _x = 1, _y = 1;

    /// <summary>그림자 색(알파 = 그림자 진하기).</summary>
    public Color32 Color { get; init; } = new(0, 0, 0, 128);

    /// <summary>오른쪽으로 옮길 px(음수 = 왼쪽), ±<see cref="MaxOffset"/>.</summary>
    public int OffsetX { get => _x; init => _x = Math.Clamp(value, -MaxOffset, MaxOffset); }

    /// <summary>아래로 옮길 px(음수 = 위), ±<see cref="MaxOffset"/>.</summary>
    public int OffsetY { get => _y; init => _y = Math.Clamp(value, -MaxOffset, MaxOffset); }

    /// <summary>디더 밀도(없음 = 색의 알파로 반투명). 예전 파일은 없음.</summary>
    public ShadowDither Dither { get; init; } = ShadowDither.None;

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_Shadow;
}

/// <summary>디더 그림자 밀도(4×4 Bayer 무늬에서 칠하는 칸 비율). 50% = 바둑판.</summary>
public enum ShadowDither
{
    None,
    Light25,
    Half50,
    Dense75,
}

/// <summary>빛이 오는 방향(가장자리 빛·그늘).</summary>
public enum LightDirection
{
    TopLeft, Top, TopRight,
    Left, Right,
    BottomLeft, Bottom, BottomRight,
}

/// <summary>
/// 가장자리 빛·그늘: 빛 방향으로 지정한 거리 안에 투명 픽셀이 있으면 빛 색을, 반대쪽이면 그늘 색을 덧칠한다(알파는 그대로).
/// 대각선 방향이면 그 두 축 중 하나라도 비면 가장자리다. 한 픽셀이 양쪽 다 해당하면 빛이 이긴다. 색 알파 0 = 그쪽은 칠하지 않음.
/// </summary>
public sealed record RimLightEffect : LayerEffect
{
    public const int MaxSize = 16;
    private readonly int _size = 1;

    /// <summary>가장자리에서 안쪽으로 칠할 칸 수 1~<see cref="MaxSize"/>.</summary>
    public int Size { get => _size; init => _size = Math.Clamp(value, 1, MaxSize); }
    public Color32 Light { get; init; } = new(255, 255, 255, 160);
    public Color32 Shade { get; init; } = new(0, 0, 0, 96);
    public LightDirection Direction { get; init; } = LightDirection.TopLeft;

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_EdgeLightShade;

    /// <summary>빛이 오는 쪽 (dx, dy) — 각각 −1, 0, 1(왼쪽 위 원점).</summary>
    public (int X, int Y) Vector => Direction switch
    {
        LightDirection.TopLeft => (-1, -1),
        LightDirection.Top => (0, -1),
        LightDirection.TopRight => (1, -1),
        LightDirection.Left => (-1, 0),
        LightDirection.Right => (1, 0),
        LightDirection.BottomLeft => (-1, 1),
        LightDirection.Bottom => (0, 1),
        _ => (1, 1),
    };
}

/// <summary>색 덮기: 보이는 픽셀의 RGB를 색(또는 공간 그라데이션) 쪽으로 <see cref="Amount"/>% × 색 알파만큼 섞는다. 원본 픽셀 알파는 그대로.</summary>
public sealed record ColorOverlayEffect : LayerEffect
{
    private readonly int _amount = 100;

    public Color32 Color { get; init; } = Color32.White;

    /// <summary>0~100(%).</summary>
    public int Amount { get => _amount; init => _amount = Math.Clamp(value, 0, 100); }

    /// <summary>색을 원본에 섞는 방식. 예전 파일은 일반 덮기.</summary>
    public EffectBlend Blend { get; init; } = EffectBlend.Normal;

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_ColorOverlay;
}

/// <summary>움직이는 효과의 빠르기 곡선(진행 0→1을 어떻게 지나가는지).</summary>
public enum EffectEasing
{
    /// <summary>처음 빠르게(끝으로 갈수록 느려짐).</summary>
    EaseOut,
    /// <summary>일정하게.</summary>
    Linear,
    /// <summary>천천히 시작.</summary>
    EaseIn,
    /// <summary>천천히 시작해 천천히 끝남.</summary>
    EaseInOut,
}

/// <summary>움직이는 효과가 프레임 범위 안에서 몇 번 도는지.</summary>
public enum EffectRepeat
{
    /// <summary>한 번(범위 처음 = 시작, 끝 = 끝).</summary>
    Once,
    /// <summary>N번 반복(끝나면 처음부터).</summary>
    Loop,
    /// <summary>왕복 N번(갔다가 돌아옴 — 범위 끝에서 원래대로).</summary>
    PingPong,
}

/// <summary>
/// 움직이는(애니메이션) 효과: 모습이 효과의 프레임 범위 안 진행 <see cref="Time"/>(범위 첫 프레임 0 → 마지막 프레임 1)에 따라 바뀐다.
/// 범위가 길면 느리게, 짧으면 빠르게 움직인다. 범위가 null이면 애니메이션 전체가 범위다.
/// 저장되는 설정은 빠르기 곡선·반복뿐이고, 그릴 프레임 위치(<see cref="FrameStep"/>·<see cref="FrameSpan"/>)와 피벗(<see cref="PivotPoint"/>)·다른 프레임 원본(<see cref="FrameSource"/>)은
/// 그릴 때 <see cref="Edit.LayerEffects.At"/>이 채운 사본에만 있다(저장 안 함, 목록에 든 효과는 0·null).
/// </summary>
public abstract record AnimatedEffect : LayerEffect
{
    public const int MaxRepeatCount = 8;

    private readonly int _repeatCount = 1, _step, _span;

    public EffectEasing Easing { get; init; } = EffectEasing.Linear;
    public EffectRepeat Repeat { get; init; } = EffectRepeat.Once;

    /// <summary>반복·왕복 횟수 1~<see cref="MaxRepeatCount"/>(한 번이면 쓰지 않음).</summary>
    public int RepeatCount { get => _repeatCount; init => _repeatCount = Math.Clamp(value, 1, MaxRepeatCount); }

    /// <summary>그릴 때만: 범위 첫 프레임부터 몇 프레임째인지(0부터).</summary>
    public int FrameStep { get => _step; init => _step = Math.Max(0, value); }

    /// <summary>그릴 때만: 범위 길이 − 1(끝 − 시작). 0이면 진행은 늘 0.</summary>
    public int FrameSpan { get => _span; init => _span = Math.Max(0, value); }

    /// <summary>그릴 때만: 피벗 자리(효과 원본 — 셀·그룹이면 캔버스 — 왼쪽 위 기준 px). null이면 원본 가운데.</summary>
    public System.Drawing.PointF? PivotPoint { get; init; }

    /// <summary>
    /// 그릴 때만: 프레임 번호 → 그 프레임의 효과 원본(셀, 그룹이면 부위 합성 — 효과 없이, 원본과 같은 크기, 빈 셀이면 null). 잔상처럼 다른 프레임 그림이 필요한 효과가 쓴다.
    /// null이면 다른 프레임을 쓰지 않는다. 주는 쪽이 셀 원본 그대로를 주므로(효과 재귀 없음) 결과는 셀 내용만으로 정해진다.
    /// </summary>
    public Func<int, PixelImage?>? FrameSource { get; init; }

    /// <summary>그릴 때만: 앞 프레임에 잔상을 제외한 효과를 적용한 그림과 원본 대비 위치.</summary>
    public Func<int, PixelImage, (PixelImage Image, int OffsetX, int OffsetY)>? RenderedFrameSource { get; init; }

    /// <summary>그릴 때만: 그리는 프레임 번호(범위 시작 + <see cref="FrameStep"/>).</summary>
    public int FrameIndex => (Frames?.From ?? 0) + FrameStep;

    /// <summary>진행 t = (프레임 − 시작) / max(1, 끝 − 시작), 0~1.</summary>
    public double Time => Math.Clamp((double)FrameStep / Math.Max(1, FrameSpan), 0, 1);

    /// <summary>반복을 넣은 진행(0~1, 빠르기 곡선 전).</summary>
    public double Phase => Cycle(Time, Repeat, RepeatCount);

    /// <summary>반복과 빠르기 곡선을 넣은 진행(0~1).</summary>
    public double Progress => Ease(Phase, Easing);

    /// <summary>
    /// 프레임 <paramref name="frameIndex"/>(프레임 <paramref name="frameCount"/>개 중)에 그릴 사본: 범위 안 위치와 피벗, 다른 프레임 원본(<paramref name="frameSource"/>)을 채운다
    /// (범위 밖이면 가까운 끝).
    /// </summary>
    public AnimatedEffect AtFrame(int frameIndex, int frameCount, System.Drawing.PointF? pivot, Func<int, PixelImage?>? frameSource = null)
    {
        var (from, to) = Frames is { } r ? (r.From, r.To) : (0, Math.Max(0, frameCount - 1));
        return this with { FrameStep = Math.Clamp(frameIndex - from, 0, to - from), FrameSpan = to - from, PivotPoint = pivot, FrameSource = frameSource };
    }

    /// <summary>
    /// 반복: 한 번 = 그대로, N번 반복 = 0→1을 N번(경계 프레임은 끝 1), 왕복 N번 = 0→1→0을 N번. 시작(t = 0)은 늘 0.
    /// </summary>
    public static double Cycle(double t, EffectRepeat repeat, int count)
    {
        t = Math.Clamp(t, 0, 1);
        if (repeat == EffectRepeat.Once || t <= 0) return t;
        double v = t * Math.Max(1, count);
        if (repeat == EffectRepeat.Loop) return v - Math.Ceiling(v) + 1;
        double f = v - Math.Floor(v);
        return 1 - Math.Abs(1 - 2 * f);
    }

    public static double Ease(double u, EffectEasing easing) => easing switch
    {
        EffectEasing.EaseOut => 1 - (1 - u) * (1 - u),
        EffectEasing.EaseIn => u * u,
        EffectEasing.EaseInOut => u * u * (3 - 2 * u),
        _ => u,
    };
}

/// <summary>터지기의 중심.</summary>
public enum ExplodeCenter
{
    /// <summary>보이는 그림의 가운데.</summary>
    Middle,
    /// <summary>피벗(부위면 부위 피벗, 그 밖에는 프레임 피벗).</summary>
    Pivot,
    /// <summary>보이는 그림의 아래 가운데(위로 솟구침).</summary>
    BottomCenter,
    /// <summary>직접 (<see cref="ExplodeEffect.CenterX"/>, <see cref="ExplodeEffect.CenterY"/>) 픽셀.</summary>
    Custom,
}

/// <summary>
/// 터지기: 보이는 픽셀(또는 <see cref="PieceSize"/>×<see cref="PieceSize"/> 조각)이 중심에서 바깥쪽으로 튀어 날아가며 사라진다.
/// 조각마다 방향 = 중심 → 조각 + 흩어짐만큼의 무작위 각도, 거리 = <see cref="Distance"/> × (0.4 + 0.6 × 중심에서 떨어진 정도) × (1 − 흩어짐 × 0.6 × 무작위) × 진행,
/// 아래로 <see cref="Gravity"/> × 시간²(끝에서 중력 px만큼 더 떨어짐). 무작위는 <see cref="Seed"/>와 조각 자리로 정해져 늘 같다.
/// 진행 0(범위 첫 프레임)은 원래 그림 그대로. 조각은 정수 픽셀에 놓이고 알파만 흐려진다(번짐 없음).
/// </summary>
public sealed record ExplodeEffect : AnimatedEffect
{
    public const int MaxDistance = 128, MaxGravity = 64, MaxPieceSize = 4, MaxFlashFrames = 8, MaxSeed = 9999;

    private readonly int _distance = 16, _spread = 50, _gravity = 6, _piece = 1, _fadeStart = 40, _seed = 1, _flash;
    private readonly int _cx, _cy;

    public ExplodeEffect() => Easing = EffectEasing.EaseOut;

    /// <summary>거리: 끝까지 날아가는 최대 px(1~<see cref="MaxDistance"/>).</summary>
    public int Distance { get => _distance; init => _distance = Math.Clamp(value, 1, MaxDistance); }

    public ExplodeCenter Center { get; init; } = ExplodeCenter.Middle;

    /// <summary>직접 중심: 원본(셀·그룹이면 캔버스) 왼쪽 위 기준 픽셀.</summary>
    public int CenterX { get => _cx; init => _cx = Math.Clamp(value, -PixelImage.MaxSize, PixelImage.MaxSize); }
    public int CenterY { get => _cy; init => _cy = Math.Clamp(value, -PixelImage.MaxSize, PixelImage.MaxSize); }

    /// <summary>흩어짐 0~100%: 방향(±90° × %)과 빠르기(최대 60% 느리게)의 무작위.</summary>
    public int Spread { get => _spread; init => _spread = Math.Clamp(value, 0, 100); }

    /// <summary>중력: 끝에서 더 떨어지는 px(음수 = 떠오름), ±<see cref="MaxGravity"/>.</summary>
    public int Gravity { get => _gravity; init => _gravity = Math.Clamp(value, -MaxGravity, MaxGravity); }

    /// <summary>조각 크기 1~<see cref="MaxPieceSize"/>px(원본 왼쪽 위 기준 격자).</summary>
    public int PieceSize { get => _piece; init => _piece = Math.Clamp(value, 1, MaxPieceSize); }

    /// <summary>사라짐: 켜면 <see cref="FadeStart"/>% 진행부터 알파가 줄어 끝에서 0.</summary>
    public bool Fade { get; init; } = true;

    /// <summary>흐려지기 시작하는 진행 0~100%.</summary>
    public int FadeStart { get => _fadeStart; init => _fadeStart = Math.Clamp(value, 0, 100); }

    /// <summary>작아지기: 조각(2px 이상)이 끝으로 갈수록 작아진다.</summary>
    public bool Shrink { get; init; }

    /// <summary>시드 0~<see cref="MaxSeed"/>(같으면 같은 흩어짐).</summary>
    public int Seed { get => _seed; init => _seed = Math.Clamp(value, 0, MaxSeed); }

    /// <summary>원본 남기기: 켜면 원래 그림은 그 자리에 남고 복사본이 날아간다.</summary>
    public bool KeepOriginal { get; init; }

    /// <summary>번쩍임: 시작 다음 프레임부터 이 프레임 수 동안 조각을 <see cref="FlashColor"/> 쪽으로 물들인다(점점 약하게, 0 = 없음).</summary>
    public int FlashFrames { get => _flash; init => _flash = Math.Clamp(value, 0, MaxFlashFrames); }

    public Color32 FlashColor { get; init; } = Color32.White;

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_Explode;
}

/// <summary>
/// 빛 지나가기: 빛 띠가 <see cref="Angle"/> 방향으로 보이는 픽셀 위를 지나간다(동전·단추의 반짝임). 진행 0에는 그림 한쪽 바로 밖, 1에는 반대쪽 바로 밖.
/// 보이는 픽셀에만 스크린(밝게 더하기)으로 색 쪽으로 <see cref="Strength"/>%만큼, 알파는 그대로. 부드러움은 띠 양옆 1~2px을 계단식으로 약하게.
/// </summary>
public sealed record ShineEffect : AnimatedEffect
{
    public const int MaxWidth = 32, MaxSoftness = 2;

    private readonly int _angle = 45, _width = 3, _strength = 80, _soft;

    /// <summary>지나가는 방향(도, 0 = 오른쪽, 90 = 위, 45 = 왼쪽 아래 → 오른쪽 위).</summary>
    public int Angle { get => _angle; init => _angle = ((value % 360) + 360) % 360; }

    /// <summary>띠 폭 1~<see cref="MaxWidth"/>px.</summary>
    public int Width { get => _width; init => _width = Math.Clamp(value, 1, MaxWidth); }

    /// <summary>빛 색(알파는 쓰지 않음).</summary>
    public Color32 Color { get; init; } = Color32.White;

    /// <summary>세기 0~100%.</summary>
    public int Strength { get => _strength; init => _strength = Math.Clamp(value, 0, 100); }

    /// <summary>부드러움 0(딱딱한 띠)~<see cref="MaxSoftness"/>px(양옆 계단식 가장자리).</summary>
    public int Softness { get => _soft; init => _soft = Math.Clamp(value, 0, MaxSoftness); }

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_LightSweep;
}

/// <summary>늘었다 줄기의 기준점(이 점은 움직이지 않음).</summary>
public enum SquashAnchor
{
    /// <summary>보이는 그림의 아래 가운데(발이 땅에 붙음).</summary>
    BottomCenter,
    /// <summary>보이는 그림의 가운데.</summary>
    Middle,
    /// <summary>피벗(부위면 부위 피벗, 그 밖에는 프레임 피벗).</summary>
    Pivot,
}

/// <summary>
/// 늘었다 줄기(젤리): 가로 배율 1 + 가로 세기·f(t), 세로 배율 1 − 세로 세기·f(t). f는 0에서 시작해 <see cref="Bounces"/>번 출렁이며 줄어들어 끝(t = 1)에서 0인 감쇠 사인
/// (첫 봉우리 = 1). 옆으로 먼저면 먼저 납작·넓게, 위로 먼저면 먼저 좁고 길게. 최근접 샘플링이라 픽셀이 또렷하다.
/// </summary>
public sealed record SquashEffect : AnimatedEffect
{
    public const int MaxAmount = 50, MaxBounces = 3;

    private readonly int _widthAmount = 20, _heightAmount = 20, _bounces = 2;

    /// <summary>가로 최대 배율 변화 0~<see cref="MaxAmount"/>%(0 = 가로 고정).</summary>
    public int WidthAmount { get => _widthAmount; init => _widthAmount = Math.Clamp(value, 0, MaxAmount); }

    /// <summary>세로 최대 배율 변화 0~<see cref="MaxAmount"/>%(0 = 세로 고정).</summary>
    public int HeightAmount { get => _heightAmount; init => _heightAmount = Math.Clamp(value, 0, MaxAmount); }

    public SquashAnchor Anchor { get; init; } = SquashAnchor.BottomCenter;

    /// <summary>true = 위로 먼저(좁고 길게 → 넓게), false = 옆으로 먼저(넓고 낮게 → 좁고 길게).</summary>
    public bool StretchFirst { get; init; }

    /// <summary>튕김 횟수 1~<see cref="MaxBounces"/>(출렁임 수).</summary>
    public int Bounces { get => _bounces; init => _bounces = Math.Clamp(value, 1, MaxBounces); }

    public override string DisplayName => Gpixel.Core.Localization.DomainStrings.Effect_SquashStretch;

    private const double Damping = 2.5;
    private static readonly double[] Peaks = Enumerable.Range(1, MaxBounces).Select(b =>
        Enumerable.Range(0, 1001).Max(i => Math.Sin(2 * Math.PI * b * i / 1000.0) * Math.Exp(-Damping * i / 1000.0))).ToArray();

    /// <summary>
    /// 진행 <paramref name="u"/>(0~1)의 출렁임 f: sin(2π·튕김·u)·e^(−2.5u)를 첫 봉우리가 1이 되게 나눈 값(양수 = 옆으로 넓게, 순서가 위로 먼저면 부호 반대).
    /// u = 0, 1에서 0.
    /// </summary>
    public double Wobble(double u)
    {
        if (u <= 0 || u >= 1) return 0;
        double f = Math.Clamp(Math.Sin(2 * Math.PI * Bounces * u) * Math.Exp(-Damping * u) / Peaks[Bounces - 1], -1, 1);
        return StretchFirst ? -f : f;
    }
}
