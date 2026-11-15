using System.Drawing;
using System.Numerics;

namespace Chickxel.Core.Play;

/// <summary>방향 입력(방향키/WASD).</summary>
public readonly record struct DirectionalInput(bool Left, bool Right, bool Up, bool Down)
{
    /// <summary>입력 방향(-1~1, y 아래가 +). 정규화하지 않음.</summary>
    public Vector2 Raw => new((Right ? 1 : 0) - (Left ? 1 : 0), (Down ? 1 : 0) - (Up ? 1 : 0));
}

/// <summary>
/// 탑뷰 8방향 이동. 위치는 픽셀 단위(y 아래가 +). 대각선 속도도 <see cref="Speed"/>와 같도록 정규화한다.
/// <see cref="Walls"/>가 있으면 <see cref="Collider"/>로 축마다 부딪쳐 벽을 따라 미끄러진다. 방향별 idle/walk 애니메이션을 고른다.
/// </summary>
public sealed class TopDownController
{
    /// <summary>한 번에 옮기는 최대 거리(px) — 빠르게 움직여도 얇은 벽을 뚫지 않게 나눠 옮긴다.</summary>
    private const float MaxSubstep = 2f;

    private float _speed = 60f;
    private readonly List<RectangleF> _near = new();

    /// <summary>캐릭터 위치(픽셀).</summary>
    public Vector2 Position { get; set; }

    /// <summary>이동 속도(px/s, 0 이상).</summary>
    public float Speed
    {
        get => _speed;
        set => _speed = Math.Max(0, value);
    }

    public Vector2 Velocity { get; private set; }
    public Facing Facing { get; set; } = Facing.Down;
    public bool IsMoving { get; private set; }

    /// <summary>위치를 가둘 영역(null이면 제한 없음).</summary>
    public RectangleF? Bounds { get; set; }

    /// <summary>지나갈 수 없는 상자들.</summary>
    public List<RectangleF> Walls { get; } = new();

    /// <summary>벽과 부딪치는 상자(<see cref="Position"/> 기준).</summary>
    public RectangleF Collider { get; set; }

    /// <summary><paramref name="dt"/>초만큼 진행한다.</summary>
    public void Update(float dt, DirectionalInput input)
    {
        var dir = input.Raw;
        IsMoving = dir != Vector2.Zero;
        if (!IsMoving)
        {
            Velocity = Vector2.Zero;
            return;
        }
        Velocity = Vector2.Normalize(dir) * Speed;
        Position = Move(Position, Velocity * Math.Max(0, dt));
        if (Bounds is { } b)
            Position = new Vector2(Math.Clamp(Position.X, b.Left, b.Right), Math.Clamp(Position.Y, b.Top, b.Bottom));
        Facing = ChooseFacing(dir, Facing);
    }

    /// <summary><paramref name="delta"/>만큼 나눠서 축마다 옮기고, 벽에 걸리면 그 축은 벽에 붙인다.</summary>
    private Vector2 Move(Vector2 pos, Vector2 delta)
    {
        if (Walls.Count == 0) return pos + delta;
        var sweep = RectangleF.Union(ColliderAt(pos), ColliderAt(pos + delta));
        sweep.Inflate(1, 1);
        _near.Clear();
        foreach (var w in Walls)
            if (w.IntersectsWith(sweep)) _near.Add(w);
        int n = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) / MaxSubstep));
        var part = delta / n;
        for (int i = 0; i < n; i++)
        {
            if (part.X != 0)
            {
                pos.X += part.X;
                foreach (var w in _near)
                    if (ColliderAt(pos).IntersectsWith(w))
                        pos.X = part.X > 0 ? w.Left - Collider.Right : w.Right - Collider.Left;
            }
            if (part.Y != 0)
            {
                pos.Y += part.Y;
                foreach (var w in _near)
                    if (ColliderAt(pos).IntersectsWith(w))
                        pos.Y = part.Y > 0 ? w.Top - Collider.Bottom : w.Bottom - Collider.Top;
            }
        }
        return pos;
    }

    private RectangleF ColliderAt(Vector2 pos) => new(pos.X + Collider.X, pos.Y + Collider.Y, Collider.Width, Collider.Height);

    /// <summary>
    /// 이동 방향에서 바라보는 방향을 정한다. 대각선이면 지금 바라보는 방향이 이동 성분 중 하나일 때 유지하고,
    /// 아니면 좌우를 택한다.
    /// </summary>
    public static Facing ChooseFacing(Vector2 dir, Facing current)
    {
        Facing? horizontal = dir.X < 0 ? Facing.Left : dir.X > 0 ? Facing.Right : null;
        Facing? vertical = dir.Y < 0 ? Facing.Up : dir.Y > 0 ? Facing.Down : null;
        if (horizontal is null) return vertical ?? current;
        if (vertical is null) return horizontal.Value;
        return current == horizontal || current == vertical ? current : horizontal.Value;
    }

    /// <summary>
    /// 현재 상태의 애니메이션: 이동 중이면 walk(없으면 run), 아니면 idle. 방향별 이름 → 좌우 반전 → 방향 없는 이름 순.
    /// 이동 애니메이션이 없으면 idle, 그것도 없으면 idle_down을 시도한다.
    /// </summary>
    public AnimationChoice? SelectAnimation(Func<string, bool> hasClip, bool plainFacesRight = true)
    {
        if (IsMoving && AnimationSelector.Resolve(["walk", "run"], Facing, hasClip, plainFacesRight) is { } move)
            return move;
        return AnimationSelector.Resolve(["idle"], Facing, hasClip, plainFacesRight)
               ?? AnimationSelector.Resolve(["idle"], Facing.Down, hasClip, plainFacesRight);
    }
}
