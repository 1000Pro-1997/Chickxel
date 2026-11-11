using System.Globalization;
using Chickxel.Core.Assets;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>예전 사용자 정의 효과·그라데이션 프리셋 → 이 컴퓨터의 에셋 한 번 옮기기(계획 14장 2단계).</summary>
public class AssetMigrationTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>backup 이름의 날짜(사용자가 보는 지역 날짜).</summary>
    private static string Day => Now.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private const string Effects = """
        { "version": 2, "presets": [
          { "name": "번쩍 외곽선", "effects": [
            { "type": "chickxel.effect.outline", "version": 1, "enabled": true, "clipToPrevious": false, "settings": { "thickness": 3, "color": "#00FFC8" } },
            { "type": "chickxel.effect.shadow", "version": 1, "enabled": false, "clipToPrevious": true, "settings": { "offsetX": -2, "offsetY": 3 } }
          ] },
          { "name": "미래", "effects": [
            { "type": "chickxel.effect.future", "version": 3, "enabled": true, "clipToPrevious": false, "settings": { "power": 7 } },
            { "type": "chickxel.effect.fade", "version": 1, "settings": { "fadeOut": true } }
          ] }
        ] }
        """;

    private const string Gradients = """
        { "version": 2, "presets": [
          { "name": "Dusk", "favorite": true, "colorSpace": "OkLab", "angle": 30, "cycles": 2, "pingPong": true,
            "stops": [ { "position": 0, "color": "#201040" }, { "position": 0.6, "color": "#C03060" }, { "position": 1, "color": "#FFD080" } ],
            "alphaStops": [ { "position": 0, "alpha": 255 }, { "position": 1, "alpha": 64 } ] },
          { "name": "Plain", "stops": [ { "position": 0, "color": "#000000" }, { "position": 1, "color": "#FFFFFF" } ] }
        ] }
        """;

    private sealed class Setup : IDisposable
    {
        private readonly TempDir _dir = new();

        public string Legacy => _dir.Path;
        public string Root => Path.Combine(_dir.Path, "Assets");
        public string EffectsPath => Path.Combine(Legacy, AssetMigration.EffectPresetsFileName);
        public string GradientsPath => Path.Combine(Legacy, AssetMigration.GradientPresetsFileName);

        public AssetStore Open() => AssetStore.Open(Root);

        public void Dispose() => _dir.Dispose();
    }

    private static string[] Json(IEnumerable<LayerEffect> effects) => effects.Select(e => EffectCodec.Write(e).ToJsonString()).ToArray();

    [Fact]
    public void Run_MovesLegacyPresetsIntoLocalAssets_WithFavorites_AndDatedBackups()
    {
        using var setup = new Setup();
        File.WriteAllText(setup.EffectsPath, Effects);
        File.WriteAllText(setup.GradientsPath, Gradients);
        var legacyEffects = AssetMigration.ReadLegacyEffects(Effects);
        var legacyGradients = AssetMigration.ReadLegacyGradients(Gradients);

        var store = setup.Open();
        Assert.False(store.HasLibraryFile);
        var result = AssetMigration.Run(store, setup.Legacy, Now);

        Assert.Equal((true, 2, 2, (string?)null), (result.Ran, result.Recipes, result.Gradients, result.Error));
        string effectsBackup = Path.Combine(setup.Legacy, $"effect-presets.backup-{Day}.json");
        string gradientsBackup = Path.Combine(setup.Legacy, $"gradient-presets.backup-{Day}.json");
        Assert.Equal([effectsBackup, gradientsBackup], result.Backups);
        Assert.False(File.Exists(setup.EffectsPath));
        Assert.False(File.Exists(setup.GradientsPath));
        Assert.Equal(Effects, File.ReadAllText(effectsBackup));
        Assert.Equal(Gradients, File.ReadAllText(gradientsBackup));

        foreach (var catalog in new[] { store.Catalog, setup.Open().Catalog })
        {
            Assert.Equal(["번쩍 외곽선", "미래"], catalog.Recipes.Select(e => e.Name).ToArray());
            Assert.All(catalog.Recipes, e => Assert.Equal(((AssetAuthor?)null, (AssetStoreLink?)null, AssetSources.Local), (e.Author, e.Store, e.Source)));
            for (int i = 0; i < 2; i++) Assert.Equal(Json(legacyEffects[i].Effects), Json(catalog.Recipes[i].Recipe!.Effects));
            // 예전 그림자는 같은 설정의 공식 스크립트로 옮겨지고, 레시피가 그 원문을 싣는다
            var shadow = Assert.IsType<ScriptEffect>(catalog.Recipes[0].Recipe!.Effects[1]);
            Assert.Equal(OfficialEffects.HashOf("shadow"), shadow.ScriptHash);
            Assert.Equal(OfficialEffects.Source("shadow"), catalog.Recipes[0].Recipe!.Scripts[shadow.ScriptHash]);
            Assert.Equal((false, true, -2, 3),
                (shadow.Enabled, shadow.ClipToPrevious, Convert.ToInt32(shadow.Settings["offsetX"]), Convert.ToInt32(shadow.Settings["offsetY"])));

            Assert.Equal(["Dusk", "Plain"], catalog.Gradients.Select(e => e.Name).ToArray());
            Assert.Equal([true, false], catalog.Gradients.Select(e => e.Favorite).ToArray());
            for (int i = 0; i < 2; i++) AssetSamples.AssertSameGradient(legacyGradients[i], catalog.Gradients[i].Gradient!);
            Assert.True(catalog.Gradients[0].Gradient!.Favorite);
        }
    }

    [Fact]
    public void Run_WithVersionOneFiles_ReadsShortIdsAndAddsNewBuiltInGradients()
    {
        using var setup = new Setup();
        File.Copy(TestUtil.Fixture(Path.Combine("effects", "effect-presets-v1.json")), setup.EffectsPath);
        File.WriteAllText(setup.GradientsPath, """
            {"version":1,"presets":[{"name":"Mine","favorite":true,"stops":[{"position":0,"color":"#FF0000"},{"position":1,"color":"#0000FF"}]}]}
            """);

        var store = setup.Open();
        var result = AssetMigration.Run(store, setup.Legacy, Now);
        Assert.True(result.Ran);
        Assert.Equal(2, result.Backups.Count);

        var reopened = setup.Open();
        var recipe = Assert.Single(reopened.Catalog.Recipes);
        Assert.Equal("all", recipe.Name);
        Assert.Equal(Json(EffectRecipe.Capture("", "all", EffectCharacterizationTests.LegacySavedKinds()).Effects), Json(recipe.Recipe!.Effects));
        Assert.Equal(["Mine", "Fire", "Forest", "Grayscale"], reopened.Catalog.Gradients.Select(e => e.Name).ToArray());
        Assert.Equal([true, false, false, false], reopened.Catalog.Gradients.Select(e => e.Favorite).ToArray());
    }

    [Fact]
    public void Run_IsOneShot_SecondRunAndReopenDoNothing()
    {
        using var setup = new Setup();
        File.WriteAllText(setup.EffectsPath, Effects);
        File.WriteAllText(setup.GradientsPath, Gradients);
        var store = setup.Open();
        AssetMigration.Run(store, setup.Legacy, Now);
        byte[] library = File.ReadAllBytes(store.LibraryPath);

        // 예전 앱이 파일을 다시 만들어도 옮기지 않는다
        File.WriteAllText(setup.EffectsPath, Effects);
        Assert.Same(AssetMigrationResult.Skipped, AssetMigration.Run(store, setup.Legacy, Now));
        var reopened = setup.Open();
        Assert.Same(AssetMigrationResult.Skipped, AssetMigration.Run(reopened, setup.Legacy, Now.AddDays(1)));

        Assert.Equal(library, File.ReadAllBytes(store.LibraryPath));
        Assert.Equal(2, reopened.Catalog.Recipes.Count);
        Assert.Equal(2, reopened.Catalog.Gradients.Count);
        Assert.Equal(Effects, File.ReadAllText(setup.EffectsPath));
        Assert.Single(Directory.GetFiles(setup.Legacy, "effect-presets.backup-*"));
    }

    [Fact]
    public void Run_WithoutLegacyFiles_AddsNothing_BuiltInGradientsComeAsOfficialPackages()
    {
        using var setup = new Setup();
        var store = setup.Open();
        var result = AssetMigration.Run(store, setup.Legacy, Now);

        Assert.Equal((true, 0, 0, 0, (string?)null), (result.Ran, result.Recipes, result.Gradients, result.Backups.Count, result.Error));
        Assert.True(store.HasLibraryFile);
        var reopened = setup.Open();
        Assert.Empty(reopened.Catalog.All);
        Assert.Empty(Directory.GetFiles(setup.Legacy));
    }

    /// <summary>예전 파일 하나라도 읽지 못하면 아무것도 쓰지 않는다(빈 목록으로 덮지 않음). 고친 뒤 다음 실행에 다시 옮긴다.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_WithUnreadableLegacyFile_WritesNothing_AndRetriesNextRun(bool corruptEffects)
    {
        using var setup = new Setup();
        string effects = corruptEffects ? "{ \"version\": 2, \"presets\": [" : Effects;
        string gradients = corruptEffects ? Gradients : "{ \"version\": 2, \"presets\": [ { \"name\": \"Bad\", \"stops\": [] } ] }";
        File.WriteAllText(setup.EffectsPath, effects);
        File.WriteAllText(setup.GradientsPath, gradients);

        var store = setup.Open();
        var result = AssetMigration.Run(store, setup.Legacy, Now);
        Assert.True(result.Ran);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Backups);
        Assert.False(store.HasLibraryFile);
        Assert.Empty(store.Catalog.All);
        Assert.Equal(effects, File.ReadAllText(setup.EffectsPath));
        Assert.Equal(gradients, File.ReadAllText(setup.GradientsPath));
        Assert.Empty(Directory.GetFiles(setup.Legacy, "*.backup-*"));

        // 그 사이 만든 에셋은 남고, 다음 실행에 파일을 고쳤으면 그 앞에 옮긴다
        var mine = store.SaveGradient(null, AssetSamples.Gradient("그 사이"));
        File.WriteAllText(setup.EffectsPath, Effects);
        File.WriteAllText(setup.GradientsPath, Gradients);
        var next = setup.Open();
        Assert.Null(AssetMigration.Run(next, setup.Legacy, Now).Error);
        Assert.Null(next.LibraryError);
        Assert.Equal(2, next.Catalog.Recipes.Count);
        Assert.Equal(["Dusk", "Plain", "그 사이"], next.Catalog.Gradients.Select(e => e.Name).ToArray());
        Assert.Equal(mine.Id, next.Catalog.Gradients[^1].Id);
    }

    /// <summary>예전 저장소(v1)에 개인 에셋 파일이 있었으면 프리셋은 그때 이미 옮겼다: 저장소를 옮긴 뒤에도 다시 옮기지 않는다. 개인 파일이 없던 저장소는 옮긴다.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Run_AfterVersionOneStoreMigration_RunsOnlyIfTheOldStoreHadNoPersonalFile(bool hadPersonalFile)
    {
        using var setup = new Setup();
        File.WriteAllText(setup.EffectsPath, Effects);
        var v1 = new Version1Layout(setup.Root);
        if (hadPersonalFile) v1.Personal([]);
        else v1.Installed([]);

        var store = setup.Open();
        Assert.True(store.HasLibraryFile);
        var result = AssetMigration.Run(store, setup.Legacy, Now);
        Assert.Equal(!hadPersonalFile, result.Ran);
        Assert.Equal(hadPersonalFile ? 0 : 2, setup.Open().Catalog.Recipes.Count);
        Assert.Equal(hadPersonalFile, File.Exists(setup.EffectsPath));
        // 어느 쪽이든 한 번뿐
        Assert.Same(AssetMigrationResult.Skipped, AssetMigration.Run(setup.Open(), setup.Legacy, Now));
    }

    [Fact]
    public void BackupNameCollision_GetsNumberSuffix()
    {
        using var setup = new Setup();
        File.WriteAllText(setup.EffectsPath, Effects);
        string taken = Path.Combine(setup.Legacy, $"effect-presets.backup-{Day}.json");
        File.WriteAllText(taken, "이전 backup");

        var result = AssetMigration.Run(setup.Open(), setup.Legacy, Now);
        Assert.Equal([Path.Combine(setup.Legacy, $"effect-presets.backup-{Day}-2.json")], result.Backups);
        Assert.Equal("이전 backup", File.ReadAllText(taken));
        Assert.Equal(Effects, File.ReadAllText(result.Backups[0]));
    }

    [Fact]
    public void MigratedUnknownEffect_SurvivesStoreReloadAndRewrite()
    {
        using var setup = new Setup();
        File.WriteAllText(setup.EffectsPath, Effects);
        var store = setup.Open();
        AssetMigration.Run(store, setup.Legacy, Now);
        string original = Json(AssetMigration.ReadLegacyEffects(Effects)[1].Effects)[0];

        // 다른 개인 에셋을 저장해 파일을 다시 써도 모르는 효과의 원본이 남는다
        store.SaveGradient(null, AssetSamples.Gradient("새 그라데이션"));
        var reopened = setup.Open();
        var recipe = reopened.Catalog.Recipes.Single(e => e.Name == "미래").Recipe!;
        var missing = Assert.IsType<MissingEffect>(recipe.Effects[0]);
        Assert.Equal("chickxel.effect.future", missing.TypeId);
        Assert.Equal(original, Json(recipe.Effects)[0]);
        var fade = Assert.IsType<ScriptEffect>(recipe.Effects[1]);
        Assert.Equal(OfficialEffects.HashOf("fade"), fade.ScriptHash);
        Assert.True(fade.Settings["fadeOut"] is true);
    }

    [Fact]
    public void LegacyNames_AreCleanedToValidAssetNames()
    {
        using var setup = new Setup();
        string longName = "긴" + new string('x', 150);
        File.WriteAllText(setup.EffectsPath, $$"""
            { "version": 2, "presets": [ { "name": "탭\t이름", "effects": [ { "type": "chickxel.effect.fade" } ] },
              { "name": "{{longName}}", "effects": [ { "type": "chickxel.effect.fade" } ] },
              { "name": "\u0001\u0002", "effects": [ { "type": "chickxel.effect.fade" } ] } ] }
            """);
        var store = setup.Open();
        AssetMigration.Run(store, setup.Legacy, Now);

        // 제어 문자뿐인 이름은 비지 않게 대신 붙인 이름으로 옮겨져 다시 열어도 남는다
        var names = setup.Open().Catalog.Recipes.Select(e => e.Name).ToArray();
        Assert.Equal(["탭이름", longName[..AssetManifest.MaxNameLength], Chickxel.Core.Localization.DomainStrings.FormatAssetMigration_UnnamedRecipe(number: 3)], names);
        Assert.All(names, name => Assert.True(AssetManifest.IsValidName(name)));
    }
}
