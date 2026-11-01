using System.Globalization;
using Chickxel.Core.IO;
using Chickxel.Core.Localization;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>선언 <c>language</c>: 읽기·검사, 화면 언어가 그 언어면 선언 값이 번역보다 먼저, 정보 칸이 고칠 언어, 번역 한 칸 고치기, 새 스크립트 틀.</summary>
public class ScriptLanguageTests
{
    private const string Source = """
        effect({
          name: "Shine",
          language: "en",
          description: "A light sweeps across.",
          translations: {
            en: { name: "Old English name" },
            ko: { name: "반짝임", description: "빛이 지나갑니다.", labels: { power: "세기" } },
            "zh-Hans": { name: "闪光" },
          },
          settings: [{ key: "power", label: "Power", type: "int", min: 0, max: 10, default: 5 }],
          render(img, ctx, s) {},
        });
        """;

    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    [Fact]
    public void Language_is_read_and_the_declared_text_wins_in_that_language()
    {
        var program = ScriptEffectProgram.Parse(Source).Program!;
        Assert.Equal("en", program.Language);
        // 화면 언어가 선언 언어(또는 그 아래 언어)면 같은 언어의 번역이 있어도 선언 값
        Assert.Equal("Shine", program.NameFor(C("en")));
        Assert.Equal("Shine", program.NameFor(C("en-GB")));
        Assert.Equal("Power", program.LabelFor(program.Settings[0], C("en-US")));
        // 다른 언어는 그대로 번역 → 선언 값
        Assert.Equal(("반짝임", "빛이 지나갑니다.", "세기"),
            (program.NameFor(C("ko")), program.DescriptionFor(C("ko-KR")), program.LabelFor(program.Settings[0], C("ko"))));
        Assert.Equal("闪光", program.NameFor(C("zh-CN")));
        Assert.Equal("Shine", program.NameFor(C("ja")));

        var asset = ScriptAsset.From("", program);
        Assert.Equal(("en", "Shine", "반짝임", "세기"), (asset.Language, asset.NameFor(C("en")), asset.NameFor(C("ko")), asset.LabelFor(program.Settings[0], C("ko"))));

        // language가 없으면 예전처럼 번역이 먼저
        var plain = ScriptEffectProgram.Parse(Source.Replace("  language: \"en\",", "", StringComparison.Ordinal)).Program!;
        Assert.Null(plain.Language);
        Assert.Null(ScriptAsset.From("", plain).Language);
        Assert.Equal("Old English name", plain.NameFor(C("en")));
    }

    [Fact]
    public void Find_prefers_a_more_specific_translation_before_reaching_the_base_language()
    {
        var translations = new Dictionary<string, ScriptTranslation>(StringComparer.OrdinalIgnoreCase)
        {
            ["en-GB"] = new("Colour", null, new Dictionary<string, string>()),
            ["en"] = new("Color", null, new Dictionary<string, string>()),
        };
        Assert.Equal("Colour", ScriptTranslations.NameFor(translations, "Base", C("en-GB"), "en"));
        Assert.Equal("Base", ScriptTranslations.NameFor(translations, "Base", C("en-US"), "en"));
        Assert.Equal("Base", ScriptTranslations.NameFor(translations, "Base", C("en"), "EN"));
        Assert.Equal("Color", ScriptTranslations.NameFor(translations, "Base", C("en-US"), "ko"));
        Assert.Equal("Color", ScriptTranslations.NameFor(translations, "Base", C("en-US")));
    }

    [Theory]
    [InlineData("language: 3,")]
    [InlineData("language: '',")]
    [InlineData("language: 'english',")]
    [InlineData("language: 'en_US',")]
    [InlineData("language: ['en'],")]
    public void Bad_language_is_a_declaration_error_at_its_line(string language)
    {
        var parsed = ScriptEffectProgram.Parse("effect({ name: 'x',\n" + language + "\nrender() {} });");
        Assert.False(parsed.Success);
        var error = Assert.Single(parsed.Errors);
        Assert.Equal((ScriptErrorKind.Declaration, 2, DomainStrings.Script_InvalidLanguage), (error.Kind, error.Line, error.Message));
    }

