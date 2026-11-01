using System.IO.Compression;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Xunit;

namespace Chickxel.Core.Tests;

/// <summary>
/// 기본(C#) 효과 → 공식 스크립트 효과 이전: 예전 프로젝트·레시피·패키지를 읽으면 같은 그림의 스크립트 효과가 되고, 저장하면 스크립트로 쓴다.
/// 공식 효과 패키지(기본 설치·되돌리기)와 카탈로그 원본도 본다.
/// </summary>
public sealed class LegacyEffectMigrationTests
{
    private const int Frames = 4;

    /// <summary>공식 원문이 있는 예전 기본 효과마다 설정을 조금씩 바꾼 효과 둘(그라데이션·프레임 범위·클리핑·움직임 포함).</summary>
    private static IEnumerable<(EffectDefinition Definition, LayerEffect[] Effects)> LegacySamples()
    {
        foreach (var definition in EffectRegistry.BuiltIn.Where(d => OfficialEffects.IdOfType(d.TypeId) is not null))
        {
            var plain = definition.Create();
            var slot = definition.Parameters.Select(p => p.ColorSlot).FirstOrDefault(s => s is not null);
            var first = slot is null ? plain : plain with { ColorGradients = new Dictionary<string, EffectColorGradient> { [slot] = EffectParity.Gradients[0] } };
            var second = plain with { Frames = new FrameRange(1, 2), ClipToPrevious = true };
            if (second is AnimatedEffect animated) second = animated with { Easing = EffectEasing.EaseInOut, Repeat = EffectRepeat.PingPong, RepeatCount = 2 };
            // 셋째는 꺼 둔 효과(켜짐도 옮겨지는지)
            yield return (definition, [first, second, plain with { Enabled = false }]);
        }
    }

