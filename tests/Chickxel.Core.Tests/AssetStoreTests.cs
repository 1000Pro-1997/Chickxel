using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 로컬 에셋 저장소: 패키지 파일의 에셋을 하나씩 넣기(에셋마다 작성자·라이선스·상점 연결), 겹침 묻기, 내 에셋 저장·지우기·즐겨찾기,
/// 받은 에셋을 그 자리에서 고치기, 내보내기 왕복, 끊긴 가져오기 복구, 손상된 library.json, 프로젝트와의 독립.
/// </summary>
public class AssetStoreTests
{
    private const string Id = AssetSamples.PackageId;

    private static string RootOf(TempDir dir) => Path.Combine(dir.Path, "Assets");

    /// <summary>버전과 외곽선 두께만 다른 패키지(레시피 + 그라데이션 — 두께로 어느 버전인지 본다).</summary>
    private static string Package(TempDir dir, string version, int thickness)
        => AssetSamples.Save(dir.Path, AssetSamples.Build(AssetSamples.Header(version), AssetSamples.Recipe(thickness: thickness)), $"pack-{version}-{thickness}.chickxel-asset");

    /// <summary>레시피의 외곽선 두께(가져오면 예전 외곽선이 공식 외곽선 스크립트로 바뀐다).</summary>
    private static int Thickness(AssetEntry recipe)
    {
        var outline = Assert.IsType<ScriptEffect>(recipe.Recipe!.Effects[0]);
        Assert.Equal(OfficialEffects.HashOf("outline"), outline.ScriptHash);
        return Convert.ToInt32(outline.Settings["thickness"]);
    }

    private static int ReceivedThickness(AssetStore store) => Thickness(Assert.Single(store.Catalog.Recipes, e => e.Store?.PackageId == Id));

    /// <summary>개인 내보내기 머리(앱의 "내보내기"와 같은 모양: 작성자 personal, ID personal.*).</summary>
    private static AssetManifest PersonalHeader { get; } = AssetSamples.Header() with
    {
        Id = "personal.backup", Name = "Backup", Author = new AssetAuthor(AssetStore.PersonalAuthorId, "Me"), License = "LicenseRef-Personal",
    };

    private static readonly AssetAuthor PixelCat = new("pixel-cat", "Pixel Cat");

    private static readonly string ScriptSource = OfficialEffects.Source("shine") + "\n// " + nameof(AssetStoreTests) + "\n";

    private static string Export(AssetStore store, TempDir dir, IEnumerable<string> ids, AssetManifest? header = null, AssetAuthor? localAuthor = null,
        string name = "export.chickxel-asset")
    {
        using var output = new MemoryStream();
        store.Export(ids, header ?? PersonalHeader, output, localAuthor);
        return AssetSamples.Save(dir.Path, output.ToArray(), name);
    }

