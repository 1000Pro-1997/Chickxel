using Chickxel.App.Localization;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.App;

/// <summary>
/// 모양 펜 세트 모음을 고치는 동작(도구 설정의 세트 줄과 에셋 관리자의 모양 펜 거르기가 같이 씀): 새 세트·이름 바꾸기·지우기·기본 세트 되돌리기·내 세트로 복사·
/// 세트 파일 내보내기·가져오기, 상점에 올리기(내 에셋으로 떠 둔 사본). 고치면 <see cref="EditorContext.NotifyShapeSetsEdited"/>로 알려 메인 창이 저장하고
/// 도구 설정과 에셋 관리자가 목록을 다시 그린다. 묻는 창·오류 상자는 <c>owner</c> 위에 뜬다.
/// </summary>
internal static class ShapeSetActions
{
    /// <summary>내 세트: 세트 모음에서 기본 세트 다섯 가지(<see cref="StampBuiltIns.Ids"/>)를 뺀 것(직접 만든 세트와 예전 기본 세트에서 남은 세트).</summary>
    public static IEnumerable<StampSet> Own(EditorContext ctx) => ctx.ShapeSets.Sets.Where(set => !IsBuiltIn(set));

    public static bool IsBuiltIn(StampSet set) => StampBuiltIns.Ids.Contains(set.Id);

    public static FileFilter SetFileFilter => new(UiStrings.ShapePen_SetFileType, "*" + StampLibrary.SetFileExtension);

    /// <summary>이름을 물어(처음 값 "새 세트 n") 빈 세트를 끝에 더하고 고른다. 취소하면 null.</summary>
    public static StampSet? New(Visual owner, EditorContext ctx)
    {
        var names = ctx.ShapeSets.Sets.Select(s => s.DisplayName).ToHashSet();
        int n = 1;
        while (names.Contains(UiStrings.FormatShapePen_NewSetName(number: n))) n++;
        if (InputDialog.Ask(owner, UiStrings.ShapePen_NewSetTip, UiStrings.ShapePen_SetNamePrompt, UiStrings.FormatShapePen_NewSetName(number: n)) is not { } name)
            return null;
        var set = new StampSet { Name = name };
        ctx.ShapeSets.Sets.Add(set);
        ctx.ShapePenSetId = set.Id;
        ctx.NotifyShapeSetsEdited();
        return set;
    }

    /// <summary>세트 이름을 묻고 바꾼다(받은 세트는 바꾸지 않음). 바꿨으면 true.</summary>
    public static bool Rename(Visual owner, EditorContext ctx, StampSet set)
    {
        if (ctx.IsInstalledShapeSet(set)) return false;
        if (InputDialog.Ask(owner, UiStrings.ShapePen_RenameSetTip, UiStrings.ShapePen_SetNamePrompt, set.DisplayName) is not { } name || name == set.DisplayName)
            return false;
        set.Name = name;
        ctx.NotifyShapeSetsEdited();
        return true;
    }

    /// <summary>
    /// 묻고 세트를 세트 모음에서 지운다(받은 세트는 에셋 관리자의 받은 에셋 지우기로). 고른 세트였으면 그 자리의 세트를 고른다. 지웠으면 true.
    /// </summary>
    public static bool Delete(Visual owner, EditorContext ctx, StampSet set)
    {
        int at = ctx.ShapeSets.Sets.IndexOf(set);
        if (at < 0) return false;
        if (Ui.Ask(owner, UiStrings.FormatShapePen_DeleteSetConfirm(name: set.DisplayName, count: set.Shapes.Count), UiStrings.ShapePen_DeleteSetTip,
                MessageButtons.OKCancel, MessageIcon.Warning, okText: UiStrings.ShapePen_DeleteSet) != DialogResult.OK) return false;
        bool selected = ctx.ShapePenSet == set;
        ctx.ShapeSets.Sets.RemoveAt(at);
        var sets = ctx.ShapeSets.Sets;
        if (selected) ctx.ShapePenSetId = sets.Count > 0 ? sets[Math.Min(at, sets.Count - 1)].Id : null;
        ctx.NotifyShapeSetsEdited();
        return true;
    }

