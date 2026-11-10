using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Assets;

/// <summary>
/// 효과 미리보기 병아리: 효과 패키지 미리보기(<see cref="AssetPreview.Package"/>)에서 효과를 입힐 견본 그림. 그림은 홈페이지 마스코트
/// (<c>docs/assets/mascot/chick-&lt;ID&gt;.png</c>, 원래 크기)를 Core에 넣은 것이고, 레시피·스크립트 효과는 ID로 고른다.
/// 번호(<see cref="Ids"/>의 차례 + 1)는 고르는 창에 보이므로 새 병아리는 맨 뒤에만 더한다.
/// </summary>
public static class PreviewChicks
{
    /// <summary>고르지 않았거나 모르는 ID일 때의 병아리.</summary>
    public const string Default = "original";

    /// <summary>
    /// 병아리 없음: 스스로 그리는 효과(비 등)의 미리보기는 기본 병아리 크기의 빈 투명 캔버스에 입힌다. <see cref="Ids"/>(번호 붙은 병아리 그림)에는 없다.
    /// 이 값을 모르는 예전 앱은 모르는 ID로 보고 기본 병아리로 그린다.
    /// </summary>
    public const string None = "none";

    public static IReadOnlyList<string> Ids { get; } =
    [
        Default, "painter", "pencil", "palette", "easel", "paint-bucket", "designer", "heart-canvas", "cursor", "hatch",
        "knight", "wizard", "archer", "ninja", "pirate", "astronaut", "viking", "king", "gamer", "healer",
    ];

    /// <summary>고를 수 있는 값인지(<see cref="Ids"/>의 병아리나 <see cref="None"/>).</summary>
    public static bool IsKnown(string? id) => id == None || id is not null && Ids.Contains(id);

    /// <summary>
    /// 병아리 그림(새 사본). <see cref="None"/>이면 기본 병아리 크기의 빈 캔버스, 모르는 ID면 기본 병아리 — 다음 버전에서 더한 병아리를 고른 효과도 그려진다.
    /// </summary>
    public static PixelImage Load(string? id)
    {
        if (id == None)
        {
            var size = Load(Default);
            return new PixelImage(size.Width, size.Height);
        }
        string name = "Chickxel.Mascot.chick-" + (IsKnown(id) ? id : Default) + ".png";
        using var stream = typeof(PreviewChicks).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException(name);
        return PngIO.Load(stream);
    }
}
