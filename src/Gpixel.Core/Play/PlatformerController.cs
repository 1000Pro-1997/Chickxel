using System.Drawing;
using System.Numerics;

namespace Gpixel.Core.Play;

/// <summary>플랫포머 물리 설정(픽셀·초 단위, y 아래가 +).</summary>
public sealed class PlatformerSettings
{
    /// <summary>좌우 이동 속도(px/s).</summary>
    public float MoveSpeed { get; set; } = 90f;
    /// <summary>중력 가속도(px/s²).</summary>
    public float Gravity { get; set; } = 900f;
    /// <summary>점프 순간 위쪽 속도(px/s).</summary>
    public float JumpVelocity { get; set; } = 300f;
    /// <summary>최대 낙하 속도(px/s).</summary>
    public float MaxFallSpeed { get; set; } = 400f;
    /// <summary>발판을 벗어난 뒤에도 점프를 받아 주는 시간(초).</summary>
    public float CoyoteTime { get; set; } = 0.08f;
    /// <summary>착지 직전에 누른 점프를 기억하는 시간(초).</summary>
    public float JumpBufferTime { get; set; } = 0.1f;
    /// <summary>상승 중 점프 키를 떼면 위쪽 속도를 이 비율 이하로 줄인다(낮은 점프). 1이면 끔.</summary>
    public float JumpCutMultiplier { get; set; } = 0.5f;
    /// <summary>고정 시간 간격(초).</summary>
    public float FixedStep { get; set; } = 1f / 120f;
}

/// <summary>바닥/발판 상자. <see cref="OneWay"/>면 위에서만 밟히고 아래·옆에서는 통과한다.</summary>
public readonly record struct PlatformBox(RectangleF Bounds, bool OneWay = false);

/// <summary>플랫포머 입력. <see cref="JumpPressed"/>는 이번 갱신에 점프 키가 새로 눌렸을 때만 true.</summary>
public readonly record struct PlatformerInput(bool Left, bool Right, bool JumpHeld, bool JumpPressed);

/// <summary>플랫포머 애니메이션 상태.</summary>
public enum PlatformerState
{
    Idle,
    Run,
    Jump,
    Fall,
}

/// <summary>
/// 고정 시간 간격 플랫포머 물리: 중력·점프(코요테 타임·점프 버퍼·낮은 점프)·최대 낙하 속도,
/// 축별 이동 후 상자 충돌 해소. 위치는 발밑 가운데(피벗) 픽셀 좌표다.
/// </summary>
public sealed class PlatformerController
{
    private const float MaxFrameTime = 0.25f;

    private float _accumulator;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _jumpHeld;

    public PlatformerSettings Settings { get; set; } = new();
    public List<PlatformBox> Platforms { get; } = new();