    /// <summary>저장소 뿌리(이 원본 파일 위쪽에서 Chickxel.sln — 출력 폴더를 다른 곳에 둬도 찾음).</summary>
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(here)!, AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Chickxel.sln"))) return dir.FullName;
        throw new InvalidOperationException("Chickxel.sln not found");
    }

    private static PixelImage Sprite() => PngIO.Load(TestUtil.Fixture("player@2x.png"));

    /// <summary>예전 기본 효과를 그대로 저장한 프로젝트(예전 앱이 쓴 파일과 같은 JSON).</summary>
    private static (SpriteProject Project, List<LayerEffect[]> Effects) LegacyProject()
    {
        var sprite = Sprite();
        var p = new SpriteProject(sprite.Width, sprite.Height);
        for (int i = 1; i < Frames; i++) p.DuplicateFrame(0);
        var effects = new List<LayerEffect[]>();
        int layer = 0;
        foreach (var (_, list) in LegacySamples())
        {
            if (layer > 0) p.AddLayer();
            for (int f = 0; f < Frames; f++) p.SetCel(layer, f, sprite.Clone());
            p.SetLayerEffects(layer, list);
            effects.Add(list);
            layer++;
        }
        return (p, effects);
    }

    private static MemoryStream Save(SpriteProject p)
    {
        var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        return stream;
    }

    private static JsonObject ProjectJson(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = zip.GetEntry(ProjectIO.ProjectEntryName)!.Open();
        return (JsonObject)JsonNode.Parse(reader)!;
    }

    [Fact]
    public void OldProject_WithEveryBuiltInType_OpensAsOfficialScripts_RendersIdentically_AndSavesScripts()
    {
        var (legacy, expected) = LegacyProject();
        Assert.Equal(OfficialEffects.Ids.Count, expected.Count);
        using var stream = Save(legacy);
        // 예전 앱이 쓴 파일처럼 기본 효과 종류 ID로 저장돼 있다
        var oldTypes = ProjectJson(stream)["layers"]!.AsArray().SelectMany(l => l!["effects"]!.AsArray()).Select(e => (string)e!["type"]!).ToHashSet();
        Assert.DoesNotContain(ScriptDefinition.Id, oldTypes);
        stream.Position = 0;

        var warnings = new List<string>();
        var loaded = ProjectIO.Load(stream, warnings);
        Assert.Empty(warnings);
        var input = new EffectParity.Input("player", [Sprite()]);
        for (int i = 0; i < expected.Count; i++)
        {
            var effects = loaded.Layers[i].Effects;
            Assert.All(effects, e => Assert.IsType<ScriptEffect>(e));
            var scripts = effects.Cast<ScriptEffect>().ToArray();
            Assert.All(scripts, s => Assert.True(OfficialEffects.IsOfficialHash(s.ScriptHash)));
            Assert.All(scripts, s => Assert.Equal(OfficialEffects.Source(OfficialEffects.IdOfHash(s.ScriptHash)!), loaded.Scripts[s.ScriptHash]));
            Assert.Equal(expected[i].Select(e => (e.Enabled, e.Frames, e.ClipToPrevious)), effects.Select(e => (e.Enabled, e.Frames, e.ClipToPrevious)));
            using var generous = ScriptEffectProgram.UseTimeout(TimeSpan.FromMinutes(5));
            for (int f = 0; f < Frames; f++)
            {
                var want = EffectParity.RenderFrame(input, expected[i], f, Frames);
                var got = EffectParity.RenderFrame(input, effects, f, Frames);
                Assert.Equal((want.OffsetX, want.OffsetY), (got.OffsetX, got.OffsetY));
                Assert.True(want.Image.ContentEquals(got.Image), $"{expected[i][0].GetType().Name} frame {f}");
            }
        }
        // 프로젝트 전체 합성도 같다
        for (int f = 0; f < Frames; f++) Assert.True(legacy.Flatten(f).ContentEquals(loaded.Flatten(f)), $"flatten frame {f}");

        // 다시 저장하면 스크립트 효과와 원문 표만 쓴다(예전 앱도 스크립트 효과로 연다)
        using var saved = Save(loaded);
        var json = ProjectJson(saved);
        var types = json["layers"]!.AsArray().SelectMany(l => l!["effects"]!.AsArray()).Select(e => (string)e!["type"]!).Distinct().ToArray();
        Assert.Equal([ScriptDefinition.Id], types);
        var table = json["scripts"]!.AsObject();
        Assert.Equal(OfficialEffects.Ids.Select(OfficialEffects.HashOf).Order(StringComparer.Ordinal), table.Select(p => p.Key).Order(StringComparer.Ordinal));
        saved.Position = 0;
        var again = ProjectIO.Load(saved);
        // 그라데이션 사전은 읽을 때마다 새 값이라 저장 JSON으로 비교
        for (int i = 0; i < expected.Count; i++)
            Assert.Equal(loaded.Layers[i].Effects.Select(e => EffectCodec.Write(e).ToJsonString()), again.Layers[i].Effects.Select(e => EffectCodec.Write(e).ToJsonString()));
    }

    [Fact]
    public void LegacyShortIdFormat_AlsoBecomesOfficialScript()
    {
        // 프로젝트 형식 3 이하: 짧은 ID, 설정이 효과 객체에 바로 있음
        var json = (JsonObject)JsonNode.Parse("""{ "type": "outline", "enabled": true, "thickness": 2, "color": "#FF0000" }""")!;
        var stored = Assert.IsType<OutlineEffect>(EffectCodec.ReadStored(json));
        var read = Assert.IsType<ScriptEffect>(EffectCodec.Read(json));
        Assert.Equal(OfficialEffects.HashOf("outline"), read.ScriptHash);
        Assert.Equal(2, Convert.ToInt32(read.Settings["thickness"]));
        Assert.Equal(LegacyEffects.ToScript(stored, OfficialEffects.Program("outline")), read);
    }

    [Fact]
    public void UnknownType_StaysMissing_AndTextEffectStaysCSharp()
    {
        var unknown = (JsonObject)JsonNode.Parse("""{ "type": "chickxel.effect.future", "settings": { "x": 1 } }""")!;
        Assert.IsType<MissingEffect>(EffectCodec.Read(unknown));
        foreach (var definition in EffectRegistry.Selectable)
        {
            var effect = definition.Create();
            Assert.False(LegacyEffects.IsLegacy(effect));
            Assert.Same(effect, LegacyEffects.Convert(effect));
        }
    }

    [Fact]
    public void Recipe_WithLegacyEffects_ConvertsAndCarriesOfficialSources()
    {
        // 예전 개인 레시피(기본 효과 종류 ID)
        var json = (JsonObject)JsonNode.Parse("""
            { "schemaVersion": 2, "id": "personal.x", "name": "Old", "effects": [
              { "type": "chickxel.effect.outline", "version": 1, "settings": { "color": "#102030", "thickness": 2 } },
              { "type": "chickxel.effect.hitflash", "version": 1, "clipToPrevious": true, "settings": { "count": 3 } } ],
              "parameters": [ { "id": "c", "name": "Color", "kind": "color", "default": "#FF00FF", "bindings": [ { "effect": 0, "key": "color" } ] } ] }
            """)!;
        var context = new AssetReadContext("old.json", strict: false, AssetPackageLimits.Default, []);
        var recipe = EffectRecipeFormat.Read(json, context)!;
        Assert.NotNull(recipe);
        var scripts = recipe.Effects.Cast<ScriptEffect>().ToArray();
        Assert.Equal([OfficialEffects.HashOf("outline"), OfficialEffects.HashOf("hitflash")], scripts.Select(s => s.ScriptHash));
        Assert.Equal(scripts.Select(s => s.ScriptHash).Order(StringComparer.Ordinal), recipe.Scripts.Keys);
        // 매개변수 기본값이 스크립트 설정에 들어간다
        Assert.Equal("#FF00FF", scripts[0].Settings["color"]);
        // 다시 쓰면 스크립트 효과와 원문(형식 3 이상)
        var written = EffectRecipeFormat.Write(recipe);
        Assert.All(written["effects"]!.AsArray(), e => Assert.Equal(ScriptDefinition.Id, (string)e!["type"]!));
        Assert.Equal(2, written["scripts"]!.AsObject().Count);
        // Capture도 기본 효과를 공식 스크립트로 바꾼다
        var captured = EffectRecipe.Capture("", "c", [new ShadowEffect { OffsetX = 2 }]);
        var shadow = Assert.IsType<ScriptEffect>(Assert.Single(captured.Effects));
        Assert.Equal(OfficialEffects.Source("shadow"), captured.Scripts[shadow.ScriptHash]);
    }

    [Fact]
    public void OfficialPresetPackages_InCatalog_InstallAsScripts_AndRenderLikeTheirBuiltInEffects()
    {
        var catalog = Path.Combine(RepoRoot(), "catalog");
        using var dir = new TempDir();
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        var input = new EffectParity.Input("player", [Sprite()]);
        foreach (var file in Directory.GetFiles(catalog, "chickxel.effects.*.chickxel-asset", SearchOption.AllDirectories))
        {
            var package = store.Install(file);
            foreach (var entry in package.Entries.Where(e => e.Recipe is not null))
            {
                var recipe = entry.Recipe!;
                Assert.All(recipe.Effects, e => Assert.IsType<ScriptEffect>(e));
                Assert.All(recipe.Effects.Cast<ScriptEffect>(), s => Assert.Equal(OfficialEffects.Source(OfficialEffects.IdOfHash(s.ScriptHash)!), recipe.Scripts[s.ScriptHash]));
                // 패키지 안 JSON을 바꾸지 않고 읽은 기본 효과와 같은 그림
                using var zip = ZipFile.OpenRead(file);
                var manifestEntry = package.Manifest!.Assets.First(a => a.Id == entry.Id);
                JsonObject raw;
                using (var reader = zip.GetEntry(manifestEntry.Path)!.Open()) raw = (JsonObject)JsonNode.Parse(reader)!;
                var builtIn = raw["effects"]!.AsArray().Select(e => EffectCodec.ReadStored((JsonObject)e!)).ToArray();
                var expected = RecipeParameters.ApplyDefaults(builtIn, recipe.Parameters);
                using var generous = ScriptEffectProgram.UseTimeout(TimeSpan.FromMinutes(5));
                for (int f = 0; f < EffectRecipe.DefaultPreviewFrames; f++)
                {
                    var want = EffectParity.RenderFrame(input, expected, f, EffectRecipe.DefaultPreviewFrames);
                    var got = EffectParity.RenderFrame(input, recipe.Effects, f, EffectRecipe.DefaultPreviewFrames);
                    Assert.True(want.Image.ContentEquals(got.Image) && (want.OffsetX, want.OffsetY) == (got.OffsetX, got.OffsetY), $"{entry.Id} frame {f}");
                }
            }
        }
    }

    [Fact]
    public void OfficialScripts_AreTrustedAndResolvable()
    {
        foreach (var id in OfficialEffects.Ids)
        {
            Assert.Equal(ScriptTrust.Trusted, ScriptLibrary.TrustOf(OfficialEffects.HashOf(id)));
            Assert.Equal(id, OfficialEffects.Resolve(id.ToUpperInvariant()));
            Assert.Equal(id, OfficialEffects.Resolve("chickxel.effect." + id));
            Assert.Equal(id, OfficialEffects.Resolve(OfficialEffectPackages.PackageId(id)));
            Assert.Equal(id, OfficialEffects.Resolve(OfficialEffectPackages.AssetId(id)));
        }
        Assert.Equal("coloroverlay", OfficialEffects.Resolve("gpixel.effect.coloroverlay") ?? OfficialEffects.Resolve("coloroverlay"));
        Assert.Null(OfficialEffects.Resolve("chickxel.effect.text"));
        Assert.Null(OfficialEffects.Resolve("nope"));
        // 효과 창 차례 = 예전 기본 효과 등록 차례
        Assert.Equal(EffectRegistry.BuiltIn.Select(d => OfficialEffects.IdOfType(d.TypeId)).OfType<string>(), OfficialEffects.Ordered);
    }

    /// <summary>
    /// 공식 원문을 고친 뒤에도 예전 공식 원문을 담은 프로젝트·레시피는 묻지 않고 실행된다: 내보낸 해시 목록(hashes.txt)이 지금 원문을 모두 담고,
    /// 목록의 해시는 모두 허락한다(목록이 빠짐없는지는 CatalogBuilder --check가 카탈로그의 모든 공식 패키지 버전으로도 본다).
    /// </summary>
    [Fact]
    public void ShippedHashList_HasEveryCurrentSource_AndEveryListedSourceStaysTrusted()
    {
        // 커밋한 목록 파일 = 앱에 든 목록
        var committed = OfficialEffects.ParseShipped(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Chickxel.Core", "Scripting", "Official", OfficialEffects.ShippedFileName)));
        Assert.Equal(committed.OrderBy(p => p.Key, StringComparer.Ordinal), OfficialEffects.ShippedHashes.OrderBy(p => p.Key, StringComparer.Ordinal));
        foreach (var id in OfficialEffects.Ids) Assert.Equal(id, OfficialEffects.ShippedHashes[OfficialEffects.HashOf(id)]);
        Assert.All(OfficialEffects.ShippedHashes, pair =>
        {
            Assert.Equal(ScriptTrust.Trusted, ScriptLibrary.TrustOf(pair.Key));
            Assert.Equal(pair.Value, OfficialEffects.IdOfShippedHash(pair.Key));
        });

        // 공식 원문을 고친 원문(목록에 없음)은 묻는다
        var edited = ScriptLibrary.Add(OfficialEffects.Source("outline") + "\n// 고침\n");
        Assert.False(OfficialEffects.IsOfficialHash(edited));
        Assert.Null(OfficialEffects.IdOfShippedHash(edited));
        Assert.Equal(ScriptTrust.Unknown, ScriptLibrary.TrustOf(edited));

        // 목록 글: 설명 줄·빈 줄·CRLF를 넘기고 FormatShipped와 왕복, 틀린 줄과 두 ID에 적힌 해시는 줄 번호와 함께 실패
        string old = new('a', 64), current = OfficialEffects.HashOf("outline");
        var shipped = new Dictionary<string, string>(StringComparer.Ordinal) { [old] = "outline", [current] = "outline", [OfficialEffects.HashOf("shadow")] = "shadow" };
        Assert.Equal(shipped.OrderBy(p => p.Key, StringComparer.Ordinal),
            OfficialEffects.ParseShipped(OfficialEffects.FormatShipped(shipped).ReplaceLineEndings("\r\n") + "\r\n\r\n").OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Contains(":2:", Assert.Throws<FormatException>(() => OfficialEffects.ParseShipped($"# x\noutline {old.ToUpperInvariant()}")).Message);
        Assert.Throws<FormatException>(() => OfficialEffects.ParseShipped($"outline {old} extra"));
        Assert.Throws<FormatException>(() => OfficialEffects.ParseShipped($"Outline {old}"));
        Assert.Throws<FormatException>(() => OfficialEffects.ParseShipped($"outline {old}\nshadow {old}"));
    }

    [Fact]
    public void OfficialPackages_BuildValid_AndMatchTheCatalogSource()
    {
        var catalog = Path.Combine(RepoRoot(), "catalog");
        foreach (var id in OfficialEffects.Ids)
        {
            var package = AssetPackageValidator.Read(new MemoryStream(OfficialEffectPackages.Build(id)), new AssetValidationOptions());
            Assert.Equal((OfficialEffectPackages.PackageId(id), OfficialEffectPackages.VersionOf(id), "chickxel"),
                (package.Manifest.Id, package.Manifest.Version, package.Manifest.Author.Id));
            var script = Assert.IsType<ScriptAsset>(Assert.Single(package.Items).Data);
            Assert.Equal(OfficialEffectPackages.AssetId(id), Assert.Single(package.Items).Entry.Id);
            Assert.Equal(OfficialEffects.HashOf(id), script.Hash);
            Assert.NotNull(Assert.Single(package.Items).Entry.Preview);
            // 카탈로그 원본의 지금 버전 패키지 원문 = 앱에 든 원문(다르면 버전을 올려 CatalogBuilder를 돌림)
            var file = Path.Combine(catalog, OfficialEffectPackages.PackageId(id), $"{OfficialEffectPackages.PackageId(id)}-{OfficialEffectPackages.VersionOf(id)}{AssetPackage.Extension}");
            var published = AssetPackageValidator.Read(file, new AssetValidationOptions());
            Assert.Equal(script.Hash, Assert.IsType<ScriptAsset>(Assert.Single(published.Items).Data).Hash);
        }
    }

    [Fact]
    public void Sync_InstallsOnce_KeepsDisabledAndDeleted_RestoreBringsBack_UpdatesOlderVersions()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        Assert.True(OfficialEffectPackages.NeedsSync(store));
        var first = OfficialEffectPackages.Sync(store);
        Assert.Empty(first.Errors);
        Assert.Equal(OfficialEffects.Ids.Count, first.Installed.Count);
        Assert.All(store.Catalog.Packages, p => Assert.Equal((OfficialEffectPackages.BundledSource, true), (p.State.Source, p.Enabled)));
        // 효과 탭 차례
        Assert.Equal(OfficialEffects.Ordered, store.Catalog.EffectScripts.Select(e => AssetCatalog.OfficialIdOf(e)));

        // 다시 열어 맞춰도 아무것도 하지 않는다
        store = AssetStore.Open(store.Root);
        Assert.False(OfficialEffectPackages.NeedsSync(store));
        Assert.False(OfficialEffectPackages.Sync(store).Changed);

        // 끈 것은 그대로, 지운 것은 다시 설치하지 않는다
        store.SetEnabled(OfficialEffectPackages.PackageId("outline"), false);
        store.Delete(OfficialEffectPackages.PackageId("shadow"));
        var again = OfficialEffectPackages.Sync(AssetStore.Open(store.Root));
        Assert.False(again.Changed);
        store = AssetStore.Open(store.Root);
        Assert.False(OfficialEffectPackages.NeedsSync(store));
        Assert.True(OfficialEffectPackages.NeedsSync(store, restore: true));
        Assert.False(store.Catalog.FindPackage(OfficialEffectPackages.PackageId("outline"))!.Enabled);
        Assert.Null(store.Catalog.FindPackage(OfficialEffectPackages.PackageId("shadow")));
        Assert.DoesNotContain(store.Catalog.EffectScripts, e => AssetCatalog.OfficialIdOf(e) is "outline" or "shadow");

        // 더 낮은 버전이 설치돼 있으면 켜짐 그대로 업데이트
        var program = OfficialEffects.Program("bob");
        var builder = new AssetPackageBuilder(OfficialEffectPackages.Header("bob", program) with { Version = "0.9.0" });
        builder.Add(AssetKinds.EffectScript, ScriptAsset.From("", program), AssetPreview.Package(ScriptAsset.From("", program)), segment: "bob");
        builder.AddDocument("LICENSE.txt", OfficialEffectPackages.LicenseText);
        var old = Path.Combine(dir.Path, "old.chickxel-asset");
        using (var output = File.Create(old)) builder.Write(output);
        store.Delete(OfficialEffectPackages.PackageId("bob"));
        store.Install(old, OfficialEffectPackages.BundledSource);
        store.SetEnabled(OfficialEffectPackages.PackageId("bob"), false);
        Assert.True(OfficialEffectPackages.NeedsSync(store));
        var update = OfficialEffectPackages.Sync(store);
        Assert.Equal([OfficialEffectPackages.PackageId("bob")], update.Updated);
        var bob = store.Catalog.FindPackage(OfficialEffectPackages.PackageId("bob"))!;
        Assert.Equal((OfficialEffectPackages.VersionOf("bob"), false), (bob.Version, bob.Enabled));

        // 되돌리기: 지운 것을 다시 설치하고 끈 것을 켠다
        var restore = OfficialEffectPackages.Sync(store, restore: true);
        Assert.Equal([OfficialEffectPackages.PackageId("shadow")], restore.Installed);
        Assert.Equal([OfficialEffectPackages.PackageId("outline"), OfficialEffectPackages.PackageId("bob")], restore.Enabled);
        Assert.All(store.Catalog.Packages, p => Assert.True(p.Enabled));
        Assert.Equal(OfficialEffects.Ordered, store.Catalog.EffectScripts.Select(e => AssetCatalog.OfficialIdOf(e)));
    }

    /// <summary>
    /// 앱은 공식 효과를 백그라운드에서 같은 폴더의 다른 저장소 객체로 설치하고, 끝나면 앱의 저장소를 다시 읽는다(Changed 한 번).
    /// 다른 쪽이 잠금을 쥐고 바꾸는 중이면 저장소를 열어도 그 Staging·새 폴더를 정리하지 않는다.
    /// </summary>
    [Fact]
    public async Task SyncOnAnotherStoreObject_AppearsAfterRefresh_AndOpenSkipsCleanupWhileLocked()
    {
        using var dir = new TempDir();
        string root = Path.Combine(dir.Path, "Assets");
        var app = AssetStore.Open(root);
        int changed = 0;
        app.Changed += () => changed++;
        var result = await Task.Run(() => OfficialEffectPackages.Sync(AssetStore.Open(root)));
        Assert.Equal(OfficialEffects.Ids.Count, result.Installed.Count);
        Assert.Empty(app.Catalog.Packages);
        app.Refresh();
        Assert.Equal(1, changed);
        Assert.Equal(OfficialEffects.Ordered, app.Catalog.EffectScripts.Select(e => AssetCatalog.OfficialIdOf(e)));
        Assert.False(OfficialEffectPackages.NeedsSync(app));
        app.Refresh();
        Assert.Equal(1, changed);

        var staged = Path.Combine(app.StagingDirectory, "busy" + AssetPackage.Extension);
        File.WriteAllText(staged, "x");
        using (new FileStream(Path.Combine(root, AssetStore.LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.Equal(OfficialEffects.Ids.Count, AssetStore.Open(root).Catalog.Packages.Count);
        Assert.True(File.Exists(staged));
        AssetStore.Open(root);
        Assert.False(File.Exists(staged));
    }
}
