namespace Gpixel.Core.AiDraw;

/// <summary>
/// 애니메이션 동작 이름과 기본 프레임 수, 동작마다 열쇠 자세(모델에게 보낼 영어 설명 — 옆모습, 오른쪽을 봄). 되풀이 동작(대기·걷기·달리기)은
/// 마지막 프레임 다음이 첫 프레임으로 이어지고, 한 번 하는 동작(공격·점프·피격·쓰러짐)은 첫 자세에서 마지막 자세까지 간다.
/// </summary>
public static class AiDrawActions
{
    public const string Idle = "idle", Walk = "walk", Run = "run", Attack = "attack", Jump = "jump", Hurt = "hurt", Death = "death", Custom = "custom";

    /// <summary>설정 칸에 프레임 수를 보이는 동작(이 순서).</summary>
    public static IReadOnlyList<string> All { get; } = [Idle, Walk, Run, Attack, Jump, Hurt, Death];

    public static int DefaultFrames(string action) => action switch
    {
        Idle => 4,
        Walk or Run or Attack or Jump => 6,
        Hurt => 3,
        Death => 5,
        _ => 4,
    };

    public static Dictionary<string, int> DefaultFrameCounts() => All.ToDictionary(a => a, DefaultFrames);

    /// <summary>동작의 프레임 길이(ms).</summary>
    public static int DurationMs(string? action) => action switch
    {
        Idle => 150,
        Run or Attack => 80,
        Jump => 90,
        Death => 120,
        _ => 100,
    };

    /// <summary>되풀이 동작(마지막 프레임 다음이 첫 프레임).</summary>
    public static bool Loops(string action) => action is Idle or Walk or Run;

    /// <summary>알려진 동작 이름이면 소문자 이름, 아니면 null.</summary>
    public static string? Parse(string? text)
    {
        var t = text?.Trim().ToLowerInvariant();
        return t is not null && (All.Contains(t) || t == Custom) ? t : null;
    }

    /// <summary>동작의 열쇠 자세(알려지지 않은 동작은 빈 목록).</summary>
    public static IReadOnlyList<string> KeyPoses(string action) => action switch
    {
        Idle =>
        [
            "neutral relaxed standing pose, arms at the sides",
            "breathing in: chest and shoulders raised by about one pixel, head slightly up",
            "neutral relaxed standing pose, arms at the sides",
            "breathing out: body slightly lowered and squashed by about one pixel, shoulders down",
        ],
        Walk =>
        [
            "contact: right leg forward with the heel touching the ground, left leg behind on its toes, left arm swung forward, right arm back",
            "down: weight on the bent right leg, body at its lowest point, left foot lifting off behind",
            "passing: right leg straight under the body, left leg bent and passing forward, arms close to the body, body at its highest point",
            "contact: left leg forward with the heel touching the ground, right leg behind on its toes, right arm swung forward, left arm back",
            "down: weight on the bent left leg, body at its lowest point, right foot lifting off behind",
            "passing: left leg straight under the body, right leg bent and passing forward, arms close to the body, body at its highest point",
        ],
        Run =>
        [
            "right foot striking the ground ahead, body leaning forward, left arm forward and right arm back, elbows bent",
            "right leg pushing off behind, left knee driving high forward, body leaning forward",
            "airborne at the highest point, both feet off the ground, legs spread front and back",
            "left foot striking the ground ahead, body leaning forward, right arm forward and left arm back, elbows bent",
            "left leg pushing off behind, right knee driving high forward, body leaning forward",
            "airborne at the highest point, both feet off the ground, legs spread front and back (other leg forward)",
        ],
        Attack =>
        [
            "ready combat stance, weapon or fists raised in front",
            "anticipation: winding up, weapon or arm pulled far back, body twisted away, weight on the back foot",
            "swing begins: weapon or arm moving forward fast, body turning toward the target",
            "strike: full extension at the moment of impact, weapon or arm fully forward, weight on the front foot",
            "follow-through: weapon or arm past the target, body still leaning forward",
            "recovering back toward the ready combat stance",
        ],
        Jump =>
        [
            "crouching anticipation: knees deeply bent, arms back, body low",
            "take-off: legs extending and pushing off the ground, arms swinging up, body stretching upward",
            "rising in the air: legs tucked up, arms up",
            "peak of the jump: body compact and highest, legs tucked",
            "falling: legs extending down toward the ground, arms out for balance",
            "landing: feet on the ground, knees bent absorbing the impact, body low",
        ],
        Hurt =>
        [
            "hit recoil: knocked back, head and torso leaning backward, pained expression, arms flung out",
            "recoil peak: leaning back the most, eyes squeezed shut",
            "recovering: straightening back toward the normal standing pose",
        ],
        Death =>
        [
            "hit hard: staggering backward, pained expression",
            "knees buckling, body collapsing, arms dropping",
            "falling backward toward the ground",
            "hitting the ground on the back, limbs loose",
            "lying flat on the ground, motionless, eyes closed",
        ],
        _ => [],
    };
}

