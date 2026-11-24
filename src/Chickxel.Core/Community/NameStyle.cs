using Chickxel.Core.Model;
using static Chickxel.Core.Community.RewardArt;

namespace Chickxel.Core.Community;

/// <summary>
/// 이름 효과의 움직임 종류(앱 <c>NameEffectText</c>·홈페이지 <c>rewards.js</c>가 같은 규칙으로 그림 — 규칙은 각 값의 설명).
/// 모든 움직임은 <see cref="NameStyle.PeriodMs"/>마다 되풀이되고, 움직임 줄이기 설정이면 처음 모습으로 멈춘다. 글자 폭은 바뀌지 않는다(레이아웃이 흔들리지 않게).
/// </summary>
public enum NameEffect
{
    /// <summary>글자를 왼쪽 → 오른쪽 색 띠(<see cref="NameStyle.Colors"/>)로 칠함. 주기가 0보다 크면 띠가 오른쪽으로 흐름.</summary>
    Gradient,
    /// <summary>네온 사인: 글자 색 Colors[0], 같은 색 번짐 빛이 숨 쉬듯 밝아졌다 어두워짐(0.4~1), 주기의 70%쯤 두 번 깜빡.</summary>
    Neon,
    /// <summary>물결: 색 띠로 칠하고 글자마다 위아래로 출렁임(글자 i의 높이 = −0.12em·sin(2π(t/주기 + 0.12·i))).</summary>
    Wave,
    /// <summary>
    /// 글리치: 평소엔 Colors[0] 글자(옆으로 ±0.5px 색 어긋남이 희미하게). 주기마다 한 번 약 240ms 동안 Colors[1]·Colors[2] 사본이 ±2px 어긋나고
    /// 가로 띠 1~3줄이 좌우로 밀림.
    /// </summary>
    Glitch,
    /// <summary>
    /// 순간이동: Colors[0] 글자. 주기의 0~70% 보임, 70~80% 글자가 무작위 차례로 사라지며 그 자리에 반짝(Colors[1]), 80~88% 빈자리에 반짝 점만,
    /// 88~100% 왼쪽부터 글자가 다시 나타나며 1px 위에서 내려앉음.
    /// </summary>
    Teleport,
    /// <summary>환각: 색 띠 색이 계속 돌고(색상 회전), Colors[1]·Colors[2] 반투명(40%) 잔상 둘이 반대 방향으로 ±2px 작은 원을 그리며 떠다님.</summary>
    Hallucination,
    /// <summary>불꽃: 아래 → 위 색 띠(Colors 노랑 → 주황 → 빨강), 글자마다 밝기가 일렁이고 글자 위로 불티 점이 가끔 떠오름.</summary>
    Fire,
    /// <summary>은하: 색 띠(짙은 보라 → 파랑)에 Colors[2] 은은한 빛, 글자 위에 작은 네 갈래 별이 셋씩 번갈아 반짝.</summary>
    Galaxy,
    /// <summary>무지개 전설: 흐르는 무지개 띠 + 약한 물결(0.08em) + 반짝 별 둘 + 흰 빛.</summary>
    Rainbow,
}

/// <summary>이름 효과 보상 하나(<c>name.*</c>): 움직임 종류, 색, 되풀이 길이(ms, 0 = 멈춤).</summary>
public sealed record NameStyle(string Id, NameEffect Effect, Color32[] Colors, int PeriodMs)
{
    public static IReadOnlyList<NameStyle> All { get; } =
    [
        new("name.sunrise", NameEffect.Gradient, [Hex(0xFFB347), Hex(0xFF6F91)], 0),
        new("name.ocean", NameEffect.Gradient, [Hex(0x38BDF8), Hex(0x22D3EE), Hex(0x818CF8), Hex(0x38BDF8)], 4000),
        new("name.neon", NameEffect.Neon, [Hex(0xFF4FB4)], 1600),
        new("name.wave", NameEffect.Wave, [Hex(0x6EE7B7), Hex(0x3B82F6)], 1400),
        new("name.glitch", NameEffect.Glitch, [Hex(0xE5E7EB), Hex(0xFF0055), Hex(0x00E5FF)], 2200),
        new("name.teleport", NameEffect.Teleport, [Hex(0xC4B5FD), Hex(0xFFFFFF)], 3600),
        new("name.hallucination", NameEffect.Hallucination, [Hex(0xF472B6), Hex(0x60A5FA), Hex(0xFDE047)], 3000),
        new("name.fire", NameEffect.Fire, [Hex(0xFFE066), Hex(0xFF8A1F), Hex(0xE0401A)], 1200),
        new("name.galaxy", NameEffect.Galaxy, [Hex(0x7C3AED), Hex(0x3B82F6), Hex(0xC4B5FD)], 2000),
        new("name.legend", NameEffect.Rainbow, [Hex(0xFF4D6D), Hex(0xFFB703), Hex(0x3DDC84), Hex(0x22D3EE), Hex(0x8B5CF6)], 3000),
    ];

    private static readonly Dictionary<string, NameStyle> ById = All.ToDictionary(s => s.Id, StringComparer.Ordinal);

    public static NameStyle? Find(string? id) => id is not null && ById.TryGetValue(id, out var style) ? style : null;
}
