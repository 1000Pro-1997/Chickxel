using System.Drawing;
using System.Numerics;

namespace Chickxel.Core.Play;

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
    /// <summary>사다리·벽을 오르내리는 속도(px/s).</summary>
    public float ClimbSpeed { get; set; } = 60f;
    /// <summary>벽에 붙어 타고 오르내릴 수 있는지(<see cref="ClimbKind.Wall"/>).</summary>
    public bool WallClimb { get; set; }
    /// <summary>벽을 차고 뛴 뒤 벽 반대쪽으로 밀려나는 시간(초). 그동안은 다시 붙지 않는다.</summary>
    public float WallJumpTime { get; set; } = 0.15f;
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

/// <summary>
/// 플랫포머 입력. <see cref="JumpPressed"/>는 이번 갱신에 점프 키가 새로 눌렸을 때만 true.
/// <see cref="Up"/>은 사다리·벽을 오르는 키이고, 붙을 곳이 없으면 점프 키로도 쓴다(사다리·벽에서는 오르기만). <see cref="Down"/>은 내려가기.
/// </summary>
public readonly record struct PlatformerInput(bool Left, bool Right, bool JumpHeld, bool JumpPressed, bool Up = false, bool Down = false);

/// <summary>플랫포머 애니메이션 상태. 사다리에서 움직이면 <see cref="Climb"/>, 멈추면 <see cref="Hang"/>, 벽도 같다.</summary>
public enum PlatformerState
{
    Idle,
    Run,
    Jump,
    Fall,
    Climb,
    Hang,
    WallClimb,
    WallHang,
}

/// <summary>붙어 있는 곳: 없음, 사다리, 벽.</summary>
public enum ClimbKind
{
    None,
    Ladder,
    Wall,
}

/// <summary>
/// 고정 시간 간격 플랫포머 물리: 중력·점프(코요테 타임·점프 버퍼·낮은 점프)·최대 낙하 속도,
/// 축별 이동 후 상자 충돌 해소, 사다리(<see cref="Ladders"/> — 위·아래 키로 붙어 오르내리고 멈추면 매달림, 맨 위는 밟힘),
/// 벽타기(<see cref="PlatformerSettings.WallClimb"/> — 벽 쪽을 누르면 붙고, 반대쪽은 놓기, 점프는 벽 차고 뛰기, 위 끝에서 올라섬).
/// 위치는 발밑 가운데 픽셀 좌표다.
/// </summary>
public sealed class PlatformerController
{
    private const float MaxFrameTime = 0.25f;
    /// <summary>한 번에 옮기는 최대 거리(px) — 빠르게 움직여도 얇은 벽을 뚫지 않게 나눠 옮긴다.</summary>
    private const float MaxSubstep = 2f;
    /// <summary>사다리·벽에서 뛰어내린 뒤 다시 붙지 않는 시간(초).</summary>
    private const float RegrabTime = 0.25f;

    private float _accumulator;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    /// <summary>버퍼에 든 점프가 위 키로만 눌린 것인지(사다리·벽에서는 오르기로 씀).</summary>
    private bool _jumpFromUp;
    private bool _jumpHeld;
    private bool _upHeld;
    private float _kickTimer;
    private int _kickDir;
    private float _regrabTimer;
    private readonly List<PlatformBox> _near = new();

    public PlatformerSettings Settings { get; set; } = new();
    public List<PlatformBox> Platforms { get; } = new();

    /// <summary>사다리 상자. 맨 위는 위에서만 밟히는 발판이 된다.</summary>
    public List<RectangleF> Ladders { get; } = new();

    /// <summary>발밑 가운데 위치(픽셀).</summary>
    public Vector2 Position { get; set; }
    public Vector2 Velocity { get; set; }

    /// <summary>충돌 상자 크기(발밑 가운데 기준).</summary>
    public SizeF ColliderSize { get; set; } = new(12, 16);

    public bool IsGrounded { get; private set; }
    public bool FacingRight { get; set; } = true;

    /// <summary>지금 붙어 있는 곳.</summary>
    public ClimbKind Climbing { get; private set; }

    /// <summary>붙은 벽의 쪽(1 = 오른쪽, -1 = 왼쪽). 벽에 붙어 있을 때만 뜻이 있다.</summary>
    public int WallSide { get; private set; }

    /// <summary>시작 위치(<see cref="KillY"/> 아래로 떨어지면 여기로 돌아온다).</summary>
    public Vector2 SpawnPoint { get; set; }

