using Chickxel.Core.Assets;
using Chickxel.Core.IO;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 게시 흐름(5단계): 게시 창 검사가 공개 규칙(예전 카탈로그 CI와 같은 함수)으로 작성자 없는 패키지를 막고, 앱이 만든 패키지(에셋 하나, 고정 라이선스 CC0-1.0과
/// 그 원문 LICENSE.txt, 언어별 이름·설명)가 그대로 예전 정적 카탈로그 만들기·최신 확인을 통과하며 다른 컴퓨터(새 저장소)에 설치되는지,
/// 목록 미리보기, 패키지 ID 제안, 버전 제안과 게시 버전 규칙.
/// </summary>
public class AssetPublishTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    private const string NeonHit = "effect({ name: \"Neon Hit\", previewChick: \"knight\", render(img) { const d = img.data; for (let i = 0; i < d.length; i += 4) d[i] = 255 - d[i]; } });";

    /// <summary>이 컴퓨터에서 만든 스크립트 효과 하나와 그라데이션 하나가 든 저장소(효과 레시피는 올리지 않음).</summary>
    private static AssetStore PersonalStore(TempDir dir)
    {
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        store.SaveScript(NeonHit, null, null);
        store.SaveGradient(null, AssetSamples.Gradient());
        return store;
    }

    private static AssetEntry ScriptEntry(AssetStore store) => store.Catalog.All.Single(e => e.Script is not null && e.Author is null);

    private static PublishAsset Asset(AssetEntry entry) => new(entry.Kind, entry.Data, AssetPreview.Package(entry.Data));

    private static PublishRequest Request(AssetStore store, string version = "1.0.0") => new()
    {
        Author = new AssetAuthor("pixel-cat", "Pixel Cat"),
        Id = "pixel-cat.neon",
        Version = version,
        Name = "Neon Hit",
        Summary = "네온처럼 색을 뒤집는 효과",
        Translations = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("Neon Hit", "Inverts colors like neon."),
            ["ja"] = new("ネオンヒット", null),
        },
        Tags = AssetPublishCheck.ParseTags("#Neon #effect, hit"),
        Asset = Asset(ScriptEntry(store)),
        MinAppVersion = "1.14.55",
    };

    private static PublishResult Check(PublishRequest request, IReadOnlyList<RemoteCatalogEntry>? catalog = null, IEnumerable<PublishedVersion>? saved = null)
        => AssetPublishCheck.Check(request, catalog ?? [], saved ?? []);

    /// <summary>관리자처럼 패키지와 검토 정보를 예전 정적 카탈로그 원본 <paramref name="catalog"/>의 <c>&lt;ID&gt;/</c>에 둔다.</summary>
    private static void Unpack(PublishResult result, string catalog, string? summary = null, DateOnly? updated = null)
    {
        var manifest = result.Manifest!;
        string folder = Path.Combine(catalog, manifest.Id);
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, result.PackageFileName!), result.Package!);
        File.WriteAllBytes(Path.Combine(folder, RemoteCatalogBuilder.ReviewFileName),
            RemoteCatalogBuilder.WriteReview(summary ?? manifest.Description!, result.Tags, RemoteReview.Reviewed, Today, updated ?? Today, null));
    }

    private static AssetPackage Read(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        return AssetPackageValidator.Read(stream, new AssetValidationOptions());
    }

    // ───────────── 작성자·라이선스 없는 공개 제출 ─────────────

    private static readonly Dictionary<string, Func<PublishRequest, PublishRequest>> BadRequests = new()
    {
        ["no-author"] = r => r with { Author = new AssetAuthor("", "") },
        ["no-author-name"] = r => r with { Author = new AssetAuthor("pixel-cat", " ") },
        ["personal-author"] = r => r with { Author = new AssetAuthor("personal", "Me"), Id = "personal.neon" },
        ["other-author-namespace"] = r => r with { Id = "chickxel.neon" },
        ["no-preview"] = r => r with { Asset = r.Asset with { Preview = null } },
        ["no-summary"] = r => r with { Summary = " " },
        ["bad-tag"] = r => r with { Tags = ["Neon!"] },
        ["too-many-tags"] = r => r with { Tags = ["a", "b", "c", "d", "e", "f", "g", "h", "i"] },
        ["bad-version"] = r => r with { Version = "1.0" },
        ["bad-language"] = r => r with { Translations = new Dictionary<string, AssetTranslation> { ["english!"] = new("Neon", null) } },
        ["empty-translation"] = r => r with { Translations = new Dictionary<string, AssetTranslation> { ["en"] = new(null, null) } },
        ["long-translated-summary"] = r => r with { Translations = new Dictionary<string, AssetTranslation> { ["en"] = new(null, new string('a', 201)) } },
        ["recipe"] = r => r with { Asset = new PublishAsset(AssetKinds.EffectRecipe, AssetSamples.Recipe(), AssetPreview.Package(AssetSamples.Recipe())) },
    };

    public static TheoryData<string, string> BadRequestCodes => new()
    {
        { "no-author", nameof(PublishIssueCode.AuthorId) },
        { "no-author-name", nameof(PublishIssueCode.AuthorName) },
        { "personal-author", nameof(CatalogBuildIssueCode.MissingAuthor) },
        { "other-author-namespace", nameof(CatalogBuildIssueCode.AuthorMismatch) },
        { "no-preview", nameof(CatalogBuildIssueCode.MissingPreview) },
        { "no-summary", nameof(PublishIssueCode.Summary) },
        { "bad-tag", nameof(PublishIssueCode.Tags) },
        { "too-many-tags", nameof(PublishIssueCode.Tags) },
        { "bad-version", nameof(PublishIssueCode.Version) },
        { "bad-language", nameof(PublishIssueCode.Translations) },
        { "empty-translation", nameof(PublishIssueCode.Translations) },
        { "long-translated-summary", nameof(PublishIssueCode.Translations) },
        { "recipe", nameof(PublishIssueCode.RecipeNotPublishable) },
    };

    [Theory]
    [MemberData(nameof(BadRequestCodes))]
    public void PublicSubmission_BrokenRule_IsAnErrorThatBlocksUploading(string label, string code)
    {
        using var dir = new TempDir();
        var result = Check(BadRequests[label](Request(PersonalStore(dir))));
        Assert.False(result.Ok, label);
        Assert.Contains(result.Issues, issue => issue.IsError && (issue.Code?.ToString() ?? issue.CatalogCode?.ToString()) == code);
        Assert.All(result.Issues, issue => Assert.False(string.IsNullOrWhiteSpace(issue.Message)));
    }

    [Fact]
    public void OfficialAuthor_IsCheckedAsAnOfficialPackage()
    {
        // 서버와 같이 작성자 chickxel이면 official(관리자 계정만 그 핸들을 가짐 — 서버가 소유를 본다).
        using var dir = new TempDir();
        var result = Check(Request(PersonalStore(dir)) with { Author = new AssetAuthor("chickxel", "Chickxel"), Id = "chickxel.neon" });
        Assert.True(result.Ok, string.Join("\n", result.Issues));
    }

    [Fact]
    public void LayerReadingScript_MustCarryTheLayerReferenceTag()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        var saved = store.SaveScript(
            "effect({ name: \"Ground Glow\", tags: [\"glow\"], settings: [{ key: \"ground\", type: \"layer\", default: \"ground\" }], "
            + "render(img, ctx) { const g = ctx.layer(\"ground\"); if (g) img.data[0] = g.data[0]; } });", null, null);
        // 저장한 에셋의 태그(게시 창의 처음 값)에는 강제 태그가 붙어 있다
        Assert.Equal(["glow", ScriptAsset.LayerReferenceTag], saved.Script!.Tags);
        var request = Request(store) with { Id = "pixel-cat.ground-glow", Name = "Ground Glow", Translations = AssetTranslations.Empty, Asset = Asset(saved) };
        PublishIssueCode? Code(IReadOnlyList<string> tags)
            => Check(request with { Tags = tags }).Issues.FirstOrDefault(i => i.Code is PublishIssueCode.LayerReferenceTag or PublishIssueCode.LayerReferenceTagFull)?.Code;

        Assert.Equal(PublishIssueCode.LayerReferenceTag, Code(["glow"]));
        Assert.Equal(PublishIssueCode.LayerReferenceTag, Code([]));
        // 이미 8개면 더할 자리가 없다: 태그를 하나 빼라는 오류
        Assert.Equal(PublishIssueCode.LayerReferenceTagFull, Code([.. Enumerable.Range(1, 8).Select(i => "t" + i)]));
        Assert.Null(Code(["glow", ScriptAsset.LayerReferenceTag]));
        var ok = Check(request with { Tags = saved.Script!.Tags });
        Assert.True(ok.Ok, string.Join("\n", ok.Issues));

        // 다른 레이어를 읽지 않는 스크립트는 그 태그가 없어도 된다
        using var other = new TempDir();
        Assert.DoesNotContain(Check(Request(PersonalStore(other)) with { Tags = ["neon"] }).Issues,
            i => i.Code is PublishIssueCode.LayerReferenceTag or PublishIssueCode.LayerReferenceTagFull);
    }

    [Fact]
    public void FrameReadingScript_MustCarryTheFrameReferenceTag()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        var saved = store.SaveScript(
            "effect({ name: \"Echo\", tags: [\"echo\"], render(img, ctx) { const f = ctx.renderedFrameSource(0); if (f) img.data[0] = f.data[0]; } });", null, null);
        Assert.True(saved.Script!.ReadsOtherFrames);
        Assert.False(saved.Script!.ReadsOtherLayers);
        Assert.Equal(["echo", ScriptAsset.FrameReferenceTag], saved.Script!.Tags);
        var request = Request(store) with { Id = "pixel-cat.echo", Name = "Echo", Translations = AssetTranslations.Empty, Asset = Asset(saved) };
        PublishIssueCode? Code(IReadOnlyList<string> tags)
            => Check(request with { Tags = tags }).Issues.FirstOrDefault(i => i.Code is PublishIssueCode.FrameReferenceTag or PublishIssueCode.FrameReferenceTagFull)?.Code;

        Assert.Equal(PublishIssueCode.FrameReferenceTag, Code(["echo"]));
        Assert.Equal(PublishIssueCode.FrameReferenceTagFull, Code([.. Enumerable.Range(1, 8).Select(i => "t" + i)]));
        Assert.Null(Code(["echo", ScriptAsset.FrameReferenceTag]));
        var ok = Check(request with { Tags = saved.Script!.Tags });
        Assert.True(ok.Ok, string.Join("\n", ok.Issues));

        // 레이어와 프레임을 둘 다 읽으면 두 태그가 다 붙고, 둘 다 빠졌을 때는 둘을 더할 자리가 있어야 한다
        var both = store.SaveScript(
            "effect({ name: \"Ground Echo\", tags: [\"echo\"], settings: [{ key: \"ground\", type: \"layer\", default: \"ground\" }], "
            + "render(img, ctx) { const g = ctx.layer(\"ground\"); const f = ctx.frameSource(0); if (g && f) img.data[0] = g.data[0] + f.data[0]; } });", null, null);
        Assert.Equal(["echo", ScriptAsset.LayerReferenceTag, ScriptAsset.FrameReferenceTag], both.Script!.Tags);
        var bothRequest = request with { Id = "pixel-cat.ground-echo", Name = "Ground Echo", Asset = Asset(both) };
        var seven = Check(bothRequest with { Tags = [.. Enumerable.Range(1, 7).Select(i => "t" + i)] }).Issues.Select(i => i.Code).ToList();
        Assert.Contains(PublishIssueCode.LayerReferenceTag, seven);
        Assert.Contains(PublishIssueCode.FrameReferenceTagFull, seven);
    }

    [Fact]
    public void PivotMovingScript_GetsThePivotMoveTagFromItsCode_AndMustKeepIt()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        // 선언의 tags에 없어도 원문이 ctx.movePivot을 쓰면 저절로 붙는다
        var saved = store.SaveScript(
            "effect({ name: \"Hop\", tags: [\"jump\"], animated: true, render(img, ctx) { ctx.movePivot(0, -1); } });", null, null);
        Assert.True(saved.Script!.MovesPivot);
        Assert.Equal(["jump", ScriptAsset.PivotMoveTag], saved.Script!.Tags);
        var request = Request(store) with { Id = "pixel-cat.hop", Name = "Hop", Translations = AssetTranslations.Empty, Asset = Asset(saved) };
        PublishIssueCode? Code(IReadOnlyList<string> tags)
            => Check(request with { Tags = tags }).Issues.FirstOrDefault(i => i.Code is PublishIssueCode.PivotMoveTag or PublishIssueCode.PivotMoveTagFull)?.Code;

        Assert.Equal(PublishIssueCode.PivotMoveTag, Code(["jump"]));
        Assert.Equal(PublishIssueCode.PivotMoveTagFull, Code([.. Enumerable.Range(1, 8).Select(i => "t" + i)]));
        Assert.Null(Code(["jump", ScriptAsset.PivotMoveTag]));
        var ok = Check(request with { Tags = saved.Script!.Tags });
        Assert.True(ok.Ok, string.Join("\n", ok.Issues));

        // 옮기지 않는 스크립트에는 붙지 않는다
        using var other = new TempDir();
        Assert.DoesNotContain(Check(Request(PersonalStore(other)) with { Tags = ["neon"] }).Issues,
            i => i.Code is PublishIssueCode.PivotMoveTag or PublishIssueCode.PivotMoveTagFull);
    }

    [Fact]
    public void SubmissionsWithoutLicenseOrAuthor_AreRejectedByTheCatalogBuilder_WhileAppPackagesAlwaysCarryTheFixedLicense()
    {
        // 게시 창은 라이선스를 고정(CC0-1.0 + 원문 LICENSE.txt)하므로 라이선스 문제가 생기지 않는다. 직접 만든 패키지는 카탈로그 만들기가 거부한다.
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        foreach (var (label, author, id, license, code) in new[]
                 {
                     ("no-license-file", new AssetAuthor("pixel-cat", "Pixel Cat"), "pixel-cat.neon", "LicenseRef-MyTerms", CatalogBuildIssueCode.MissingLicense),
                     ("personal-license", new AssetAuthor("pixel-cat", "Pixel Cat"), "pixel-cat.neon", "LicenseRef-Personal", CatalogBuildIssueCode.MissingLicense),
                     ("personal-author", new AssetAuthor("personal", "Me"), "personal.neon", "CC0-1.0", CatalogBuildIssueCode.MissingAuthor),
                 })
        {
            var header = new AssetManifest { Id = id, Version = "1.0.0", Name = "Neon", Author = author, License = license, MinAppVersion = "1.0.0" };
            using var package = new MemoryStream();
            store.Export(store.Catalog.All.Select(e => e.Id), header, package, preview: entry => AssetPreview.Package(entry.Data));
            string folder = Path.Combine(dir.Path, label, "catalog", id);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "pack" + AssetPackage.Extension), package.ToArray());
            File.WriteAllBytes(Path.Combine(folder, RemoteCatalogBuilder.ReviewFileName),
                RemoteCatalogBuilder.WriteReview("요약", ["neon"], RemoteReview.Reviewed, Today, Today, null));

            var build = RemoteCatalogBuilder.Build(Path.GetDirectoryName(folder)!);
            Assert.False(build.Ok, label);
            Assert.Contains(build.Issues, issue => issue.Code == code);
        }
        var app = Check(Request(store));
        Assert.True(app.Ok, string.Join("\n", app.Issues));
        Assert.DoesNotContain(app.Issues, issue => issue.CatalogCode == CatalogBuildIssueCode.MissingLicense);
    }

    // ───────────── 앱이 만든 패키지: CI 통과와 다른 컴퓨터 설치 ─────────────

    [Fact]
    public void AppMadePackage_HasOneAssetFixedLicenseAndTranslations_PassesCatalogBuild_AndInstallsIntoAFreshStore()
    {
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var result = Check(Request(store));
        Assert.True(result.Ok, string.Join("\n", result.Issues));
        Assert.Empty(result.Issues);
        Assert.Equal("pixel-cat.neon-1.0.0.chickxel-asset", result.PackageFileName);

        // 패키지: 에셋 하나, 라이선스 CC0-1.0과 CC0 1.0 원문, 작성자 주소 없음, 언어별 이름·설명.
        var package = Read(result.Package!);
        var manifest = package.Manifest;
        Assert.Equal(AssetKinds.EffectScript, Assert.Single(manifest.Assets).Kind);
        Assert.Equal(("CC0-1.0", new AssetAuthor("pixel-cat", "Pixel Cat")), (manifest.License, manifest.Author));
        string license = System.Text.Encoding.UTF8.GetString(package.Files[AssetPublishCheck.LicenseFileName]);
        Assert.Equal(AssetPublishCheck.LicenseText, license);
        Assert.Contains("CC0 1.0 Universal", license, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', license);
        Assert.Equal(["en", "ja"], manifest.Translations.Keys);
        Assert.Equal(new AssetTranslation("Neon Hit", "Inverts colors like neon."), manifest.Translations["EN"]);
        Assert.Equal(new AssetTranslation("ネオンヒット", null), manifest.Translations["ja"]);
        Assert.Equal(["neon", "effect", "hit"], result.Tags);

        // 관리자 흐름: 패키지를 catalog/에 두고 만들기 → 쓰기 → --check(다시 만들어 최신인지).
        string catalog = Path.Combine(dir.Path, "repo", "catalog");
        string output = Path.Combine(dir.Path, "repo", "docs", "catalog");
        Directory.CreateDirectory(catalog);
        Unpack(result, catalog);
        var build = RemoteCatalogBuilder.Build(catalog, output);
        Assert.True(build.Ok, string.Join("\n", build.Issues));
        RemoteCatalogBuilder.Write(build, output);
        var check = RemoteCatalogBuilder.Build(catalog, output);
        Assert.True(check.Ok, string.Join("\n", check.Issues));
        Assert.Empty(RemoteCatalogBuilder.Stale(check, output));

        var entry = Assert.Single(build.Entries);
        Assert.Equal(("pixel-cat.neon", "1.0.0", "Neon Hit", "네온처럼 색을 뒤집는 효과", "CC0-1.0", RemoteReview.Reviewed),
            (entry.Id, entry.Version, entry.Name, entry.Summary, entry.License, entry.Review));
        Assert.Equal(new AssetTranslation("Neon Hit", "Inverts colors like neon."), entry.Translations["en"]);
        Assert.Equal(new AssetAuthor("pixel-cat", "Pixel Cat"), entry.Author);
        // index 파일을 다시 읽어도 번역이 남는다.
        var page = RemoteCatalogFormat.Read(File.ReadAllBytes(Path.Combine(output, RemoteCatalogFormat.FirstPageName)));
        Assert.Equal(new AssetTranslation("ネオンヒット", null), Assert.Single(page.Packages).Translations["ja"]);
        byte[] published = File.ReadAllBytes(Path.Combine(output, entry.Package.Url));
        Assert.Equal(result.Package, published);

        // 다른 컴퓨터: 새 저장소에 게시된 파일을 설치하면 같은 스크립트 효과가 그 상점 패키지의 작성자·라이선스·연결을 달고 생기고, 목록 이름은 화면 언어로 보인다.
        var other = AssetStore.Open(Path.Combine(dir.Path, "other", "Assets"));
        var script = Assert.Single(other.InstallFile(AssetSamples.Save(dir.Path, published, "downloaded.chickxel-asset"), AssetSources.Remote));
        Assert.Equal(script, Assert.Single(other.Catalog.Available(AssetKinds.EffectScript)));
        Assert.Equal(("Neon Hit", "pixel-cat.neon.neon-hit", "pixel-cat.neon", "1.0.0", AssetSources.Remote), (script.Name, script.Id, script.Store!.PackageId, script.Store.Version, script.Source));
        Assert.Equal((new AssetAuthor("pixel-cat", "Pixel Cat"), "CC0-1.0", false), (script.Author, script.License, script.Modified));
        Assert.Equal(ScriptEntry(store).Script!.Hash, script.Script!.Hash);
        Assert.Equal(AssetPublishCheck.LicenseText, File.ReadAllText(script.LicenseFilePath!));
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("ja-JP");
            Assert.Equal(("ネオンヒット", "네온처럼 색을 뒤집는 효과"), (script.Store.DisplayName, script.Store.DisplayDescription));
            Assert.Equal(("ネオンヒット", "네온처럼 색을 뒤집는 효과"), (page.Packages[0].DisplayName, page.Packages[0].DisplaySummary));
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("Inverts colors like neon.", page.Packages[0].DisplaySummary);
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo("fr-FR");
            Assert.Equal(("Neon Hit", "네온처럼 색을 뒤집는 효과"), (page.Packages[0].DisplayName, page.Packages[0].DisplaySummary));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void ListPreview_IsTheAssetPreview_AndACustomImageReplacesIt()
    {
        // 서버는 첫(유일한) 에셋의 미리보기를 목록 미리보기로 쓴다.
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var gradient = store.Catalog.All.Single(e => e.Gradient is not null);
        var request = Request(store) with { Asset = Asset(gradient) };
        var result = Check(request);
        Assert.True(result.Ok, string.Join("\n", result.Issues));
        var package = Read(result.Package!);
        Assert.Equal(AssetKinds.Gradient, Assert.Single(package.Manifest.Assets).Kind);
        Assert.Equal(request.Asset.Preview!.Png, package.Files[AssetPublishCheck.ListPreview(package.Manifest, null)!]);

        // 직접 고른 그림은 효과 없는 원본과 짝이 맞지 않으므로 스크립트 효과의 원본은 빠진다(서버가 크기가 다른 원본을 거부함).
        byte[] custom = AssetPreview.Package(new GradientAsset("", AssetSamples.Gradient("Cover")))!.Png;
        var withCustom = Check(Request(store) with { CustomPreview = custom });
        Assert.True(withCustom.Ok, string.Join("\n", withCustom.Issues));
        var customPackage = Read(withCustom.Package!);
        Assert.Equal(custom, customPackage.Files[Assert.Single(customPackage.Manifest.Assets).Preview!]);
        Assert.DoesNotContain(customPackage.Files.Keys, path => path.EndsWith(".original.png", StringComparison.Ordinal));
    }

    // ───────────── 태그·패키지 ID ─────────────

    [Fact]
    public void ParseTags_SplitsOnHashCommaAndSpace_LowercasesAndDropsDuplicates()
        => Assert.Equal(["neon", "hit", "glow"], AssetPublishCheck.ParseTags(" #Neon #hit,glow#NEON\n#"));

    [Theory]
    [InlineData("Neon Hit", null, "pixel-cat.neon-hit")]
    [InlineData("늘었다 줄기", "Stretch & Squash", "pixel-cat.stretch-squash")]
    [InlineData("  ", "Glow", "pixel-cat.glow")]
    public void DefaultId_IsHandleDotSlugOfTheFirstAsciiName(string name, string? english, string expected)
        => Assert.Equal(expected, AssetPublishCheck.DefaultId("pixel-cat", name, english));

    [Fact]
    public void DefaultId_WithoutAsciiLetters_FallsBackToAShortStableHash()
    {
        string id = AssetPublishCheck.DefaultId("pixel-cat", "늘었다 줄기");
        Assert.Matches("^pixel-cat\\.[0-9a-f]{8}$", id);
        Assert.True(AssetManifest.IsValidId(id));
        Assert.Equal(id, AssetPublishCheck.DefaultId("pixel-cat", "늘었다 줄기", null));
        Assert.NotEqual(id, AssetPublishCheck.DefaultId("pixel-cat", "반짝이"));
    }

    // ───────────── 패키지 ID 정하기(같은 핸들 + 같은 상점 ID = 업데이트) ─────────────

    /// <summary>요청으로 만든 패키지를 <paramref name="store"/>에 설치한다(다른 컴퓨터에서 내 패키지를 내려받은 것처럼 — 같은 내용의 내 에셋과 따로 둠).</summary>
    private static AssetEntry InstallPublished(AssetStore store, TempDir dir, PublishRequest request)
    {
        var result = Check(request);
        Assert.True(result.Ok, string.Join("\n", result.Issues));
        return Assert.Single(store.Install(AssetSamples.Save(dir.Path, result.Package!, request.Id + AssetPackage.Extension), AssetSources.Remote,
            _ => ImportChoice.KeepBoth));
    }

    [Fact]
    public void ResolveTarget_MyListedAsset_KeepsItsStoreId_EvenAfterEditingInPlace()
    {
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var received = InstallPublished(store, dir, Request(store) with { Id = "pixel-cat.old-name", Name = "Old name" });

        // 내 핸들로 상점에 올라간 에셋 → 그 상점 ID(이름을 바꿔도)
        var own = AssetPublishCheck.ResolveTarget("pixel-cat", received, store.Catalog, "Renamed");
        Assert.Equal(("pixel-cat.old-name", PublishIdSource.OwnPackage, received.Id), (own.Id, own.Source, own.Existing?.Id));

        // 그 자리에서 고쳐도(고친 것으로 보임) 같은 상점 ID의 새 버전
        var edited = store.SaveScript(NeonHit.Replace("255 - d[i]", "254 - d[i]", StringComparison.Ordinal), received.Id, null);
        Assert.True(edited.Modified);
        Assert.Equal(("pixel-cat.old-name", PublishIdSource.OwnPackage), Pair(AssetPublishCheck.ResolveTarget("pixel-cat", edited, store.Catalog, "Neon Hit")));

        // 이 컴퓨터에서 만든 같은 원문의 에셋은 상점 연결이 없다 → 핸들.이름
        var local = AssetPublishCheck.ResolveTarget("pixel-cat", ScriptEntry(store), store.Catalog, "Renamed");
        Assert.Equal(("pixel-cat.renamed", PublishIdSource.Derived, null), (local.Id, local.Source, local.Existing));

        // 올린 뒤 연결을 붙이면 내 상점 에셋
        var marked = store.MarkPublished(ScriptEntry(store).Id, new AssetAuthor("pixel-cat", "Pixel Cat"), AssetPublishCheck.License,
            AssetStoreLink.FromManifest(Check(Request(store)).Manifest!, "pixel-cat.neon.neon-hit", ScriptEntry(store).ContentHash));
        Assert.Equal((false, false), (marked.Modified, marked.IsOwnedBy("dog")));
        Assert.Equal(("pixel-cat.neon", PublishIdSource.OwnPackage), Pair(AssetPublishCheck.ResolveTarget("pixel-cat", marked, store.Catalog, "Neon Hit")));

        // 다른 핸들로 로그인하면 내 것이 아니다 → 그 핸들.이름
        var other = AssetPublishCheck.ResolveTarget("dog", received, store.Catalog, "Neon Hit");
        Assert.Equal(("dog.neon-hit", PublishIdSource.Derived, null), (other.Id, other.Source, other.Existing));
    }

    private static (string, PublishIdSource) Pair(PublishTarget target) => (target.Id, target.Source);

    [Fact]
    public void ResolveTarget_SomeoneElsesAsset_GetsMyHandle()
    {
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var theirs = InstallPublished(store, dir, Request(store) with { Author = new AssetAuthor("other-cat", "Other Cat"), Id = "other-cat.neon" });
        Assert.False(theirs.IsOwnedBy("pixel-cat"));
        var target = AssetPublishCheck.ResolveTarget("pixel-cat", theirs, store.Catalog, theirs.Name);
        Assert.Equal(("pixel-cat.neon-hit", PublishIdSource.Derived, null), (target.Id, target.Source, target.Existing));
    }

    [Fact]
    public void ResolveTarget_Otherwise_DerivesFromTheName_AndPointsAtMyAssetListedUnderThatId()
    {
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var gradient = store.Catalog.All.Single(e => e.Gradient is not null);
        var fresh = AssetPublishCheck.ResolveTarget("pixel-cat", ScriptEntry(store), store.Catalog, "Neon Hit");
        Assert.Equal(("pixel-cat.neon-hit", PublishIdSource.Derived, null), (fresh.Id, fresh.Source, fresh.Existing));

        // 이름에서 나온 ID가 다른 내용의 내 상점 에셋(그라데이션)과 같으면 그 에셋을 가리킨다(같은 ID라 업데이트 — 창이 알림).
        var mine = InstallPublished(store, dir, Request(store) with { Id = "pixel-cat.neon-hit", Asset = Asset(gradient) });
        var same = AssetPublishCheck.ResolveTarget("pixel-cat", ScriptEntry(store), store.Catalog, "Neon Hit");
        Assert.Equal(("pixel-cat.neon-hit", PublishIdSource.Derived, mine.Id), (same.Id, same.Source, same.Existing?.Id));
    }

    /// <summary>요청으로 만든 패키지를 <paramref name="store"/>에 앱의 상점 받기 규칙(같은 상점 에셋이면 바꾸고, 아니면 하나 더 둠)으로 설치한다.</summary>
    private static void Download(AssetStore store, TempDir dir, PublishResult result)
    {
        Assert.True(result.Ok, string.Join("\n", result.Issues));
        store.Install(AssetSamples.Save(dir.Path, result.Package!, result.PackageFileName!), AssetSources.Remote,
            item => item.SameStoreAsset ? ImportChoice.Replace : ImportChoice.KeepBoth);
    }

    [Fact]
    public void NewVersion_KeepsTheAssetIdAfterRenaming_AndReplacesTheCopyOnAnotherComputer()
    {
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var request = Request(store);
        var first = Check(request);
        string assetId = first.Manifest!.Assets[0].Id;
        var published = store.MarkPublished(ScriptEntry(store).Id, request.Author, AssetPublishCheck.License,
            AssetStoreLink.FromManifest(first.Manifest, assetId, ScriptEntry(store).ContentHash));
        var other = AssetStore.Open(Path.Combine(dir.Path, "Other"));
        Download(other, dir, first);

        // 이름을 바꿔 새 버전으로 올려도 에셋 ID는 지난 버전의 것
        var renamed = store.SaveScript(NeonHit.Replace("Neon Hit", "Neon Flash", StringComparison.Ordinal), published.Id, null);
        var target = AssetPublishCheck.ResolveTarget("pixel-cat", renamed, store.Catalog, "Neon Flash");
        var next = request with { Version = "1.0.1", Name = "Neon Flash", Asset = Asset(renamed) };
        var second = Check(next with { AssetSegment = AssetPublishCheck.AssetSegmentOf(target) });
        Assert.Equal(assetId, second.Manifest!.Assets[0].Id);
        Download(other, dir, second);
        var updated = Assert.Single(other.Catalog.All);
        Assert.Equal(("Neon Flash", "1.0.1", assetId), (updated.Name, updated.Store!.Version, updated.Store.AssetId));

        // 예전 게시 창은 이름에서 에셋 ID를 다시 만들었다 — 그 패키지도 받은 것을 바꾼다(에셋 하나뿐인 패키지는 패키지 ID가 정체)
        var legacy = Check(next with { Version = "1.0.2" });
        Assert.NotEqual(assetId, legacy.Manifest!.Assets[0].Id);
        Download(other, dir, legacy);
        Assert.Equal((updated.Id, "1.0.2"), (Assert.Single(other.Catalog.All).Id, other.Catalog.All[0].Store!.Version));
    }

    [Fact]
    public void AssetSegmentOf_OfficialPackageUsesTheBundledAssetId_FirstUploadUsesTheName()
    {
        Assert.Equal("outline", AssetPublishCheck.AssetSegmentOf(new PublishTarget(OfficialEffectPackages.PackageId("outline"), PublishIdSource.OwnPackage, null)));
        Assert.Null(AssetPublishCheck.AssetSegmentOf(new PublishTarget("pixel-cat.neon", PublishIdSource.Derived, null)));
    }

    [Fact]
    public void MarkPublished_SupersedesOtherCopiesOfThePackage_RemovingUneditedAndUnlinkingEdited()
    {
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var local = ScriptEntry(store);
        var request = Request(store);
        // 받아 둔 지난 버전 둘: 그대로인 것(즐겨찾기)과 그 자리에서 고친 것
        var received = InstallPublished(store, dir, request);
        store.SetFavorite(received.Id, true);
        var copy = InstallPublished(store, dir, request);
        string edited = NeonHit.Replace("255 - d[i]", "254 - d[i]", StringComparison.Ordinal);
        Assert.True(store.SaveScript(edited, copy.Id, null).Modified);

        var result = Check(request with { Version = "1.0.1" });
        store.MarkPublished(local.Id, request.Author, AssetPublishCheck.License, AssetStoreLink.FromManifest(result.Manifest!, result.Manifest!.Assets[0].Id, local.ContentHash));
        Assert.Equal(local.Id, Assert.Single(store.Catalog.FromPackage("pixel-cat.neon")).Id);
        Assert.Null(store.Catalog.Find(received.Id));
        Assert.True(store.Catalog.Find(local.Id)!.Favorite);
        var kept = store.Catalog.Find(copy.Id)!;
        Assert.Equal(((AssetStoreLink?)null, (AssetAuthor?)null, ScriptEffectProgram.HashOf(edited)), (kept.Store, kept.Author, kept.Script!.Hash));
    }

    // ───────────── 버전 ─────────────

    [Theory]
    [InlineData(null, "1.0.0")]
    [InlineData("not-a-version", "1.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.2.3", "1.2.4")]
    [InlineData("2.0.0-beta.1", "2.0.0")]
    public void SuggestVersion_FirstIsOneZero_ReleaseRaisesPatch_PrereleaseBecomesRelease(string? previous, string expected)
        => Assert.Equal(expected, AssetPublishCheck.SuggestVersion(previous));

    [Fact]
    public void PublishedVersions_SameVersionMustKeepContent_NoDowngrade_SuggestedBumpPasses()
    {
        using var dir = new TempDir();
        var store = PersonalStore(dir);
        var first = Check(Request(store, "1.2.0"));
        Assert.True(first.Ok, string.Join("\n", first.Issues));

        // 게시된 1.2.0(온라인 목록)
        string catalog = Path.Combine(dir.Path, "catalog");
        string output = Path.Combine(dir.Path, "docs-catalog");
        Directory.CreateDirectory(catalog);
        Unpack(first, catalog);
        var build = RemoteCatalogBuilder.Build(catalog, output);
        RemoteCatalogBuilder.Write(build, output);
        var online = build.Entries;

        // 같은 버전은 내용이 같아도 거부(올린 버전은 바꿀 수 없음). 패키지 바이트는 같은 입력이면 같다.
        var again = Check(Request(store, "1.2.0"), online);
        Assert.Equal(PublishIssueCode.VersionNotNewer, Assert.Single(again.Issues, i => i.IsError).Code);
        Assert.Equal(first.Package, again.Package);

        // 내용을 바꾸고 같은 버전 → 거부, 낮은 버전 → 거부, 제안 버전 → 통과(게시 날짜는 처음 날짜를 이음).
        store.SaveScript(NeonHit.Replace("255 - d[i]", "254 - d[i]", StringComparison.Ordinal), ScriptEntry(store).Id, null);
        Assert.Contains(Check(Request(store, "1.2.0"), online).Issues, i => i.Code == PublishIssueCode.VersionNotNewer);
        Assert.Contains(Check(Request(store, "1.1.9"), online).Issues, i => i.Code == PublishIssueCode.VersionNotNewer);
        string next = AssetPublishCheck.SuggestVersion(AssetPublishCheck.LatestVersion("pixel-cat.neon", online, []));
        Assert.Equal("1.2.1", next);
        var bumped = Check(Request(store, next), online);
        Assert.True(bumped.Ok, string.Join("\n", bumped.Issues));

        // 관리자가 새 버전으로 바꾸면 CI도 통과한다.
        Directory.Delete(Path.Combine(catalog, "pixel-cat.neon"), recursive: true);
        Unpack(bumped, catalog, "새 두께", Today.AddDays(3));
        var rebuilt = RemoteCatalogBuilder.Build(catalog, output);
        Assert.True(rebuilt.Ok, string.Join("\n", rebuilt.Issues));
        Assert.Equal("1.2.1", Assert.Single(rebuilt.Entries).Version);

        // 이 컴퓨터의 기록도 센다: 가장 높은 버전(프리릴리스 포함)보다 높아야 한다.
        PublishedVersion[] saved = [new("pixel-cat.neon", "1.4.0-rc.1", "x"), new("pixel-cat.neon", "1.3.0", first.Sha256!)];
        Assert.Equal("1.4.0-rc.1", AssetPublishCheck.LatestVersion("pixel-cat.neon", online, saved));
        Assert.Contains(Check(Request(store, "1.3.0"), online, saved).Issues, i => i.Code == PublishIssueCode.VersionNotNewer);
        var above = Check(Request(store, "1.4.0"), online, saved);
        Assert.True(above.Ok, string.Join("\n", above.Issues));
    }
}
