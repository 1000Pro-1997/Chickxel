using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;

namespace Chickxel.App;

/// <summary>
/// 그라데이션 프리셋과 에셋 저장소 사이(편집 UI: <see cref="PresetsWindow"/>). 고르는 목록은 라이브러리의 모든 그라데이션이고,
/// 저장·이름 바꾸기·삭제는 내 에셋에 한다(받은 그라데이션은 고치면 내 에셋으로 복사). 실패하면 알리고 false/null을 돌려준다.
/// </summary>
public static class GradientPresetUi
{
    /// <summary>새로 고를 수 있는 그라데이션(라이브러리 차례).</summary>
    public static IReadOnlyList<AssetEntry> Available => AppAssets.Catalog.Gradients;

    /// <summary>내 그라데이션(<see cref="AppAssets.IsMine"/> — 이름 겹침 검사·덮어쓰기 대상).</summary>
    public static IReadOnlyList<AssetEntry> Personal => AppAssets.Catalog.Gradients.Where(AppAssets.IsMine).ToArray();

    /// <summary>그라데이션을 저장한다(<paramref name="id"/>가 있으면 그 자리에, null이면 새 내 에셋으로). 즐겨찾기는 <see cref="GradientPreset.Favorite"/>를 따른다.</summary>
    public static AssetEntry? Save(Window owner, string? id, GradientPreset preset)
    {
        AssetEntry? saved = null;
        return AppAssets.Try(owner, () => saved = AppAssets.Store.SaveGradient(id, preset), UiStrings.GradientPreset_SaveError) ? saved : null;
    }

    public static bool Remove(Window owner, string id)
        => AppAssets.Try(owner, () => AppAssets.Store.Remove(id), UiStrings.GradientPreset_SaveError);

    public static bool SetFavorite(Window owner, string id, bool favorite)
        => AppAssets.Try(owner, () => AppAssets.Store.SetFavorite(id, favorite), UiStrings.GradientPreset_SaveError);

    /// <summary>기본 그라데이션을 처음 값으로 되돌리고 지운 것은 다시 넣는다(직접 만든 것과 즐겨찾기는 그대로).</summary>
    public static bool RestoreBuiltIns(Window owner)
        => AppAssets.Try(owner, AppAssets.Store.RestoreBuiltInGradients, UiStrings.GradientPreset_SaveError);
}
