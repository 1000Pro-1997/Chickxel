using System.Drawing;
using System.Numerics;

namespace Gpixel.Core.Play;

/// <summary>방향 입력(방향키/WASD).</summary>
public readonly record struct DirectionalInput(bool Left, bool Right, bool Up, bool Down)
{
    /// <summary>입력 방향(-1~1, y 아래가 +). 정규화하지 않음.</summary>
    public Vector2 Raw => new((Right ? 1 : 0) - (Left ? 1 : 0), (Down ? 1 : 0) - (Up ? 1 : 0));
}

/// <summary>
/// 탑뷰 8방향 이동. 위치는 픽셀 단위(y 아래가 +). 대각선 속도도 <see cref="Speed"/>와 같도록 정규화한다.
/// 방향별 idle/walk 애니메이션을 고른다.
/// </summary>
public sealed class TopDownController
{
    private float _speed = 60f;

    /// <summary>캐릭터 위치(피벗이 놓이는 점, 픽셀).</summary>
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
        Position += Velocity * Math.Max(0, dt);
        if (Bounds is { } b)
            Position = new Vector2(Math.Clamp(Position.X, b.Left, b.Right), Math.Clamp(Position.Y, b.Top, b.Bottom));
        Facing = ChooseFacing(dir, Facing);
    }

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
