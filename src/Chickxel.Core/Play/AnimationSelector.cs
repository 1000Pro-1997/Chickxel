namespace Chickxel.Core.Play;

/// <summary>바라보는 방향(4방향).</summary>
public enum Facing
{
    Down,
    Up,
    Left,
    Right,
}

/// <summary>재생할 애니메이션 이름과 좌우 반전 여부.</summary>
public readonly record struct AnimationChoice(string Name, bool FlipX);

/// <summary>
/// 이름 규칙으로 애니메이션을 고른다: <c>{이름}_{방향}</c>(down/up/left/right) → 반대쪽 좌우를 반전
/// (오른쪽이 없으면 왼쪽을 뒤집어 씀, 반대도 같음) → 방향 없는 <c>{이름}</c>(오른쪽을 본다고 보고 왼쪽이면 반전).
/// </summary>
public static class AnimationSelector
{
    public static string DirectionName(Facing facing) => facing switch
    {
        Facing.Up => "up",
        Facing.Left => "left",
        Facing.Right => "right",
        _ => "down",
    };

    /// <summary>
    /// 후보 이름들을 순서대로 시도해 처음 찾은 것을 돌려준다. 없으면 null.
    /// </summary>
    /// <param name="baseNames">예: ["walk", "run"].</param>
    /// <param name="hasClip">그 이름의 애니메이션이 있는지.</param>
    /// <param name="plainFacesRight">방향 없는 애니메이션이 오른쪽을 보고 그려졌는지(false면 왼쪽).</param>
    public static AnimationChoice? Resolve(IEnumerable<string> baseNames, Facing facing, Func<string, bool> hasClip,
        bool plainFacesRight = true)
    {
        foreach (var b in baseNames)
        {
            var exact = $"{b}_{DirectionName(facing)}";
            if (hasClip(exact)) return new AnimationChoice(exact, false);
            if (facing == Facing.Right && hasClip($"{b}_left")) return new AnimationChoice($"{b}_left", true);
            if (facing == Facing.Left && hasClip($"{b}_right")) return new AnimationChoice($"{b}_right", true);
            if (hasClip(b))
            {
                bool flip = facing switch
                {
                    Facing.Left => plainFacesRight,
                    Facing.Right => !plainFacesRight,
                    _ => false,
                };
                return new AnimationChoice(b, flip);
            }
        }
        return null;
    }
}
