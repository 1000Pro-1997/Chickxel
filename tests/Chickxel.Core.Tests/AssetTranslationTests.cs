using System.Globalization;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;

namespace Chickxel.Core.Tests;

/// <summary>패키지 언어별 이름·짧은 설명(manifest·원격 카탈로그 <c>translations</c>): 검사, 쓰고 다시 읽기, 화면 언어 고르기.</summary>
public class AssetTranslationTests
{
    private static JsonObject Manifest(JsonNode? translations)
    {
        var json = new AssetManifest
        {
            Id = "pixel-cat.neon", Version = "1.0.0", Name = "네온", Description = "네온 효과", Author = new AssetAuthor("pixel-cat", "Pixel Cat"),
            License = "CC0-1.0", MinAppVersion = "1.0.0", Assets = [new AssetManifestEntry("pixel-cat.neon.a", AssetKinds.Gradient, "assets/gradients/a.json")],
            Files = new Dictionary<string, string> { ["assets/gradients/a.json"] = new string('0', 64) },
        }.ToJson();
        if (translations is not null) json["translations"] = translations.DeepClone();
        return json;
    }

    private static (AssetManifest? Manifest, List<AssetIssue> Issues) Read(JsonNode? translations)
    {
        var issues = new List<AssetIssue>();
        return (AssetManifest.Read(Manifest(translations), issues), issues);
    }

    [Fact]
    public void Manifest_Translations_RoundTrip_TrimmedAndCaseInsensitive()
    {
        var (manifest, issues) = Read(JsonNode.Parse("""{ "en": { "name": " Neon ", "summary": "Glowing outline.", "future": 1 }, "zh-Hans": { "summary": "霓虹" } }"""));
        Assert.Empty(issues);
        Assert.Equal(new AssetTranslation("Neon", "Glowing outline."), manifest!.Translations["EN"]);
        Assert.Equal(new AssetTranslation(null, "霓虹"), manifest.Translations["zh-hans"]);

        var again = AssetManifest.Read(JsonNode.Parse(manifest.Serialize()), issues)!;
        Assert.Empty(issues);
        Assert.Equal(["en", "zh-Hans"], again.Translations.Keys);
        Assert.Equal(manifest.Translations["en"], again.Translations["en"]);
        Assert.Null(((JsonObject)again.ToJson()["translations"]!)["zh-Hans"]!["name"]);
    }

    [Fact]
    public void Manifest_WithoutTranslations_WritesNoField()
    {
        var (manifest, issues) = Read(null);
        Assert.Empty(issues);
        Assert.Empty(manifest!.Translations);
        Assert.False(manifest.ToJson().ContainsKey("translations"));
    }

    [Theory]
    [InlineData("""[]""", "translations")]
    [InlineData("""{ "english!": { "name": "Neon" } }""", "translations.english!")]
    [InlineData("""{ "e": { "name": "Neon" } }""", "translations.e")]
    [InlineData("""{ "en": { "name": "Neon" }, "EN": { "name": "Neon" } }""", "translations.EN")]
    [InlineData("""{ "en": "Neon" }""", "translations.en")]
    [InlineData("""{ "en": { } }""", "translations.en")]
    [InlineData("""{ "en": { "name": " " } }""", "translations.en.name")]
    [InlineData("""{ "en": { "name": 3 } }""", "translations.en.name")]
    [InlineData("""{ "en": { "summary": "line\nbreak" } }""", "translations.en.summary")]
    public void Manifest_BadTranslations_AreInvalidFields(string json, string field)
    {
        var (manifest, issues) = Read(JsonNode.Parse(json));
        Assert.Null(manifest);
        Assert.Contains(issues, issue => issue.Code == AssetIssueCode.InvalidField && issue.Detail == field);
    }

    [Fact]
    public void Manifest_LengthAndLanguageLimits()
    {
        Assert.NotNull(Read(JsonNode.Parse($$"""{ "en": { "name": "{{new string('a', 100)}}", "summary": "{{new string('b', 200)}}" } }""")).Manifest);
        Assert.Null(Read(JsonNode.Parse($$"""{ "en": { "name": "{{new string('a', 101)}}" } }""")).Manifest);
        Assert.Null(Read(JsonNode.Parse($$"""{ "en": { "summary": "{{new string('b', 201)}}" } }""")).Manifest);

        var codes = new JsonObject();
        for (int i = 0; i < AssetTranslations.MaxLanguages; i++) codes[$"zz-x{i:D2}"] = new JsonObject { ["name"] = "n" };
        Assert.NotNull(Read(codes).Manifest);
        codes["en"] = new JsonObject { ["name"] = "n" };
        Assert.Null(Read(codes).Manifest);
    }

    [Fact]
    public void CatalogEntry_Translations_RoundTrip_AndPickTheUiLanguage()
    {
        var entry = new RemoteCatalogEntry
        {
            Id = "pixel-cat.neon", Version = "1.0.0", Name = "네온", Summary = "네온 효과", Tags = ["neon"], Author = new AssetAuthor("pixel-cat", "Pixel Cat"),
            License = "CC0-1.0", Kinds = new Dictionary<string, int> { [AssetKinds.Gradient] = 1 }, MinAppVersion = "1.0.0",
            Package = new RemoteFile("p.chickxel-asset", 10, new string('a', 64)), Published = new DateOnly(2026, 11, 1), Updated = new DateOnly(2026, 11, 1),
            Review = RemoteReview.Community,
            Translations = new Dictionary<string, AssetTranslation>(StringComparer.OrdinalIgnoreCase) { ["en"] = new("Neon", null), ["zh-Hans"] = new("霓虹", "霓虹效果") },
        };
        var read = RemoteCatalogFormat.ReadEntry(JsonNode.Parse(RemoteCatalogFormat.WriteEntry(entry).ToJsonString()), "p");
        Assert.Equal(entry.Translations["zh-Hans"], read.Translations["zh-Hans"]);
        Assert.False(RemoteCatalogFormat.WriteEntry(entry with { Translations = AssetTranslations.Empty }).ContainsKey("translations"));
        var bad = RemoteCatalogFormat.WriteEntry(entry);
        bad["translations"] = JsonNode.Parse("""{ "en": {} }""");
        Assert.Throws<RemoteAssetException>(() => RemoteCatalogFormat.ReadEntry(JsonNode.Parse(bad.ToJsonString()), "p"));

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
            Assert.Equal(("Neon", "네온 효과"), (read.DisplayName, read.DisplaySummary));
            CultureInfo.CurrentUICulture = new CultureInfo("zh-Hans-CN");
            Assert.Equal(("霓虹", "霓虹效果"), (read.DisplayName, read.DisplaySummary));
            CultureInfo.CurrentUICulture = new CultureInfo("ko-KR");
            Assert.Equal(("네온", "네온 효과"), (read.DisplayName, read.DisplaySummary));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
