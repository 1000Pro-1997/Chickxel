using System.Drawing;
using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>예전(v1) 저장소 모양을 손으로 만든다: <c>installed.json</c>, <c>Packages/&lt;id&gt;/&lt;version&gt;/</c>(푼 패키지), <c>Personal/personal-assets.json</c>, 개인 장면 PNG.</summary>
internal sealed class Version1Layout(string root)
{
    public string Root { get; } = root;

    /// <summary>패키지 바이트를 <c>Packages/&lt;id&gt;/&lt;version&gt;/</c>에 풀고 그 폴더를 돌려준다.</summary>
    public string Package(byte[] package)
    {
        var manifest = AssetPackageValidator.Read(new MemoryStream(package)).Manifest;
        string folder = Path.Combine(Root, "Packages", manifest.Id, manifest.Version);
        foreach (var (name, data) in AssetSamples.Unzip(package))
        {
            string path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }
        return folder;
    }

    public void Installed(IEnumerable<(string Id, string Version, bool Enabled, string Source, DateTime InstalledUtc)> packages)
    {
        var list = new JsonArray();
        foreach (var p in packages)
            list.Add(new JsonObject
            {
                ["id"] = p.Id, ["version"] = p.Version, ["enabled"] = p.Enabled, ["source"] = p.Source,
                ["installedAt"] = p.InstalledUtc.ToString("O", CultureInfo.InvariantCulture),
            });
        Write("installed.json", new JsonObject { ["schemaVersion"] = 1, ["packages"] = list }.ToJsonString());
    }

    public void Personal(IEnumerable<(string Id, string Kind, IAssetData Data, DateTime CreatedUtc)> assets, params string[] favorites)
    {
        var list = new JsonArray();
        foreach (var a in assets)
            list.Add(new JsonObject
            {
                ["id"] = a.Id, ["kind"] = a.Kind, ["createdAt"] = a.CreatedUtc.ToString("O", CultureInfo.InvariantCulture),
                ["data"] = AssetKinds.Find(a.Kind)!.WritePersonal(a.Data, a.Id),
            });
        var json = new JsonObject { ["schemaVersion"] = 1, ["assets"] = list, ["favorites"] = new JsonArray(favorites.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray()) };
        PersonalRaw(json.ToJsonString());
    }

    public void PersonalRaw(string text) => Write(Path.Combine("Personal", "personal-assets.json"), text);

    public void Scene(string id, PreviewScene scene)
    {
        string path = Path.Combine(Root, "Personal", "scenes", id + ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, scene.ToPng());
    }

    public void Write(string relative, string text)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}