    /// <summary>발밑 가운데 위치(픽셀).</summary>
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }

    /// <summary>충돌 상자 크기(발밑 가운데 기준).</summary>
    public SizeF ColliderSize { get; set; } = new(12, 16);

    public bool IsGrounded { get; private set; }
    public bool FacingRight { get; set; } = true;

    /// <summary>시작 위치(<see cref="KillY"/> 아래로 떨어지면 여기로 돌아온다).</summary>
    public Vector2 SpawnPoint { get; set; }

    /// <summary>이 y보다 아래로 떨어지면 시작 위치로 되돌린다(null이면 끔).</summary>
    public float? KillY { get; set; }

    /// <summary>현재 충돌 상자(왼쪽 위 원점 좌표).</summary>
    public RectangleF Collider => ColliderAt(Position);

    public PlatformerState State => IsGrounded
        ? (Math.Abs(Velocity.X) > 0.01f ? PlatformerState.Run : PlatformerState.Idle)
        : (Velocity.Y < 0 ? PlatformerState.Jump : PlatformerState.Fall);

    /// <summary>시작 위치로 옮기고 속도·타이머를 초기화한다.</summary>
    public void Respawn()
    {
        Position = SpawnPoint;
        Velocity = Vector2.Zero;
        IsGrounded = false;
        _accumulator = 0;
        _coyoteTimer = 0;
        _jumpBufferTimer = 0;
    }

    /// <summary>
    /// 실제 경과 시간 <paramref name="dt"/>(초)만큼 고정 간격으로 진행한다(한 번에 최대 0.25초).
    /// </summary>
    public void Update(float dt, PlatformerInput input)
    {
        if (input.JumpPressed) _jumpBufferTimer = Settings.JumpBufferTime;
        _accumulator += Math.Clamp(dt, 0, MaxFrameTime);
        float h = Math.Max(1e-4f, Settings.FixedStep);
        while (_accumulator >= h)
        {
            Step(h, input);
            _accumulator -= h;
        }
    }

    /// <summary>고정 간격 한 번 진행.</summary>
    public void Step(float h, PlatformerInput input)
    {
        var s = Settings;
        var v = Velocity;

        // 좌우
        int dir = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
        v.X = dir * s.MoveSpeed;
        if (dir != 0) FacingRight = dir > 0;

        // 점프(버퍼 + 코요테)
        if (_jumpBufferTimer > 0 && _coyoteTimer > 0)
        {
            v.Y = -s.JumpVelocity;
            _jumpBufferTimer = 0;
            _coyoteTimer = 0;
            IsGrounded = false;
        }
        // 키를 떼면 낮은 점프
        if (_jumpHeld && !input.JumpHeld && v.Y < 0 && s.JumpCutMultiplier < 1)
            v.Y = Math.Max(v.Y, -s.JumpVelocity * s.JumpCutMultiplier);
        _jumpHeld = input.JumpHeld;

        // 중력
        v.Y = Math.Min(v.Y + s.Gravity * h, s.MaxFallSpeed);

        // X 이동 후 충돌
        var pos = Position;
        pos.X += v.X * h;
        foreach (var p in Platforms)
        {
            if (p.OneWay) continue;
            var c = ColliderAt(pos);
            if (!c.IntersectsWith(p.Bounds)) continue;
            if (v.X > 0) pos.X = p.Bounds.Left - ColliderSize.Width / 2;
            else if (v.X < 0) pos.X = p.Bounds.Right + ColliderSize.Width / 2;
            v.X = 0;
        }

        // Y 이동 후 충돌
        float prevBottom = pos.Y;
        pos.Y += v.Y * h;
        bool grounded = false;
        foreach (var p in Platforms)
        {
            var c = ColliderAt(pos);
            if (!c.IntersectsWith(p.Bounds)) continue;
            if (v.Y >= 0 && (!p.OneWay || prevBottom <= p.Bounds.Top + 0.01f))
            {
                pos.Y = p.Bounds.Top;
                v.Y = 0;
                grounded = true;
            }
            else if (v.Y < 0 && !p.OneWay)
            {
                pos.Y = p.Bounds.Bottom + ColliderSize.Height;
                v.Y = 0;
            }
        }

        Position = pos;
        Velocity = v;
        IsGrounded = grounded;
        _coyoteTimer = grounded ? s.CoyoteTime : Math.Max(0, _coyoteTimer - h);
        _jumpBufferTimer = Math.Max(0, _jumpBufferTimer - h);

        if (KillY is { } kill && Position.Y > kill) Respawn();
    }

    /// <summary>
    /// 상태에 맞는 애니메이션: idle / run(없으면 walk) / jump(없으면 fall) / fall(없으면 jump), 마지막으로 idle.
    /// 왼쪽을 보면 좌우 반전(방향 이름 <c>_left/_right</c>가 있으면 그것을 우선).
    /// </summary>
    public AnimationChoice? SelectAnimation(Func<string, bool> hasClip, bool plainFacesRight = true)
    {
        string[] names = State switch
        {
            PlatformerState.Run => ["run", "walk", "idle"],
            PlatformerState.Jump => ["jump", "fall", "idle"],
            PlatformerState.Fall => ["fall", "jump", "idle"],
            _ => ["idle"],
        };
        return AnimationSelector.Resolve(names, FacingRight ? Facing.Right : Facing.Left, hasClip, plainFacesRight);
    }

    private RectangleF ColliderAt(Vector2 feet)
        => new(feet.X - ColliderSize.Width / 2, feet.Y - ColliderSize.Height, ColliderSize.Width, ColliderSize.Height);
}