    /// <summary>이 y보다 아래로 떨어지면 시작 위치로 되돌린다(null이면 끔).</summary>
    public float? KillY { get; set; }

    /// <summary>현재 충돌 상자(왼쪽 위 원점 좌표).</summary>
    public RectangleF Collider => ColliderAt(Position);

    public PlatformerState State => Climbing switch
    {
        ClimbKind.Ladder => Velocity == Vector2.Zero ? PlatformerState.Hang : PlatformerState.Climb,
        ClimbKind.Wall => Velocity.Y == 0 ? PlatformerState.WallHang : PlatformerState.WallClimb,
        _ => IsGrounded
            ? (Math.Abs(Velocity.X) > 0.01f ? PlatformerState.Run : PlatformerState.Idle)
            : (Velocity.Y < 0 ? PlatformerState.Jump : PlatformerState.Fall),
    };

    /// <summary>시작 위치로 옮기고 속도·타이머를 초기화한다.</summary>
    public void Respawn()
    {
        Position = SpawnPoint;
        Velocity = Vector2.Zero;
        IsGrounded = false;
        Climbing = ClimbKind.None;
        _accumulator = 0;
        _coyoteTimer = 0;
        _jumpBufferTimer = 0;
        _kickTimer = 0;
        _regrabTimer = 0;
    }