/// <summary>생성할 프레임 하나: 번호(0부터), 전체 수, 자세 설명(빈 글이면 자세 지시 없음).</summary>
public sealed record AiDrawFrame(int Index, int Count, string Pose);

/// <summary>애니메이션 프레임 계획: 프레임마다 그릴 자세.</summary>
public static class AiDrawPlanner
{
    /// <summary>
    /// 프레임 계획. <paramref name="action"/>이 없으면 프레임 하나(자세 = <paramref name="poses"/>의 첫 줄). 있으면 <paramref name="frameCount"/>개
    /// (<see cref="AiDrawSettings.MinFrames"/>~<see cref="AiDrawSettings.MaxFrames"/>): 주어진 자세(<paramref name="poses"/>)를 앞에서부터 쓰고,
    /// 모자라면 동작의 열쇠 자세를 고르게 나눠 쓴다(되풀이 동작은 끝이 처음으로 이어지게, 열쇠 사이에 떨어지면 중간 자세). 열쇠 자세가 없는 동작은
    /// "몇 번째 프레임" 설명.
    /// </summary>
    public static IReadOnlyList<AiDrawFrame> Plan(string? action, int frameCount, IReadOnlyList<string>? poses)
    {
        var given = (poses ?? []).Select(p => (p ?? "").Trim()).ToList();
        if (string.IsNullOrWhiteSpace(action)) return [new AiDrawFrame(0, 1, given.FirstOrDefault(p => p.Length > 0) ?? "")];
        action = action.Trim().ToLowerInvariant();
        int n = Math.Clamp(frameCount, AiDrawSettings.MinFrames, AiDrawSettings.MaxFrames);
        var keys = AiDrawActions.KeyPoses(action);
        bool loop = AiDrawActions.Loops(action) || (keys.Count == 0 && action != AiDrawActions.Custom && !IsOneShotWord(action));
        var frames = new List<AiDrawFrame>(n);
        for (int i = 0; i < n; i++)
        {
            string pose = i < given.Count && given[i].Length > 0 ? given[i]
                : keys.Count > 0 ? Sample(keys, i, n, loop)
                : Generic(action, i, n, loop);
            frames.Add(new AiDrawFrame(i, n, pose));
        }
        return frames;
    }

    /// <summary>
    /// 열쇠 자세 <paramref name="keys"/>에서 프레임 <paramref name="i"/>/<paramref name="n"/>의 자세: 자리 t(되풀이 = i·K/n, 한 번 = i·(K−1)/(n−1))가
    /// 열쇠에 가까우면(±0.25) 그 열쇠, 아니면 앞뒤 열쇠 사이의 중간 자세.
    /// </summary>
    internal static string Sample(IReadOnlyList<string> keys, int i, int n, bool loop)
    {
        int k = keys.Count;
        if (k == 1 || n == 1) return keys[0];
        double t = loop ? (double)i * k / n : (double)i * (k - 1) / (n - 1);
        int k0 = (int)Math.Floor(t);
        double f = t - k0;
        int Next(int a) => loop ? (a + 1) % k : Math.Min(a + 1, k - 1);
        if (f < 0.25) return keys[k0 % k];
        if (f > 0.75) return keys[Next(k0) % k];
        return $"in-between pose halfway from \"{keys[k0 % k]}\" to \"{keys[Next(k0) % k]}\"";
    }

    private static bool IsOneShotWord(string action)
        => action.Contains("attack") || action.Contains("slash") || action.Contains("cast") || action.Contains("die") || action.Contains("hit");

    private static string Generic(string action, int i, int n, bool loop)
        => $"frame {i + 1} of {n} of the \"{action}\" motion, advancing the motion evenly from the previous frame"
           + (loop ? (i == n - 1 ? "; this last frame must lead smoothly back into frame 1 (looping)" : "") : i == n - 1 ? "; the motion ends here" : "");
}