    /// <summary>폴더 아래 모든 파일·폴더(상대 경로 → 내용 해시). 저장소가 바꾸는 동안 쥐는 잠금 파일은 뺀다(상태가 아님).</summary>
    private static SortedDictionary<string, string> Tree(string root)
        => new(Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Where(path => !IsLock(root, path)).ToDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            path => File.Exists(path) ? AssetPackageWriter.Hash(File.ReadAllBytes(path)) : "<dir>"), StringComparer.Ordinal);

    private static bool IsLock(string root, string path) => Path.GetRelativePath(root, path) == AssetStore.LockFileName;

    /// <summary>끊긴 순간의 저장소 사본(바꾸는 중이라 쥐고 있는 잠금 파일은 빼고 — 프로세스가 죽으면 잠금도 풀린다).</summary>
    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories).Where(file => !IsLock(from, file)))
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    }

    /// <summary>에셋이 가리키는 딸린 파일 폴더 이름(미리보기·라이선스 문서·장면).</summary>
    private static string[] UsedFolders(AssetStore store)
        => store.Catalog.All.SelectMany(e => new[] { e.PreviewPath, e.LicenseFilePath }).OfType<string>()
            .Select(path => Path.GetFileName(Path.GetDirectoryName(path))!).Distinct().Order(StringComparer.Ordinal).ToArray();

    private static string[] FileFolders(AssetStore store)
        => Directory.GetDirectories(store.FilesDirectory).Select(d => Path.GetFileName(d)).Order(StringComparer.Ordinal).ToArray();

    // ───────────── 가져오기와 카탈로그 ─────────────

    [Fact]
    public void Install_AddsEachAssetSeparately_WithThePackageMetadata_AndSurvivesReopen()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        int changed = 0;
        store.Changed += () => changed++;
        var local = store.SaveRecipe(EffectRecipe.Capture("", "내 효과", [new FadeEffect()]));

        var installed = store.InstallFile(Package(dir, "1.0.0", 2));
        Assert.True(changed >= 2);
        Assert.Equal([Id + ".neon-hit", Id + ".neon-blue"], installed.Select(e => e.Id).ToArray());
        Assert.Equal([local.Id, Id + ".neon-hit", Id + ".neon-blue"], store.Catalog.All.Select(e => e.Id).ToArray());
        Assert.Equal([AssetKinds.EffectRecipe, AssetKinds.Gradient], installed.Select(e => e.Kind).ToArray());
        Assert.All(installed, e =>
        {
            Assert.Equal((AssetSamples.Header().Author, "CC0-1.0", AssetSources.File, false), (e.Author, e.License, e.Source, e.Modified));
            Assert.Equal((Id, e.Id, "1.0.0", "Neon Pack", "네온 효과 묶음", e.ContentHash),
                (e.Store!.PackageId, e.Store.AssetId, e.Store.Version, e.Store.DisplayName, e.Store.DisplayDescription, e.Store.ContentHash));
            Assert.Equal("CC0", File.ReadAllText(e.LicenseFilePath!));
            Assert.True(File.Exists(e.PreviewPath));
            // 받은 에셋은 작성자 핸들일 때만 내 것
            Assert.Equal((true, false, false), (e.IsOwnedBy("author"), e.IsOwnedBy("someone"), e.IsOwnedBy(null)));
        });
        Assert.Equal(2, Thickness(installed[0]));
        AssetSamples.AssertSameGradient(AssetSamples.Gradient(), installed[1].Gradient!);
        // 이 컴퓨터에서 만든 것은 누구에게나 내 것
        Assert.StartsWith(AssetStore.LocalIdPrefix, local.Id);
        Assert.Equal(((AssetAuthor?)null, (AssetStoreLink?)null, AssetSources.Local, true, true),
            (local.Author, local.Store, local.Source, local.IsOwnedBy(null), local.IsOwnedBy("someone")));
        Assert.Empty(Directory.GetFileSystemEntries(store.StagingDirectory));

        var reopened = AssetStore.Open(store.Root);
        Assert.Empty(reopened.LoadErrors);
        static object Key(AssetEntry e) => (e.Id, e.Author, e.License, e.Source, e.AddedUtc, e.Store?.PackageId, e.Store?.Version, e.Store?.ContentHash, e.Modified,
            e.PreviewPath, e.LicenseFilePath);
        Assert.Equal(store.Catalog.All.Select(Key), reopened.Catalog.All.Select(Key));
        AssetSamples.AssertSameRecipe(installed[0].Recipe!, reopened.Catalog.Find(installed[0].Id)!.Recipe!);

        // 지우면 딸린 파일도 지운다
        reopened.Remove(installed[0].Id);
        Assert.False(Directory.Exists(Path.GetDirectoryName(installed[0].PreviewPath)));
        Assert.Equal([local.Id, Id + ".neon-blue"], AssetStore.Open(store.Root).Catalog.All.Select(e => e.Id).ToArray());
    }

    // ───────────── 겹침 ─────────────

    /// <summary>같은 상점 에셋(패키지 ID·에셋 ID)을 다시 가져오면 에셋마다 묻는다: 건너뛰기는 아무것도 바꾸지 않고, 바꾸기는 ID·즐겨찾기를 지키며, 둘 다 두기는 새 ID로 더한다.</summary>
    [Fact]
    public void Install_SameStoreAsset_AsksPerAsset_SkipReplaceKeepBoth()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var first = store.InstallFile(Package(dir, "1.0.0", 2));
        store.SetFavorite(first[0].Id, true);
        var before = Tree(store.Root);

        var asked = new List<AssetImportItem>();
        Assert.Empty(store.Install(Package(dir, "1.1.0", 3), AssetSources.Remote, item =>
        {
            asked.Add(item);
            return ImportChoice.Skip;
        }));
        Assert.Equal(before, Tree(store.Root));
        Assert.Equal([first[0].Id, first[1].Id], asked.Select(i => i.Existing!.Id).ToArray());
        Assert.All(asked, i => Assert.True(i.SameStoreAsset));
        Assert.Equal([false, true], asked.Select(i => i.SameContent).ToArray());
        Assert.All(asked, i => Assert.Equal(("1.1.0", "author", "CC0-1.0"), (i.Store!.Version, i.Author!.Id, i.License)));
        Assert.Equal("Neon Hit", asked[0].DisplayName);

        var replaced = store.Install(Package(dir, "1.1.0", 3), AssetSources.Remote, _ => ImportChoice.Replace);
        Assert.Equal([first[0].Id, first[1].Id], replaced.Select(e => e.Id).ToArray());
        Assert.Equal(2, store.Catalog.All.Count);
        Assert.Equal(3, ReceivedThickness(store));
        var recipe = store.Catalog.Find(first[0].Id)!;
        Assert.Equal(("1.1.0", AssetSources.Remote, true, false), (recipe.Store!.Version, recipe.Source, recipe.Favorite, recipe.Modified));
        Assert.Equal(UsedFolders(store), FileFolders(store));

        var both = store.Install(Package(dir, "1.0.0", 2), AssetSources.File, _ => ImportChoice.KeepBoth);
        Assert.Equal(2, both.Count);
        Assert.All(both, e => Assert.DoesNotContain(e.Id, first.Select(f => f.Id)));
        Assert.All(both, e => Assert.Equal((Id, "1.0.0", false), (e.Store!.PackageId, e.Store.Version, e.Favorite)));
        Assert.Equal(4, store.Catalog.FromPackage(Id).Count);
        Assert.Equal([3, 2], store.Catalog.Recipes.Select(Thickness).ToArray());
        Assert.Equal(store.Catalog.All.Select(e => e.Id), AssetStore.Open(store.Root).Catalog.All.Select(e => e.Id));
    }

    [Fact]
    public void InstallRemote_HigherVersion_ReplacesModifiedLinkedEffect_AndRemovesDuplicateLinks()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var first = store.InstallRemote(Package(dir, "1.0.0", 2), Id, "1.0.0");
        store.SetFavorite(first[0].Id, true);
        var duplicate = store.Install(Package(dir, "1.0.0", 2), AssetSources.File, _ => ImportChoice.KeepBoth);
        Assert.NotEqual(first[0].Id, duplicate[0].Id);
        var edited = store.SaveRecipe(AssetSamples.Recipe(thickness: 1) with { Id = first[0].Id });
        Assert.True(edited.Modified);
        Assert.Equal(1, Thickness(store.Catalog.Find(first[0].Id)!));

        var updated = store.InstallRemote(Package(dir, "1.1.0", 3), Id, "1.1.0", _ => throw new Xunit.Sdk.XunitException("Linked remote updates must not prompt."));
        Assert.Equal(first.Select(e => e.Id), updated.Select(e => e.Id));
        Assert.Equal(2, store.Catalog.FromPackage(Id).Count);
        Assert.Equal(3, ReceivedThickness(store));
        Assert.Equal(("1.1.0", AssetSources.Remote, true, false),
            (updated[0].Store!.Version, updated[0].Source, updated[0].Favorite, updated[0].Modified));
        Assert.Null(store.Catalog.Find(duplicate[0].Id));
        Assert.Null(store.Catalog.Find(duplicate[1].Id));
        Assert.Equal(UsedFolders(store), FileFolders(store));

        var before = Tree(store.Root);
        Assert.Empty(store.InstallRemote(Package(dir, "1.1.0", 4), Id, "1.1.0"));
        Assert.Empty(store.InstallRemote(Package(dir, "1.0.0", 2), Id, "1.0.0"));
        Assert.Equal(before, Tree(store.Root));
        Assert.Equal(3, ReceivedThickness(store));
    }

    [Fact]
    public void InstallRemote_RejectsCatalogIdentityOrVersionMismatch_WithoutChangingLibrary()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        string file = Package(dir, "1.0.0", 2);
        var before = Tree(store.Root);
        Assert.Throws<InvalidDataException>(() => store.InstallRemote(file, "author.other-pack", "1.0.0"));
        Assert.Throws<InvalidDataException>(() => store.InstallRemote(file, Id, "1.1.0"));
        Assert.Equal(before, Tree(store.Root));
        Assert.Empty(store.Catalog.All);
    }

    [Fact]
    public void InstallRemote_SameOrLowerVersion_CleansOldLinksWithoutRollingBackNewerContent()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var current = store.InstallRemote(Package(dir, "1.1.0", 3), Id, "1.1.0");
        var older = store.Install(Package(dir, "1.0.0", 2), AssetSources.File, _ => ImportChoice.KeepBoth);
        store.SetFavorite(older[0].Id, true);
        store.SaveRecipe(AssetSamples.Recipe(thickness: 1) with { Id = current[0].Id });

        Assert.Empty(store.InstallRemote(Package(dir, "1.0.0", 2), Id, "1.0.0",
            _ => throw new Xunit.Sdk.XunitException("Linked remote versions must not prompt.")));
        Assert.Equal(2, store.Catalog.FromPackage(Id).Count);
        var retained = store.Catalog.Find(current[0].Id)!;
        Assert.Equal(("1.1.0", true, true, 1),
            (retained.Store!.Version, retained.Favorite, retained.Modified, Thickness(retained)));
        Assert.Null(store.Catalog.Find(older[0].Id));
        Assert.Equal(UsedFolders(store), FileFolders(store));

        var before = Tree(store.Root);
        Assert.Empty(store.InstallRemote(Package(dir, "1.1.0", 4), Id, "1.1.0"));
        Assert.Equal(before, Tree(store.Root));
        Assert.Equal(1, ReceivedThickness(store));
    }

    [Fact]
    public void InstallRemote_SingleAssetRenamedInNewVersion_KeepsLocalId()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        string PackageWithName(string version, string name, int thickness)
        {
            var builder = new AssetPackageBuilder(AssetSamples.Header(version));
            builder.Add(AssetSamples.Recipe(name, thickness));
            using var output = new MemoryStream();
            builder.Write(output);
            return AssetSamples.Save(dir.Path, output.ToArray(), $"single-{version}.chickxel-asset");
        }

        var first = Assert.Single(store.InstallRemote(PackageWithName("1.0.0", "Old name", 2), Id, "1.0.0"));
        store.SetFavorite(first.Id, true);
        var updated = Assert.Single(store.InstallRemote(PackageWithName("1.1.0", "New name", 3), Id, "1.1.0"));
        Assert.Equal((first.Id, "New name", true, "1.1.0"), (updated.Id, updated.Name, updated.Favorite, updated.Store!.Version));
        Assert.Equal(3, Thickness(updated));
        Assert.Single(store.Catalog.FromPackage(Id));
    }

    /// <summary>상점 연결이 없는 가져오기(개인 내보내기 파일)는 같은 종류·같은 내용의 에셋과 겹친다.</summary>
    [Fact]
    public void Install_SameContentWithoutStoreLink_IsASameContentConflict()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var mine = store.SaveGradient(null, AssetSamples.Gradient("Mine"));
        string file = Export(store, dir, [mine.Id]);

        AssetImportItem? asked = null;
        Assert.Empty(store.Install(file, AssetSources.File, item =>
        {
            asked = item;
            return ImportChoice.Skip;
        }));
        Assert.NotNull(asked);
        Assert.Equal((mine.Id, true, false, (AssetAuthor?)null, (AssetStoreLink?)null), (asked.Existing!.Id, asked.SameContent, asked.SameStoreAsset, asked.Author, asked.Store));
        Assert.Single(store.Catalog.All);

        var copy = Assert.Single(store.Install(file, AssetSources.File, _ => ImportChoice.KeepBoth));
        Assert.NotEqual(mine.Id, copy.Id);
        Assert.Equal(["Mine", "Mine"], store.Catalog.Gradients.Select(e => e.Name).ToArray());
    }

    // ───────────── 내 에셋 ─────────────

    [Fact]
    public void LocalAssets_SaveInPlace_Favorite_AndRemove()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var recipe = store.SaveRecipe(AssetSamples.Recipe("내 네온"));
        Assert.StartsWith(AssetStore.LocalIdPrefix, recipe.Id);
        var renamed = store.SaveRecipe(recipe.Recipe! with { Name = "새 이름" });
        Assert.Equal(recipe.Id, renamed.Id);
        var gradient = store.SaveGradient(null, AssetSamples.Gradient("Mine") with { Favorite = true });
        Assert.True(gradient.Favorite);

        var reopened = AssetStore.Open(store.Root);
        Assert.Equal([recipe.Id, gradient.Id], reopened.Catalog.All.Select(e => e.Id).ToArray());
        Assert.Equal("새 이름", reopened.Catalog.Recipes.Single().Name);
        Assert.True(reopened.Catalog.Find(gradient.Id)!.Gradient!.Favorite);
        Assert.All(reopened.Catalog.All, e => Assert.Equal((AssetSources.Local, (AssetAuthor?)null, (AssetStoreLink?)null), (e.Source, e.Author, e.Store)));

        store.SetFavorite(recipe.Id, true);
        store.SaveGradient(gradient.Id, gradient.Gradient! with { Favorite = false });
        Assert.Equal([true, false], AssetStore.Open(store.Root).Catalog.All.Select(e => e.Favorite).ToArray());

        store.Remove(recipe.Id);
        store.Remove("personal.missing");
        var after = AssetStore.Open(store.Root);
        Assert.Equal([gradient.Id], after.Catalog.All.Select(e => e.Id).ToArray());
        Assert.DoesNotContain(after.Catalog.All, e => e.Favorite);
    }

    /// <summary>받은 스크립트를 그 자리에서 고치면(내 것일 때) 작성자·상점 연결은 남고 고친 것으로 보이며, 받은 미리보기는 지금 내용과 맞지 않아 뺀다.</summary>
    [Fact]
    public void SaveScript_InPlaceOnReceivedAsset_KeepsAuthorAndStoreLink_IsModified_AndDropsTheReceivedPreview()
    {
        using var dir = new TempDir();
        var program = ScriptEffectProgram.Parse(ScriptSource).Program!;
        var asset = ScriptAsset.From("", program);
        var builder = new AssetPackageBuilder(AssetSamples.Header());
        builder.Add(AssetKinds.EffectScript, asset, AssetPreview.Package(asset));
        builder.AddDocument("LICENSE.txt", "CC0");
        byte[] bytes;
        using (var output = new MemoryStream())
        {
            builder.Write(output);
            bytes = output.ToArray();
        }
        var store = AssetStore.Open(RootOf(dir));
        var received = Assert.Single(store.InstallFile(AssetSamples.Save(dir.Path, bytes)));
        Assert.NotNull(received.PreviewPath);

        string editedSource = ScriptSource.Replace("빛 지나가기", "고친 빛", StringComparison.Ordinal);
        var edited = store.SaveScript(editedSource, received.Id, null);
        Assert.Equal((received.Id, received.Author, received.License, AssetSources.File, true, "고친 빛"),
            (edited.Id, edited.Author, edited.License, edited.Source, edited.Modified, edited.Name));
        Assert.Equal((received.Store!.PackageId, received.Store.Version, received.Store.ContentHash), (edited.Store!.PackageId, edited.Store.Version, edited.Store.ContentHash));
        Assert.Null(edited.PreviewPath);
        Assert.Equal("CC0", File.ReadAllText(edited.LicenseFilePath!));
        Assert.Equal(UsedFolders(store), FileFolders(store));
        var reopened = AssetStore.Open(store.Root).Catalog.Find(received.Id)!;
        Assert.Equal((true, ScriptEffectProgram.HashOf(editedSource), (string?)null), (reopened.Modified, reopened.Script!.Hash, reopened.PreviewPath));

        // 받은 원문으로 되돌리면 고치지 않은 것
        Assert.False(store.SaveScript(ScriptSource, received.Id, null).Modified);
        // ID 없이 같은 원문을 저장하면 받은 에셋을 쓰지 않고 내 사본을 만든다
        var copy = store.SaveScript(ScriptSource, null, null);
        Assert.NotEqual(received.Id, copy.Id);
        Assert.Equal(((AssetAuthor?)null, (AssetStoreLink?)null, (string?)null), (copy.Author, copy.Store, copy.License));
    }

    // ───────────── 내보내기와 개인 내보내기 파일 ─────────────

    /// <summary>개인 내보내기 파일(작성자 personal, ID personal.*)의 패키지 정보는 작성자·상점 연결로 보지 않지만, 에셋 줄에 적힌 작성자·라이선스·상점 연결은 그대로 쓴다.</summary>
    [Fact]
    public void PersonalBundle_ImportsAsLocal_ButHonorsPerEntryMetadata()
    {
        using var dir = new TempDir();
        var recipe = AssetSamples.Recipe();
        var link = new AssetStoreLink
        {
            PackageId = "pixel-cat.neon", AssetId = "pixel-cat.neon.neon-hit", Version = "1.2.0", ContentHash = new string('a', 64), Name = "Neon",
            MinAppVersion = "1.0.0",
        };
        var builder = new AssetPackageBuilder(PersonalHeader);
        builder.Add(AssetSamples.Gradient("Plain"));
        builder.Add(AssetKinds.EffectRecipe, recipe, author: PixelCat, license: "MIT", store: link);
        builder.Add(AssetKinds.EffectScript, ScriptAsset.From("", ScriptEffectProgram.Parse(ScriptSource).Program!), author: PixelCat);
        using var output = new MemoryStream();
        builder.Write(output);

        var store = AssetStore.Open(RootOf(dir));
        var installed = store.InstallFile(AssetSamples.Save(dir.Path, output.ToArray()));
        Assert.Equal(3, installed.Count);
        var (plain, received, script) = (installed[0], installed[1], installed[2]);

        Assert.StartsWith(AssetStore.LocalIdPrefix, plain.Id);
        Assert.Equal(((AssetAuthor?)null, (string?)null, (AssetStoreLink?)null, true), (plain.Author, plain.License, plain.Store, plain.IsOwnedBy("anyone")));
        Assert.Equal((link.AssetId, PixelCat, "MIT", link.PackageId, "1.2.0", link.ContentHash),
            (received.Id, received.Author, received.License, received.Store!.PackageId, received.Store.Version, received.Store.ContentHash));
        // 적힌 내용 해시와 다르면 고친 것으로 이어진다
        Assert.True(received.Modified);
        Assert.Equal((PixelCat, (string?)null, (AssetStoreLink?)null), (script.Author, script.License, script.Store));
        Assert.StartsWith(AssetStore.LocalIdPrefix, script.Id);
    }

    /// <summary>고치지 않은 상점 에셋 하나는 원래 상점 패키지 그대로 내보내, 새 저장소에 넣어도 같은 상점 에셋(같은 ID·버전·작성자·라이선스 문서)이다.</summary>
    [Fact]
    public void Export_OneUnmodifiedStoreAsset_RoundTripsToTheSameStoreIdentity()
    {
        using var dir = new TempDir();
        var a = AssetStore.Open(Path.Combine(dir.Path, "A"));
        var gradient = a.InstallFile(Package(dir, "1.0.0", 2))[1];
        using var output = new MemoryStream();
        var manifest = a.Export([gradient.Id], PersonalHeader, output, PixelCat);
        Assert.Equal((Id, "1.0.0", "Neon Pack", AssetSamples.Header().Author, "CC0-1.0"), (manifest.Id, manifest.Version, manifest.Name, manifest.Author, manifest.License));

        var b = AssetStore.Open(Path.Combine(dir.Path, "B"));
        var copy = Assert.Single(b.InstallFile(AssetSamples.Save(dir.Path, output.ToArray(), "one.chickxel-asset")));
        Assert.Equal((gradient.Id, gradient.Author, gradient.License, false, gradient.ContentHash), (copy.Id, copy.Author, copy.License, copy.Modified, copy.ContentHash));
        Assert.Equal((gradient.Store!.PackageId, gradient.Store.AssetId, gradient.Store.Version, gradient.Store.Name, gradient.Store.ContentHash),
            (copy.Store!.PackageId, copy.Store.AssetId, copy.Store.Version, copy.Store.Name, copy.Store.ContentHash));
        Assert.Equal("CC0", File.ReadAllText(copy.LicenseFilePath!));
        // 다시 내보내도 같은 바이트(같은 상점 패키지)
        using var again = new MemoryStream();
        b.Export([copy.Id], PersonalHeader, again);
        Assert.Equal(output.ToArray(), again.ToArray());
    }

    /// <summary>여러 에셋(또는 고친 상점 에셋)은 개인 내보내기 머리 아래 에셋마다 작성자·라이선스·상점 연결을 적는다. 이 컴퓨터에서 만든 것의 작성자는 <c>localAuthor</c>.</summary>
    [Fact]
    public void Export_Bundle_RoundTripsPerEntryMetadata_AndGivesLocalAssetsTheLocalAuthor()
    {
        using var dir = new TempDir();
        var a = AssetStore.Open(Path.Combine(dir.Path, "A"));
        var mine = a.SaveGradient(null, AssetSamples.Gradient("Mine"));
        var installed = a.InstallFile(Package(dir, "1.0.0", 2));
        var recipe = a.SaveRecipe(installed[0].Recipe! with { Name = "고친 네온" });
        Assert.True(recipe.Modified);
        var gradient = installed[1];

        using var output = new MemoryStream();
        var manifest = a.Export([mine.Id, recipe.Id, gradient.Id], PersonalHeader, output, PixelCat);
        Assert.Equal(PersonalHeader.Id, manifest.Id);
        Assert.Equal([PixelCat, AssetSamples.Header().Author, AssetSamples.Header().Author], manifest.Assets.Select(e => e.Author).ToArray());
        Assert.Equal([null, "CC0-1.0", "CC0-1.0"], manifest.Assets.Select(e => e.License).ToArray());
        Assert.Equal([null, recipe.Store!.ContentHash, gradient.Store!.ContentHash], manifest.Assets.Select(e => e.Store?.ContentHash).ToArray());

        var b = AssetStore.Open(Path.Combine(dir.Path, "B"));
        var copies = b.InstallFile(AssetSamples.Save(dir.Path, output.ToArray(), "bundle.chickxel-asset"));
        Assert.Equal(3, copies.Count);
        Assert.StartsWith(AssetStore.LocalIdPrefix, copies[0].Id);
        Assert.Equal((PixelCat, (string?)null, (AssetStoreLink?)null), (copies[0].Author, copies[0].License, copies[0].Store));
        Assert.Equal((recipe.Id, recipe.Author, recipe.License, "고친 네온", true), (copies[1].Id, copies[1].Author, copies[1].License, copies[1].Name, copies[1].Modified));
        Assert.Equal((recipe.Store.PackageId, recipe.Store.Version, recipe.Store.ContentHash), (copies[1].Store!.PackageId, copies[1].Store!.Version, copies[1].Store!.ContentHash));
        Assert.Equal((gradient.Id, false, gradient.Store.Version), (copies[2].Id, copies[2].Modified, copies[2].Store!.Version));

        // 계정이 없으면 이 컴퓨터에서 만든 것은 받는 쪽에서도 작성자 없음(그쪽에서 만든 것)
        var c = AssetStore.Open(Path.Combine(dir.Path, "C"));
        var anonymous = c.InstallFile(Export(a, dir, [mine.Id, gradient.Id], name: "anonymous.chickxel-asset"));
        Assert.Equal(((AssetAuthor?)null, (string?)null, (AssetStoreLink?)null), (anonymous[0].Author, anonymous[0].License, anonymous[0].Store));
        Assert.Equal((gradient.Id, gradient.Author), (anonymous[1].Id, anonymous[1].Author));
    }

    /// <summary>이 앱이 모르는 효과가 든 레시피는 패키지로 내보낼 수 없고, 출력에는 아무것도 쓰지 않는다.</summary>
    [Fact]
    public void Export_WithUnknownEffect_FailsWithoutWritingOutput()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var unknown = Chickxel.Core.Effects.EffectCodec.Read(JsonNode.Parse("""{ "type": "chickxel.effect.future", "version": 1, "settings": {} }""")!.AsObject());
        Assert.IsType<MissingEffect>(unknown);
        var recipe = store.SaveRecipe(EffectRecipe.Capture("", "Future", [new FadeEffect(), unknown]));

        using var output = new MemoryStream();
        var ex = Assert.Throws<AssetPackageException>(() => store.Export([recipe.Id], AssetSamples.Header(), output));
        Assert.Contains(ex.Issues, i => i.Code == AssetIssueCode.UnknownEffect);
        Assert.Equal(0, output.Length);
    }

    // ───────────── 실패·취소·끊긴 가져오기 ─────────────

    [Fact]
    public void FailedValidation_LeavesStoreUnchanged()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        store.InstallFile(Package(dir, "1.0.0", 2));
        var before = Tree(store.Root);

        var entries = AssetSamples.Unzip(AssetSamples.Build(AssetSamples.Header("1.1.0"), AssetSamples.Recipe(thickness: 3)));
        entries.Add(("README.md", "목록에 없음"u8.ToArray()));
        string broken = AssetSamples.Save(dir.Path, AssetSamples.Zip(entries), "broken.chickxel-asset");
        var ex = Assert.Throws<AssetPackageException>(() => store.Install(broken, AssetSources.File, _ => ImportChoice.Replace));
        Assert.Equal(AssetIssueCode.UnlistedFile, Assert.Single(ex.Issues).Code);

        Assert.Equal(before, Tree(store.Root));
        Assert.Equal(2, ReceivedThickness(store));
        Assert.Equal(2, ReceivedThickness(AssetStore.Open(store.Root)));
    }

    /// <summary>파일을 쓰기 전·딸린 파일을 쓴 뒤 취소하거나 디스크 쓰기가 실패하면 저장소는 그대로다(새로 쓴 딸린 파일도 지움).</summary>
    [Theory]
    [InlineData("staged", false)]
    [InlineData("files", false)]
    [InlineData("staged", true)]
    [InlineData("files", true)]
    public void CancellationOrWriteFailure_LeavesStoreUnchanged(string step, bool fail)
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        store.InstallFile(Package(dir, "1.0.0", 2));
        var before = Tree(store.Root);

        using var cancel = new CancellationTokenSource();
        store.StepHook = name =>
        {
            if (name != step) return;
            if (fail) throw new IOException("디스크가 가득 참");
            cancel.Cancel();
        };
        var thrown = Record.Exception(() => store.Install(Package(dir, "1.1.0", 3), AssetSources.File, _ => ImportChoice.Replace, cancel.Token));
        Assert.True(fail ? thrown is IOException : thrown is OperationCanceledException, thrown?.ToString());
        Assert.Equal(before, Tree(store.Root));
        Assert.Equal(2, ReceivedThickness(store));
        Assert.Equal(before, Tree(AssetStore.Open(store.Root).Root));

        // 미리 취소된 토큰
        store.StepHook = null;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => store.Install(Package(dir, "1.1.0", 3), AssetSources.File, _ => ImportChoice.Replace, cancelled.Token));
        Assert.Equal(before, Tree(store.Root));
    }

    /// <summary>
    /// 새 버전을 가져오면서 단계마다 저장소 전체를 복사해 둔다(그 순간 프로세스가 죽은 것과 같음). 각 사본을 열면 library.json을 쓰기 전이면 이전 값,
    /// 쓴 뒤면 새 값이고, Staging과 쓰이지 않는 딸린 파일 폴더는 치운다.
    /// </summary>
    [Theory]
    [InlineData("staged")]
    [InlineData("files")]
    [InlineData("committed")]
    public void InterruptedInstall_ReopensToConsistentState(string step)
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var local = store.SaveRecipe(EffectRecipe.Capture("", "내 효과", [new FadeEffect()]));
        store.InstallFile(Package(dir, "1.0.0", 1));
        string snapshot = Path.Combine(dir.Path, "snapshot-" + step);
        var seen = new List<string>();
        store.StepHook = name =>
        {
            seen.Add(name);
            if (name == step) CopyTree(store.Root, snapshot);
        };
        store.Install(Package(dir, "1.1.0", 3), AssetSources.File, _ => ImportChoice.Replace);
        Assert.Contains(step, seen);
        // 사본에는 끊긴 가져오기의 흔적(Staging의 파일)이 있다
        Assert.NotEmpty(Directory.GetFileSystemEntries(Path.Combine(snapshot, "Staging")));

        var recovered = AssetStore.Open(snapshot);
        Assert.Equal(step == "committed" ? 3 : 1, ReceivedThickness(recovered));
        Assert.Empty(Directory.GetFileSystemEntries(recovered.StagingDirectory));
        Assert.Equal(UsedFolders(recovered), FileFolders(recovered));
        Assert.All(recovered.Catalog.All, e => Assert.True(e.Author is null || File.Exists(e.LicenseFilePath)));
        Assert.Equal(local.Id, recovered.Catalog.All[0].Id);
        Assert.Empty(recovered.LoadErrors);

        // 복구한 저장소에서 다시 가져오면 끝까지 된다
        recovered.Install(Package(dir, "1.1.0", 3), AssetSources.File, _ => ImportChoice.Replace);
        Assert.Equal(3, ReceivedThickness(recovered));
        Assert.Equal(3, recovered.Catalog.All.Count);
    }

    // ───────────── 손상된 library.json ─────────────

    [Theory]
    [InlineData("{ \"schemaVersion\": 2, \"assets\": [")]
    [InlineData("{ \"schemaVersion\": 9, \"assets\": [], \"favorites\": [] }")]
    [InlineData("{ \"schemaVersion\": 1, \"assets\": [] }")]
    [InlineData("[]")]
    public void UnreadableLibraryFile_BlocksEveryChangeWithoutOverwritingIt(string content)
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        var saved = store.SaveGradient(null, AssetSamples.Gradient("내 것"));
        store.InstallFile(Package(dir, "1.0.0", 2));
        File.WriteAllText(store.LibraryPath, content);
        var files = Tree(store.FilesDirectory);

        var broken = AssetStore.Open(store.Root);
        Assert.NotNull(broken.LibraryError);
        Assert.NotEmpty(broken.LoadErrors);
        Assert.Empty(broken.Catalog.All);
        Assert.Throws<InvalidOperationException>(() => broken.InstallFile(Package(dir, "1.1.0", 3)));
        Assert.Throws<InvalidOperationException>(() => broken.SaveRecipe(EffectRecipe.Capture("", "새 효과", [new FadeEffect()])));
        Assert.Throws<InvalidOperationException>(() => broken.SaveGradient(null, AssetSamples.Gradient("새 것")));
        Assert.Throws<InvalidOperationException>(() => broken.SaveScript(ScriptSource, null, null));
        Assert.Throws<InvalidOperationException>(() => broken.SetFavorite(saved.Id, true));
        Assert.Throws<InvalidOperationException>(() => broken.Remove(saved.Id));
        Assert.Throws<InvalidOperationException>(() => broken.RestoreBuiltInGradients());
        Assert.Throws<InvalidOperationException>(() => broken.MarkPublished(saved.Id, PixelCat, "CC0-1.0", new AssetStoreLink { PackageId = "pixel-cat.x", AssetId = "pixel-cat.x.y" }));
        // 마이그레이션도 덮어쓰지 않는다
        Assert.False(AssetMigration.Run(broken, dir.Path).Ran);
        Assert.Equal(content, File.ReadAllText(broken.LibraryPath));
        // 읽지 못한 상태로 딸린 파일을 치우지 않는다
        Assert.Equal(files, Tree(broken.FilesDirectory));
        Assert.Empty(Directory.GetFileSystemEntries(broken.StagingDirectory));
    }

    /// <summary>이 앱이 모르는 종류(미래 형식)의 줄은 카탈로그에 없지만 다른 에셋을 저장해도 원본 그대로 남는다.</summary>
    [Fact]
    public void UnknownLibraryRows_ArePreservedAcrossWrites()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        store.SaveGradient(null, AssetSamples.Gradient());
        var library = JsonNode.Parse(File.ReadAllText(store.LibraryPath))!.AsObject();
        var future = JsonNode.Parse("""{ "id": "future.thing", "kind": "future-kind", "addedAt": "2026-01-01T00:00:00.0000000Z", "source": "remote", "data": { "x": [1, 2] } }""")!;
        library["assets"]!.AsArray().Add(future.DeepClone());
        File.WriteAllText(store.LibraryPath, library.ToJsonString());

        var reopened = AssetStore.Open(store.Root);
        Assert.Null(reopened.LibraryError);
        Assert.Single(reopened.Catalog.All);
        reopened.SaveGradient(null, AssetSamples.Gradient("새 것"));
        var rows = JsonNode.Parse(File.ReadAllText(store.LibraryPath))!["assets"]!.AsArray();
        Assert.Contains(rows, row => JsonNode.DeepEquals(row, future));
        Assert.Equal(2, AssetStore.Open(store.Root).Catalog.All.Count);
    }

    // ───────────── 프로젝트와의 독립 ─────────────

    /// <summary>레시피를 넣은 프로젝트는 효과 값 사본을 가지므로 에셋을 지우거나 다른 내용으로 바꿔도, 저장했다 다시 열어도 그림이 같다.</summary>
    [Fact]
    public void ProjectRender_IsUnchangedAfterRemovingOrReplacingTheAsset()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(RootOf(dir));
        store.InstallFile(Package(dir, "1.0.0", 2));

        var project = new SpriteProject(24, 24);
        project.DuplicateFrame(0);
        var cel = new PixelImage(24, 24);
        for (int y = 8; y < 16; y++)
            for (int x = 8; x < 16; x++)
                cel[x, y] = new Color32(200, 40, 40);
        project.SetCel(0, 0, cel);
        project.SetCel(0, 1, cel);
        var plain = project.Flatten(0).Pixels.ToArray();
        var recipe = store.Catalog.Recipes.Single();
        project.SetLayerEffects(0, recipe.Recipe!.Instantiate(project, null));
        var expected = Enumerable.Range(0, 2).Select(f => project.Flatten(f).Pixels.ToArray()).ToArray();
        Assert.NotEqual(plain, expected[0]);
        string file = Path.Combine(dir.Path, "hero.chickxel");
        ProjectIO.Save(project, file);

        void AssertSameRender(SpriteProject p)
        {
            for (int f = 0; f < 2; f++) Assert.Equal(expected[f], p.Flatten(f).Pixels);
        }

        store.SaveRecipe(recipe.Recipe! with { Effects = [new FadeEffect()] });
        AssertSameRender(project);
        store.Remove(recipe.Id);
        Assert.Empty(store.Catalog.Recipes);
        AssertSameRender(project);
        var reloaded = ProjectIO.Load(file);
        AssertSameRender(reloaded);
        Assert.Equal(AssetSamples.EffectJson(project.Layers[0].Effects), AssetSamples.EffectJson(reloaded.Layers[0].Effects));
        // 같은 상점 ID로 다른 내용의 새 버전을 가져와도 프로젝트는 그대로
        store.Install(Package(dir, "2.0.0", 3), AssetSources.File, _ => ImportChoice.Replace);
        AssertSameRender(ProjectIO.Load(file));
    }

    // ───────────── 여러 앱 ─────────────

    /// <summary>같은 저장소를 연 두 앱이 번갈아 바꿔도 서로의 가져오기·저장을 덮어 지우지 않는다.</summary>
    [Fact]
    public void TwoOpenStores_DoNotOverwriteEachOthersChanges()
    {
        using var dir = new TempDir();
        var first = AssetStore.Open(RootOf(dir));
        var second = AssetStore.Open(first.Root);

        var recipe = first.SaveRecipe(EffectRecipe.Capture("", "앞 앱", [new FadeEffect()]));
        var gradient = second.SaveGradient(null, AssetSamples.Gradient("뒤 앱"));
        first.InstallFile(Package(dir, "1.0.0", 2));
        second.SetFavorite(gradient.Id, true);

        var reopened = AssetStore.Open(first.Root);
        Assert.Equal([recipe.Id, gradient.Id, Id + ".neon-hit", Id + ".neon-blue"], reopened.Catalog.All.Select(e => e.Id).ToArray());
        Assert.True(reopened.Catalog.Find(gradient.Id)!.Gradient!.Favorite);
    }
}