    /// <summary>
    /// 실제 경과 시간 <paramref name="dt"/>(초)만큼 고정 간격으로 진행한다(한 번에 최대 0.25초).
    /// </summary>
    public void Update(float dt, PlatformerInput input)
    {
        bool upPressed = input.Up && !_upHeld;
        _upHeld = input.Up;
        if (input.JumpPressed)
        {
            _jumpBufferTimer = Settings.JumpBufferTime;
            _jumpFromUp = false;
        }
        else if (upPressed)
        {
            _jumpBufferTimer = Settings.JumpBufferTime;
            _jumpFromUp = true;
        }
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
        var pos = Position;
        int dir = (input.Right ? 1 : 0) - (input.Left ? 1 : 0);
        int vert = (input.Down ? 1 : 0) - (input.Up ? 1 : 0);
        _regrabTimer = Math.Max(0, _regrabTimer - h);
        _kickTimer = Math.Max(0, _kickTimer - h);

        // 사다리·벽에 붙기(위 키로 붙으면 그 키는 점프가 아님)
        if (Climbing == ClimbKind.None && _regrabTimer <= 0)
        {
            if (Ladders.Count > 0 && (input.Up && OverlapsLadder(pos) || input.Down && LadderBelow(pos)))
                Grab(ClimbKind.Ladder, 0);
            else if (s.WallClimb && dir != 0 && (!IsGrounded || input.Up) && TouchesWall(pos, dir))
                Grab(ClimbKind.Wall, dir);
        }

        bool jumpNow = _jumpBufferTimer > 0 && !_jumpFromUp;
        if (Climbing != ClimbKind.None && _jumpBufferTimer > 0 && _jumpFromUp) _jumpBufferTimer = 0;
        if (Climbing == ClimbKind.Ladder)
        {
            if (jumpNow)
            {
                Release();
                v.Y = -s.JumpVelocity;
            }
            else
            {
                v.X = dir * s.ClimbSpeed;
                v.Y = vert * s.ClimbSpeed;
                if (dir != 0) FacingRight = dir > 0;
            }
        }
        else if (Climbing == ClimbKind.Wall)
        {
            if (jumpNow)
            {
                // 벽 차고 뛰기: 잠깐 벽 반대쪽으로 밀려난다.
                _kickDir = -WallSide;
                _kickTimer = s.WallJumpTime;
                Release();
                _regrabTimer = Math.Max(_regrabTimer, s.WallJumpTime);
                v.Y = -s.JumpVelocity;
            }
            else if (dir == -WallSide)
            {
                Release();
                _regrabTimer = 0.1f;
            }
            else
            {
                v.X = 0;
                v.Y = vert * s.ClimbSpeed;
                FacingRight = WallSide > 0;
            }
        }

        if (Climbing == ClimbKind.None)
        {
            // 좌우(벽 차고 뛴 직후에는 밀려나는 쪽)
            if (_kickTimer > 0) v.X = _kickDir * s.MoveSpeed;
            else v.X = dir * s.MoveSpeed;
            if (v.X != 0) FacingRight = v.X > 0;

            // 점프(버퍼 + 코요테)
            if (_jumpBufferTimer > 0 && _coyoteTimer > 0)
            {
                v.Y = -s.JumpVelocity;
                _jumpBufferTimer = 0;
                _coyoteTimer = 0;
                IsGrounded = false;
            }
            // 키를 떼면 낮은 점프
            bool held = input.JumpHeld || input.Up;
            if (_jumpHeld && !held && v.Y < 0 && s.JumpCutMultiplier < 1)
                v.Y = Math.Max(v.Y, -s.JumpVelocity * s.JumpCutMultiplier);

            // 중력
            v.Y = Math.Min(v.Y + s.Gravity * h, s.MaxFallSpeed);
        }
        _jumpHeld = input.JumpHeld || input.Up;

        // 나눠서 축별로 옮기고 충돌을 푼다.
        var start = pos;
        var delta = v * h;
        CollectNear(start, start + delta);
        int n = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) / MaxSubstep));
        var part = delta / n;
        bool grounded = false, hitX = false, hitY = false;
        for (int i = 0; i < n; i++)
        {
            pos.X += part.X;
            if (part.X != 0)
            {
                foreach (var p in _near)
                {
                    if (p.OneWay || !ColliderAt(pos).IntersectsWith(p.Bounds)) continue;
                    pos.X = part.X > 0 ? p.Bounds.Left - ColliderSize.Width / 2 : p.Bounds.Right + ColliderSize.Width / 2;
                    hitX = true;
                }
            }

            float prevBottom = pos.Y;
            pos.Y += part.Y;
            foreach (var p in _near)
            {
                if (!ColliderAt(pos).IntersectsWith(p.Bounds)) continue;
                if (p.OneWay && Climbing != ClimbKind.None) continue;
                if (part.Y >= 0 && (!p.OneWay || prevBottom <= p.Bounds.Top + 0.01f))
                {
                    pos.Y = p.Bounds.Top;
                    grounded = true;
                    hitY = true;
                }
                else if (part.Y < 0 && !p.OneWay)
                {
                    pos.Y = p.Bounds.Bottom + ColliderSize.Height;
                    hitY = true;
                }
            }
        }
        if (hitX) v.X = 0;
        if (hitY) v.Y = 0;

        // 붙은 곳을 벗어났는지
        if (Climbing == ClimbKind.Ladder)
        {
            if (grounded && vert > 0) Release();
            else if (!OverlapsLadder(pos))
            {
                // 위로 다 오르면 사다리 맨 위에 선다.
                if (vert < 0 && LadderTopBetween(pos.X, pos.Y, start.Y) is { } top)
                {
                    pos.Y = top;
                    grounded = true;
                }
                Release();
                v = Vector2.Zero;
            }
        }
        else if (Climbing == ClimbKind.Wall)
        {
            if (grounded && vert > 0) Release();
            else if (!TouchesWall(pos, WallSide))
            {
                // 위 끝까지 오르면 벽 위로 올라선다(자리가 비어 있으면).
                if (vert < 0 && WallTop(start, WallSide) is { } top)
                {
                    var onTop = new Vector2(pos.X + WallSide * ColliderSize.Width, top);
                    if (!HitsSolid(onTop))
                    {
                        pos = onTop;
                        grounded = true;
                    }
                }
                Release();
                v = Vector2.Zero;
            }
        }

        Position = pos;
        Velocity = v;
        IsGrounded = grounded;
        _coyoteTimer = grounded ? s.CoyoteTime : Math.Max(0, _coyoteTimer - h);
        _jumpBufferTimer = Math.Max(0, _jumpBufferTimer - h);

        if (KillY is { } kill && Position.Y > kill) Respawn();

        void Grab(ClimbKind kind, int side)
        {
            Climbing = kind;
            WallSide = side;
            v = Vector2.Zero;
            _kickTimer = 0;
            if (_jumpFromUp) _jumpBufferTimer = 0;
        }

        void Release()
        {
            Climbing = ClimbKind.None;
            _jumpBufferTimer = 0;
            _coyoteTimer = 0;
            _regrabTimer = RegrabTime;
        }
    }

    /// <summary>
    /// 상태에 맞는 애니메이션 이름 순서: idle / run(없으면 walk) / jump(없으면 fall) / fall(없으면 jump), 사다리 climb·hang, 벽 wall_climb·wall_hang
    /// (없으면 사다리 이름), 마지막으로 idle.
    /// </summary>
    public static string[] AnimationNames(PlatformerState state) => state switch
    {
        PlatformerState.Run => ["run", "walk", "idle"],
        PlatformerState.Jump => ["jump", "fall", "idle"],
        PlatformerState.Fall => ["fall", "jump", "idle"],
        PlatformerState.Climb => ["climb", "idle"],
        PlatformerState.Hang => ["hang", "climb", "idle"],
        PlatformerState.WallClimb => ["wall_climb", "climb", "idle"],
        PlatformerState.WallHang => ["wall_hang", "wall_climb", "hang", "climb", "idle"],
        _ => ["idle"],
    };

    /// <summary>
    /// 상태에 맞는 애니메이션(<see cref="AnimationNames"/> 차례). 왼쪽을 보면 좌우 반전(방향 이름 <c>_left/_right</c>가 있으면 그것을 우선).
    /// </summary>
    public AnimationChoice? SelectAnimation(Func<string, bool> hasClip, bool plainFacesRight = true)
        => AnimationSelector.Resolve(AnimationNames(State), FacingRight ? Facing.Right : Facing.Left, hasClip, plainFacesRight);

    private RectangleF ColliderAt(Vector2 feet)
        => new(feet.X - ColliderSize.Width / 2, feet.Y - ColliderSize.Height, ColliderSize.Width, ColliderSize.Height);

    /// <summary>이번 걸음에 닿을 수 있는 발판(사다리는 위에서만 밟히는 발판으로)을 <see cref="_near"/>에 모은다.</summary>
    private void CollectNear(Vector2 from, Vector2 to)
    {
        var sweep = RectangleF.Union(ColliderAt(from), ColliderAt(to));
        sweep.Inflate(2, 2);
        _near.Clear();
        foreach (var p in Platforms)
            if (p.Bounds.IntersectsWith(sweep)) _near.Add(p);
        foreach (var l in Ladders)
            if (l.IntersectsWith(sweep)) _near.Add(new PlatformBox(l, OneWay: true));
    }

    /// <summary>몸 가운데 세로줄이 사다리에 걸쳐 있는지.</summary>
    private bool OverlapsLadder(Vector2 feet)
    {
        float top = feet.Y - ColliderSize.Height;
        foreach (var l in Ladders)
            if (l.Left <= feet.X && feet.X < l.Right && l.Top < feet.Y && l.Bottom > top) return true;
        return false;
    }

    /// <summary>발 바로 아래가 사다리인지(맨 위에 서서 내려가기).</summary>
    private bool LadderBelow(Vector2 feet)
    {
        foreach (var l in Ladders)
            if (l.Contains(feet.X, feet.Y + 0.5f)) return true;
        return false;
    }

    /// <summary>x 줄에서 윗변이 <paramref name="top"/>~<paramref name="bottom"/> 사이인 사다리의 윗변(없으면 null).</summary>
    private float? LadderTopBetween(float x, float top, float bottom)
    {
        foreach (var l in Ladders)
            if (l.Left <= x && x < l.Right && l.Top >= top - 0.01f && l.Top <= bottom + 0.01f) return l.Top;
        return null;
    }

    /// <summary>몸 옆(<paramref name="side"/> 쪽) 바로 붙은 단단한 상자가 있는지.</summary>
    private bool TouchesWall(Vector2 feet, int side)
    {
        var strip = WallStrip(feet, side);
        foreach (var p in Platforms)
            if (!p.OneWay && p.Bounds.IntersectsWith(strip)) return true;
        return false;
    }

    /// <summary>몸 옆에 붙은 단단한 상자 중 가장 높은 윗변(없으면 null).</summary>
    private float? WallTop(Vector2 feet, int side)
    {
        var strip = WallStrip(feet, side);
        float? top = null;
        foreach (var p in Platforms)
            if (!p.OneWay && p.Bounds.IntersectsWith(strip)) top = Math.Min(top ?? float.MaxValue, p.Bounds.Top);
        return top;
    }

    private RectangleF WallStrip(Vector2 feet, int side)
    {
        var c = ColliderAt(feet);
        return new RectangleF(side > 0 ? c.Right : c.Left - 0.5f, c.Top, 0.5f, c.Height);
    }

    private bool HitsSolid(Vector2 feet)
    {
        var c = ColliderAt(feet);
        foreach (var p in Platforms)
            if (!p.OneWay && p.Bounds.IntersectsWith(c)) return true;
        return false;
    }
}
