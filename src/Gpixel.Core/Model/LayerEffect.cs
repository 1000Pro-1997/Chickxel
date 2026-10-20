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

    /// <summary>프레임 <paramref name="frameIndex"/>가 범위 안인지(켜짐 여부와 무관).</summary>
    public bool AppliesTo(int frameIndex) => Frames is not { } r || r.Contains(frameIndex);

    /// <summary>화면에 보일 이름.</summary>
    public abstract string DisplayName { get; }
}

/// <summary>프레임 번호 범위(양 끝 포함, 0 ≤ <see cref="From"/> ≤ <see cref="To"/>).</summary>
public readonly record struct FrameRange
{
    public FrameRange(int from, int to)
    {
        if (from < 0 || to < from) throw new ArgumentOutOfRangeException(nameof(from), $"프레임 범위가 잘못되었습니다: {from}~{to}");
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

    public override string DisplayName => "외곽선";
}

/// <summary>그림자: 그림 모양 그대로(단단한 그림자) 한 색으로 (X, Y)px 옮겨 뒤에 깐다.</summary>
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

    public override string DisplayName => "그림자";
}

/// <summary>빛이 오는 방향(가장자리 빛·그늘).</summary>
public enum LightDirection
{
    TopLeft, Top, TopRight,
    Left, Right,
    BottomLeft, Bottom, BottomRight,
}

/// <summary>
/// 가장자리 빛·그늘: 빛 쪽을 향한 가장자리 픽셀(그 방향 이웃이 투명)에 빛 색을, 반대쪽 가장자리에 그늘 색을 1px 안쪽으로 덧칠한다(알파는 그대로).
/// 대각선 방향이면 그 두 축 중 하나라도 비면 가장자리다. 한 픽셀이 양쪽 다 해당하면 빛이 이긴다. 색 알파 0 = 그쪽은 칠하지 않음.
/// </summary>
public sealed record RimLightEffect : LayerEffect
{
    public Color32 Light { get; init; } = new(255, 255, 255, 160);
    public Color32 Shade { get; init; } = new(0, 0, 0, 96);
    public LightDirection Direction { get; init; } = LightDirection.TopLeft;

    public override string DisplayName => "가장자리 빛·그늘";

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

/// <summary>색 덮기: 보이는 픽셀의 색을 <see cref="Amount"/>%만큼 한 색 쪽으로(100% = 실루엣), 알파는 그대로. 색의 알파는 쓰지 않는다.</summary>
public sealed record ColorOverlayEffect : LayerEffect
{
    private readonly int _amount = 100;

    public Color32 Color { get; init; } = Color32.White;

    /// <summary>0~100(%).</summary>
    public int Amount { get => _amount; init => _amount = Math.Clamp(value, 0, 100); }

    public override string DisplayName => "색 덮기";
}
