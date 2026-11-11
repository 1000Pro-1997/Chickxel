using Chickxel.Core.Localization;

namespace Chickxel.Core.IO;

/// <summary>
/// 예전 판(1.14.117~1.14.122)이 세트 모음 파일에 넣어 둔 기본 세트의 이름표. 지금 기본 세트는 받은 세트로 설치하는 공식 패키지다(<see cref="Assets.OfficialShapeSets"/>).
/// 예전 세트 파일에 남은 이 세트는 고치지 않은 공식 세트 사본이면 읽을 때 빼고, 아니면 사용자 세트로 남기며 이름이 없으면 <see cref="RetiredName"/>을 붙인다
/// (<see cref="StampLibrary.Parse"/>). 같은 이름표를 새 세트에 다시 쓰지 않는다.
/// </summary>
public static class StampBuiltIns
{
    public static readonly IReadOnlyList<string> RetiredIds =
    [
        "leaves", "autumn", "grass", "stars", "bubbles",
        "pine", "flowers", "petals", "sparkles", "dots", "clouds", "rocks", "bricks", "rain", "snow", "hearts", "puffs", "wood", "cracks", "treasure",
    ];

    /// <summary>예전 기본 세트(<see cref="RetiredIds"/>)의 이름(지금 언어). 아니면 null.</summary>
    public static string? RetiredName(string id) => id switch
    {
        "leaves" => DomainStrings.StampSet_Leaves,
        "autumn" => DomainStrings.StampSet_AutumnLeaves,
        "grass" => DomainStrings.StampSet_Grass,
        "stars" => DomainStrings.StampSet_Stars,
        "bubbles" => DomainStrings.StampSet_Bubbles,
        "pine" => DomainStrings.StampSet_PineNeedles,
        "flowers" => DomainStrings.StampSet_Flowers,
        "petals" => DomainStrings.StampSet_Petals,
        "sparkles" => DomainStrings.StampSet_Sparkles,
        "dots" => DomainStrings.StampSet_Dots,
        "clouds" => DomainStrings.StampSet_Clouds,
        "rocks" => DomainStrings.StampSet_Rocks,
        "bricks" => DomainStrings.StampSet_Bricks,
        "rain" => DomainStrings.StampSet_Rain,
        "snow" => DomainStrings.StampSet_Snowflakes,
        "hearts" => DomainStrings.StampSet_Hearts,
        "puffs" => DomainStrings.StampSet_Puffs,
        "wood" => DomainStrings.StampSet_WoodGrain,
        "cracks" => DomainStrings.StampSet_Cracks,
        "treasure" => DomainStrings.StampSet_Treasure,
        _ => null,
    };
}