    /// <summary>묻고 기본 세트 다섯 가지를 처음 모양으로 되돌린다(<see cref="StampLibrary.RestoreDefaults"/> — 내 세트는 그대로). 되돌렸으면 true.</summary>
    public static bool RestoreDefaults(Visual owner, EditorContext ctx)
    {
        if (Ui.Ask(owner, UiStrings.ShapePen_RestoreDefaultsConfirm, UiStrings.ShapePen_RestoreDefaultsTip,
                MessageButtons.OKCancel, MessageIcon.Question, okText: UiStrings.ShapePen_RestoreDefaults) != DialogResult.OK) return false;
        string? current = ctx.ShapePenSetId;
        ctx.ShapeSets.RestoreDefaults();
        ctx.ShapePenSetId = current;
        ctx.NotifyShapeSetsEdited();
        return true;
    }

    /// <summary>
    /// 받은 세트(상점·패키지 파일)를 고칠 수 있는 내 세트로 복사해 끝에 더하고 고른다(새 이름표, 이름이 겹치면 " (2)" … — 받은 원본은 그대로).
    /// </summary>
    public static StampSet CopyToMine(EditorContext ctx, StampSet set)
    {
        var copy = ShapeSetAssetFormat.Copy(set, Guid.NewGuid().ToString("N"));
        ctx.ShapeSets.AddImported(copy, set.DisplayName);
        ctx.ShapePenSetId = copy.Id;
        ctx.NotifyShapeSetsEdited();
        return copy;
    }