/// <summary>예전 저장소(설치 패키지 폴더 + 개인 에셋 파일) → library.json 한 번 옮기기.</summary>
public class AssetStoreVersion1Tests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Created = new(2025, 12, 1, 9, 30, 0, DateTimeKind.Utc), Installed = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
    private const string Id = AssetSamples.PackageId, OtherId = "other.pack", BrokenId = "broken.pack";

    private static string RootOf(TempDir dir) => Path.Combine(dir.Path, "Assets");

    private static readonly string ScriptSource = OfficialEffects.Source("shine") + "\n// " + nameof(AssetStoreVersion1Tests) + "\n";

    private static PreviewScene Scene()
    {
        var ground = new PixelImage(16, 16);
        for (int x = 0; x < 16; x++) ground[x, 15] = new Color32(0, 200, 0);
        return new PreviewScene(16, 16, 1, [new PreviewSceneLayer("ground", [ground])], 1, new Point(8, 15));
    }

    /// <summary>개인 에셋 둘(그라데이션, 장면 있는 스크립트), 켠 패키지, 끈 패키지, 파일이 바뀐 패키지가 든 예전 저장소.</summary>
    private static Version1Layout FullLayout(string root)
    {
        var v1 = new Version1Layout(root);
        v1.Package(AssetSamples.Build());
        v1.Package(AssetSamples.Build(AssetSamples.Header("2.0.0", OtherId) with { Author = new AssetAuthor("other", "Other") },
            AssetSamples.Recipe("Other", 3), AssetSamples.Gradient("Other G")));
        string broken = v1.Package(AssetSamples.Build(AssetSamples.Header("1.0.0", BrokenId) with { Author = new AssetAuthor("broken", "Broken") }));
        string recipeFile = Path.Combine(broken, "assets", "effects", "neon-hit.json");
        File.WriteAllText(recipeFile, File.ReadAllText(recipeFile).Replace("Neon Hit", "Evil Hit", StringComparison.Ordinal));
        v1.Installed([(Id, "1.0.0", true, AssetSources.Remote, Installed), (OtherId, "2.0.0", false, AssetSources.File, Installed), (BrokenId, "1.0.0", true, AssetSources.File, Installed)]);
        var script = ScriptAsset.From("", ScriptEffectProgram.Parse(ScriptSource).Program!);
        v1.Personal(
            [
                ("personal.mine", AssetKinds.Gradient, new GradientAsset("personal.mine", AssetSamples.Gradient("Mine")), Created),
                ("personal.script", AssetKinds.EffectScript, script, Created),
            ],
            "personal.mine", Id + ".neon-blue");
        v1.Scene("personal.script", Scene());
        v1.Write(Path.Combine("Deleted", "old", "note.txt"), "보관함");
        return v1;
    }

    [Fact]
    public void Open_MigratesVersionOneLayout_KeepingIdsFavoritesAndMetadata_AndMovesOldFilesToBackup()
    {
        using var dir = new TempDir();
        FullLayout(RootOf(dir));

        var store = AssetStore.Open(RootOf(dir), nowUtc: Now);
        Assert.Null(store.LibraryError);
        Assert.True(store.HasLibraryFile);

        // 개인 에셋: 이 컴퓨터에서 만든 것(ID·만든 시각·즐겨찾기·장면 유지)
        var mine = store.Catalog.Find("personal.mine")!;
        Assert.Equal(((AssetAuthor?)null, (AssetStoreLink?)null, (string?)null, AssetSources.Local, Created, true),
            (mine.Author, mine.Store, mine.License, mine.Source, mine.AddedUtc, mine.Favorite));
        AssetSamples.AssertSameGradient(AssetSamples.Gradient("Mine"), mine.Gradient!);
        var script = store.Catalog.Find("personal.script")!;
        Assert.Equal((ScriptEffectProgram.HashOf(ScriptSource), (AssetAuthor?)null), (script.Script!.Hash, script.Author));
        Assert.Equal(Scene().ToPng(), script.Script.Scene!.ToPng());

        // 켠 패키지: 에셋마다 패키지의 작성자·라이선스·상점 연결, 들어온 길·시각, 미리보기·라이선스 문서
        var received = store.Catalog.FromPackage(Id);
        Assert.Equal([Id + ".neon-hit", Id + ".neon-blue"], received.Select(e => e.Id).ToArray());
        Assert.All(received, e =>
        {
            Assert.Equal((AssetSamples.Header().Author, "CC0-1.0", AssetSources.Remote, Installed, false), (e.Author, e.License, e.Source, e.AddedUtc, e.Modified));
            Assert.Equal((Id, e.Id, "1.0.0", "Neon Pack"), (e.Store!.PackageId, e.Store.AssetId, e.Store.Version, e.Store.Name));
            Assert.True(File.Exists(e.PreviewPath));
            Assert.Equal("CC0", File.ReadAllText(e.LicenseFilePath!));
        });
        Assert.Equal([false, true], received.Select(e => e.Favorite).ToArray());
        AssetSamples.AssertSameRecipe(AssetSamples.Recipe(), received[0].Recipe!);

        // 끈 패키지도 보통 에셋으로 옮긴다
        var other = store.Catalog.FromPackage(OtherId);
        Assert.Equal(2, other.Count);
        Assert.All(other, e => Assert.Equal(("other", "2.0.0", AssetSources.File), (e.Author!.Id, e.Store!.Version, e.Source)));

        // 파일이 바뀐 패키지는 건너뛰고 까닭을 남긴다
        Assert.Empty(store.Catalog.FromPackage(BrokenId));
        Assert.Contains(store.LoadErrors, error => error.StartsWith(BrokenId + " 1.0.0", StringComparison.Ordinal));
        Assert.Equal(6, store.Catalog.All.Count);

        // 예전 파일은 v1-backup/<시각>/으로
        string backup = Path.Combine(store.Root, "v1-backup", Now.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
        foreach (var name in new[] { "installed.json", "Packages", "Personal", "Deleted" })
        {
            Assert.False(Path.Exists(Path.Combine(store.Root, name)), name);
            Assert.True(Path.Exists(Path.Combine(backup, name)), name);
        }

        // 다시 열면 library.json만 읽는다(같은 결과)
        var reopened = AssetStore.Open(store.Root);
        Assert.Empty(reopened.LoadErrors);
        Assert.Equal(store.Catalog.All.Select(e => (e.Id, e.Favorite, e.Source, e.Store?.PackageId)), reopened.Catalog.All.Select(e => (e.Id, e.Favorite, e.Source, e.Store?.PackageId)));
        Assert.Equal(Scene().ToPng(), reopened.Catalog.Find("personal.script")!.Script!.Scene!.ToPng());
    }

    /// <summary>예전 상태 파일을 읽지 못하면 아무것도 쓰지 않고 저장소를 막는다(예전 파일은 그대로). 고친 뒤 다음에 열면 옮긴다.</summary>
    [Theory]
    [InlineData("Personal/personal-assets.json", "{ \"schemaVersion\": 1, \"assets\": [")]
    [InlineData("Personal/personal-assets.json", "{ \"schemaVersion\": 2, \"assets\": [] }")]
    [InlineData("installed.json", "[]")]
    public void Open_WithUnreadableVersionOneFile_BlocksTheStoreWithoutWriting(string file, string content)
    {
        using var dir = new TempDir();
        var v1 = FullLayout(RootOf(dir));
        v1.Write(file, content);

        var store = AssetStore.Open(RootOf(dir), nowUtc: Now);
        Assert.NotNull(store.LibraryError);
        Assert.NotEmpty(store.LoadErrors);
        Assert.False(store.HasLibraryFile);
        Assert.Empty(store.Catalog.All);
        Assert.Empty(Directory.GetFileSystemEntries(store.FilesDirectory));
        Assert.False(Directory.Exists(Path.Combine(store.Root, "v1-backup")));
        Assert.Equal(content, File.ReadAllText(Path.Combine(store.Root, file)));
        Assert.Throws<InvalidOperationException>(() => store.SaveGradient(null, AssetSamples.Gradient("새 것")));
        Assert.Throws<InvalidOperationException>(() => store.InstallFile(AssetSamples.Save(dir.Path, AssetSamples.Build())));
        Assert.False(store.HasLibraryFile);

        // 고친 뒤 다음 실행
        FullLayout(RootOf(dir));
        var next = AssetStore.Open(RootOf(dir), nowUtc: Now);
        Assert.Null(next.LibraryError);
        Assert.Equal(6, next.Catalog.All.Count);
    }
}