    [Theory]
    [InlineData(null, "en", null)]
    [InlineData("en", "en", null)]
    [InlineData("en", "en-US", null)]
    [InlineData("EN", "en-GB", null)]
    [InlineData("ko", "en-US", "en")]
    [InlineData("ko", "en", "en")]
    [InlineData("en", "ko-KR", "ko")]
    [InlineData("en", "zh-CN", "zh-Hans")]
    [InlineData("en", "zh-TW", "zh-Hant")]
    [InlineData("zh-Hans", "zh-CN", null)]
    [InlineData("en", "pt-BR", "pt")]
    [InlineData("en", "", null)]
    public void EditLanguage_is_null_for_the_declared_fields_else_the_translation_key(string? baseLanguage, string ui, string? expected)
    {
        Assert.Equal(expected, ScriptTranslations.EditLanguage(baseLanguage, ScriptTranslations.Empty, C(ui)));
    }

    /// <summary>
    /// language가 없는 예전 원문(Teleport처럼 한국어 선언 + translations.en): 화면 언어의 번역이 있으면 그 번역을 고치고(적힌 키 그대로),
    /// 없으면 선언 값을 고친다.
    /// </summary>
    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-US", "en")]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("ja-JP", null)]
    [InlineData("ko-KR", null)]
    [InlineData("", null)]
    public void EditLanguage_without_language_edits_an_existing_ui_translation_else_the_declared_fields(string ui, string? expected)
    {
        var translations = new Dictionary<string, ScriptTranslation>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = new("Teleport", "Vanishes and reappears.", new Dictionary<string, string>()),
            ["zh-Hans"] = new("传送", null, new Dictionary<string, string>()),
        };
        Assert.Equal(expected, ScriptTranslations.EditLanguage(null, translations, C(ui)));
    }

    [Fact]
    public void EditLanguage_for_a_parsed_legacy_script_in_english_is_the_en_translation()
    {
        var program = ScriptEffectProgram.Parse("""
            effect({
              name: "순간이동",
              description: "사라졌다가 다른 곳에 나타납니다.",
              translations: { en: { name: "Teleport", description: "Vanishes and reappears elsewhere." } },
              render(img) {},
            });
            """).Program!;
        Assert.Null(program.Language);
        Assert.Equal("en", ScriptTranslations.EditLanguage(program.Language, program.Translations, C("en-US")));
        Assert.Null(ScriptTranslations.EditLanguage(program.Language, program.Translations, C("ja-JP")));
        Assert.Equal("ko", ScriptTranslations.GuessLanguage(program.Name, program.Description));
    }

    [Theory]
    [InlineData("순간이동", null, "ko")]
    [InlineData("Teleport 텔레포트", null, "ko")]
    [InlineData("テレポート", "瞬間移動します", "ja")]
    [InlineData("瞬间移动", null, "zh-Hans")]
    [InlineData("Телепорт", null, "ru")]
    [InlineData("เทเลพอร์ต", null, "th")]
    [InlineData("Teleport", "Vanishes.", "en")]
    [InlineData("Téléportation", null, "en")]
    [InlineData("123 ...", "", null)]
    public void GuessLanguage_reads_the_script_of_the_declared_text(string name, string? description, string? expected)
        => Assert.Equal(expected, ScriptTranslations.GuessLanguage(name, description));

    [Fact]
    public void SetLanguage_adds_after_name_replaces_and_rejects_bad_codes()
    {
        const string legacy = """
            effect({
              name: "순간이동",
              description: "사라집니다.",
              render(img) {},
            });
            """;
        string added = ScriptDeclarationEdit.SetLanguage(legacy, "ko")!;
        Assert.Contains("name: \"순간이동\",\n  language: \"ko\",", added.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Equal("ko", ScriptEffectProgram.Parse(added).Program!.Language);
        string replaced = ScriptDeclarationEdit.SetLanguage(added, "en")!;
        Assert.Equal("en", ScriptEffectProgram.Parse(replaced).Program!.Language);
        Assert.Equal(2, replaced.Split("language:").Length); // 키는 하나
        Assert.Null(ScriptDeclarationEdit.SetLanguage(legacy, "not a code"));
    }

    [Fact]
    public void SetTranslationField_changes_one_field_and_keeps_the_rest()
    {
        const string source = "effect({\n  name: \"x\",\n  translations: {\n    ko: { name: \"엑스\", labels: { power: \"세기\" }, note: 1 },\n  },\n"
                              + "  settings: [{ key: \"power\", label: \"Power\", type: \"int\", min: 0, max: 10, default: 5 }],\n  render() {},\n});\n";
        // 있던 값 바꾸기: labels·모르는 키는 그대로
        var renamed = ScriptDeclarationEdit.SetTranslationField(source, "KO", "name", "  새 이름 ")!;
        Assert.Equal(source.Replace("\"엑스\"", "\"새 이름\"", StringComparison.Ordinal), renamed);
        // 없는 키 더하기(description은 name 다음)
        var described = ScriptDeclarationEdit.SetTranslationField(renamed, "ko", "description", "설명")!;
        Assert.Equal(source.Replace("name: \"엑스\",", "name: \"새 이름\", description: \"설명\",", StringComparison.Ordinal), described);
        var program = ScriptEffectProgram.Parse(described).Program!;
        Assert.Equal(("새 이름", "설명", "세기"), (program.Translations["ko"].Name, program.Translations["ko"].Description, program.Translations["ko"].Labels["power"]));
        // 지우기
        Assert.Equal(renamed, ScriptDeclarationEdit.SetTranslationField(described, "ko", "description", " "));
        Assert.Equal(renamed, ScriptDeclarationEdit.SetTranslationField(renamed, "ko", "description", null));
        // 새 언어는 항목째 더하고, 그 키만 있던 항목은 지우면 항목째(마지막 언어면 translations째로) 없어진다
        var english = ScriptDeclarationEdit.SetTranslationField("effect({ name: 'x', render() {} });", "en", "description", "Desc")!;
        Assert.Equal("Desc", ScriptEffectProgram.Parse(english).Program!.Translations["en"].Description);
        Assert.Equal("effect({ name: 'x', render() {} });", ScriptDeclarationEdit.SetTranslationField(english, "en", "description", ""));
        Assert.Equal("effect({ name: 'x', render() {} });", ScriptDeclarationEdit.SetTranslationField("effect({ name: 'x', render() {} });", "en", "name", null));
        // 못 하는 경우
        Assert.Null(ScriptDeclarationEdit.SetTranslationField(source, "ko", "labels", "x"));
        Assert.Null(ScriptDeclarationEdit.SetTranslationField(source, "korean", "name", "x"));
        Assert.Null(ScriptDeclarationEdit.SetTranslationField("effect({ name: 'x', translations: T, render() {} });", "ko", "name", "x"));
        Assert.Null(ScriptDeclarationEdit.SetTranslationField("effect({ name: 'x', render( {} });", "ko", "name", "x"));
    }

    [Theory]
    [InlineData("ko", "ko", "새 효과", "세기")]
    [InlineData("en", "en", "New effect", "Strength")]
    public void Starter_is_written_in_the_ui_language_and_declares_it(string ui, string language, string name, string label)
    {
        var program = ScriptEffectProgram.Parse(ScriptTemplates.StarterFor(C(ui))).Program!;
        Assert.Equal((language, name, label), (program.Language, program.Name, program.Settings[0].Label));
        // ja는 앱 문구가 없어 한국어(중립) 문구이므로 language도 ko
        if (ui == "ko") Assert.Equal("ko", ScriptEffectProgram.Parse(ScriptTemplates.StarterFor(C("ja"))).Program!.Language);
    }

    [Fact]
    public void Official_effects_declare_korean_as_their_language()
    {
        Assert.All(OfficialEffects.Ids, id => Assert.Equal("ko", OfficialEffects.Program(id).Language));
    }
}