    /// <summary>세트를 세트 파일 하나(<see cref="StampLibrary.SetFileExtension"/>)로 내보낸다(처음 파일 이름 = 보이는 이름). 저장한 경로, 취소·실패면 null(실패는 알림).</summary>
    public static string? ExportFile(Visual owner, StampSet set)
    {
        var suggested = ProjectFileName.BaseNameFor(set.DisplayName) + StampLibrary.SetFileExtension;
        if (Ui.SaveFile(owner, UiStrings.ShapePen_ExportSet, null, suggested, StampLibrary.SetFileExtension, SetFileFilter) is not { } path) return null;
        try
        {
            StampLibrary.SaveSet(set, path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Ui.Error(owner, UiStrings.FormatShapePen_ExportFailed(error: ex.Message));
            return null;
        }
    }

    /// <summary>파일이 세트 파일·PNG(모양 펜 세트로 가져올 파일)인지.</summary>
    public static bool IsSetFile(string path)
        => path.EndsWith(StampLibrary.SetFileExtension, StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 파일마다 새 세트로 끝에 더하고(덮어쓰지 않음 — 이름이 겹치면 " (2)" …, 이름표는 늘 새것) 마지막 세트를 고른다.
    /// 세트 파일은 그 세트, PNG는 투명한 둘레를 잘라 낸 모양 하나짜리 세트(이름 = 파일 이름). 읽을 수 없는 파일은 모아 오류 상자로 알린다.
    /// 가져온 세트들을 돌려준다.
    /// </summary>
    public static List<StampSet> Import(Visual owner, EditorContext ctx, IReadOnlyList<string> paths)
    {
        var imported = new List<StampSet>();
        var failed = new List<string>();
        foreach (var path in paths)
        {
            if (ReadSetFile(path) is { } set)
            {
                ctx.ShapeSets.AddImported(set, Path.GetFileNameWithoutExtension(path));
                imported.Add(set);
            }
            else failed.Add(Path.GetFileName(path));
        }
        if (imported.Count > 0)
        {
            ctx.ShapePenSetId = imported[^1].Id;
            ctx.NotifyShapeSetsEdited();
        }
        if (failed.Count > 0) Ui.Error(owner, UiStrings.FormatShapePen_ImportFailed(names: string.Join(", ", failed)));
        return imported;
    }

    /// <summary>가져온 세트들의 상태 줄 글("'이름' 세트를 가져왔습니다 (모양 n개)" …).</summary>
    public static string ImportedText(IEnumerable<StampSet> imported)
        => string.Join(" · ", imported.Select(s => UiStrings.FormatShapePen_Imported(name: s.DisplayName, count: s.Shapes.Count)));

    /// <summary>파일 하나를 새 세트로 읽는다(세트 파일, 또는 PNG 한 장 = 모양 하나). 읽을 수 없거나 모양으로 쓸 수 없으면 null.</summary>
    private static StampSet? ReadSetFile(string path)
    {
        try
        {
            if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return StampLibrary.ParseSet(File.ReadAllText(path));
            return StampLibrary.Trim(PngIO.Load(path)) is { } shape && !StampLibrary.TooBig(shape) ? new StampSet { Shapes = { shape } } : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 내 세트를 올리거나 패키지로 내보낼 때 떠 둔 내 에셋(<c>shape-set</c>, 이 컴퓨터에서 만든 것): 같은 이름(대소문자 무시)의 것. 없으면 null.
    /// 그 에셋의 상점 연결이 곧 이 세트를 올린 기록이다.
    /// </summary>
    public static AssetEntry? Snapshot(StampSet set)
    {
        string? handle = AppAssets.MyHandle;
        return AppAssets.Catalog.ShapeSets.FirstOrDefault(entry => entry.Source == AssetSources.Local && entry.IsOwnedBy(handle)
                                                                   && string.Equals(entry.Name, set.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>세트가 떠 둔 사본(<paramref name="snapshot"/>)과 달라졌는지(올린 뒤 고침).</summary>
    public static bool ChangedSince(StampSet set, AssetEntry snapshot)
        => snapshot.ContentHash != AssetContent.Hash(AssetKinds.ShapeSet, ShapeSetAsset.From(set));

    /// <summary>
    /// 세트의 지금 모습을 내 에셋으로 떠 둔다: 같은 이름으로 떠 둔 것(<see cref="Snapshot"/>)이 있으면 그 자리를 바꾸고(상점 연결은 그대로 — 다시 올리면 새 버전),
    /// 같은 내용이면 그대로 쓴다. 모양이 없거나 너무 많으면 알리고 null.
    /// </summary>
    public static AssetEntry? SaveSnapshot(Window owner, StampSet set, string title)
    {
        int max = AssetPackageLimits.Default.MaxShapesPerSet;
        if (set.Shapes.Count == 0 || set.Shapes.Count > max)
        {
            Ui.Info(owner, set.Shapes.Count == 0 ? UiStrings.ShapePen_PublishEmpty : UiStrings.FormatShapePen_PublishTooMany(max: max, count: set.Shapes.Count), title);
            return null;
        }
        var existing = Snapshot(set);
        AssetEntry? saved = null;
        return AppAssets.Try(owner, () => saved = existing is not null && !ChangedSince(set, existing) ? existing : AppAssets.Store.SaveShapeSet(existing?.Id, set),
            UiStrings.Assets_ActionError) ? saved : null;
    }

    /// <summary>내 세트를 상점에 올린다(게시 창, 로그인 필요 — <see cref="SaveSnapshot"/>으로 떠 둔 에셋을 연다). 기본 세트·받은 세트는 올리지 않는다.</summary>
    public static async Task PublishAsync(Window owner, EditorContext ctx, StampSet set)
    {
        if (ctx.IsInstalledShapeSet(set) || IsBuiltIn(set)) return;
        if (SaveSnapshot(owner, set, UiStrings.Publish_Title) is { } saved) await AssetPublishDialog.RunAsync(owner, saved.Id);
    }
}
