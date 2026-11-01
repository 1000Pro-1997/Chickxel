using System.Globalization;
using Chickxel.Core.IO;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>선언 <c>translations</c>: 읽기·검사, 화면 언어에 맞춘 이름 고르기, 원문 고치기.</summary>
public class ScriptTranslationTests
{
    private const string Source = """
        effect({
          name: "반짝임",
          description: "빛이 지나갑니다.",
          translations: {
            en: { name: "Shine", description: "A light sweeps across.", labels: { power: "Power", "mode.soft": "Soft" } },
            "zh-Hans": { name: "闪光" },
          },
          settings: [
            { key: "power", label: "세기", type: "int", min: 0, max: 10, default: 5 },
            { key: "mode", label: "방식", type: "choice", choices: [{ value: "soft", label: "부드럽게" }, { value: "hard", label: "강하게" }] },
          ],
          render(img, ctx, s) {},
        });
        """;

    private static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    [Fact]
    public void Names_and_labels_follow_the_language_then_its_parent_then_the_declaration()
    {
        var program = ScriptEffectProgram.Parse(Source).Program!;
        Assert.Equal(["en", "zh-Hans"], program.Translations.Keys);
        Assert.Equal("Shine", program.NameFor(C("en")));
        Assert.Equal("Shine", program.NameFor(C("en-US")));
        Assert.Equal("A light sweeps across.", program.DescriptionFor(C("en-GB")));
        // zh-CN의 위 언어는 zh-Hans: 이름은 번역, 설명은 번역이 없어 선언 값
        Assert.Equal("闪光", program.NameFor(C("zh-CN")));
        Assert.Equal("빛이 지나갑니다.", program.DescriptionFor(C("zh-CN")));
        Assert.Equal("반짝임", program.NameFor(C("ko")));
        Assert.Equal("반짝임", program.NameFor(C("ja")));
        Assert.Equal("반짝임", program.Name);

        var power = program.Settings[0];
        var mode = program.Settings[1];
        Assert.Equal(("Power", "세기"), (program.LabelFor(power, C("en")), program.LabelFor(power, C("ko"))));
        Assert.Equal(("Soft", "강하게", "방식"),
            (program.ChoiceLabelFor(mode, mode.Choices[0], C("en")), program.ChoiceLabelFor(mode, mode.Choices[1], C("en")), program.LabelFor(mode, C("en"))));

        // 에셋도 같은 번역(정체는 선언 이름 그대로)
        var asset = ScriptAsset.From("", program);
        Assert.Equal(("반짝임", "Shine", "A light sweeps across."), (asset.Name, asset.NameFor(C("en")), asset.DescriptionFor(C("en"))));
    }

    [Theory]
    [InlineData("translations: 3,")]
    [InlineData("translations: [],")]
    [InlineData("translations: { english: { name: 'x' } },")]
    [InlineData("translations: { en: { name: 'a' }, EN: { name: 'b' } },")]
    [InlineData("translations: { en: 'x' },")]
    [InlineData("translations: { en: { name: '' } },")]
    [InlineData("translations: { en: { name: 3 } },")]
    [InlineData("translations: { en: { labels: { nope: 'x' } } },")]
    [InlineData("translations: { en: { labels: { 'k.zzz': 'x' } } },")]
    [InlineData("translations: { en: { labels: { k: 1 } } },")]
    [InlineData("translations: { en: { labels: 'k' } },")]
    public void Bad_translations_are_declaration_errors_at_their_line(string translations)
    {
        var parsed = ScriptEffectProgram.Parse("effect({ name: 'x',\n" + translations +
            "\nsettings: [{ key: 'k', label: 'K', type: 'choice', choices: ['a', 'b'] }], render() {} });");
        Assert.False(parsed.Success);
        Assert.Contains(parsed.Errors, e => e.Kind == ScriptErrorKind.Declaration && e.Line == 2);
    }

    [Fact]
    public void Long_texts_are_rejected_and_unknown_keys_are_ignored()
    {
        string longName = new('a', Assets.AssetManifest.MaxNameLength + 1);
        Assert.False(ScriptEffectProgram.Parse($"effect({{ name: 'x', translations: {{ en: {{ name: '{longName}' }} }}, render() {{}} }});").Success);
        string longDescription = new('a', ScriptEffectProgram.MaxDescriptionLength + 1);
        Assert.False(ScriptEffectProgram.Parse($"effect({{ name: 'x', translations: {{ en: {{ description: '{longDescription}' }} }}, render() {{}} }});").Success);
        // 예전 앱처럼 모르는 선언 키와 번역 안의 모르는 키는 무시한다(다음 버전 원문을 읽을 수 있게)
        Assert.True(ScriptEffectProgram.Parse("effect({ name: 'x', futureKey: { a: 1 }, translations: { en: { name: 'X', note: 'n' } }, render() {} });").Success);
    }

    [Fact]
    public void Templates_with_translations_stay_on_the_fast_path()
    {
        foreach (var template in new[] { ScriptTemplates.Starter, OfficialEffects.Source("shine") })
        {
            Assert.True(ScriptEffectProgram.Parse(template).Program!.ExecutionPath.Compiled);
            var edited = ScriptDeclarationEdit.SetTranslation(template, "en", new ScriptTranslation("English name", "English description",
                new Dictionary<string, string>()))!;
            var program = ScriptEffectProgram.Parse(edited).Program!;
            Assert.Equal("English name", program.NameFor(C("en")));
            Assert.True(program.ExecutionPath.Compiled, program.ExecutionPath.Reason);
        }
    }

    [Fact]
    public void Translation_edits_add_replace_and_remove_entries_in_place()
    {
        var labels = new Dictionary<string, string>();
        // 없으면 description 다음에 여러 줄로 만든다
        const string multi = "effect({\n  name: 'x',\n  description: 'd',\n  render() {},\n});";
        var added = ScriptDeclarationEdit.SetTranslation(multi, "en", new ScriptTranslation("X", null, labels))!;
        Assert.Equal("effect({\n  name: 'x',\n  description: 'd',\n  translations: {\n    en: { name: \"X\" },\n  },\n  render() {},\n});", added);
        // 한 줄 선언이면 한 줄로, description이 없으면 name 다음에
        Assert.Equal("effect({ name: 'x', translations: { en: { name: \"X\" } }, render() {} });",
            ScriptDeclarationEdit.SetTranslation("effect({ name: 'x', render() {} });", "en", new ScriptTranslation("X", null, labels)));

        // 둘째 언어는 마지막 언어 다음 줄에(키가 식별자가 아니면 문자열)
        var two = ScriptDeclarationEdit.SetTranslation(added, "zh-Hans", new ScriptTranslation("甲", "乙", new Dictionary<string, string> { ["mode.a"] = "丙" }))!;
        Assert.Equal(added.Replace("    en: { name: \"X\" },\n", "    en: { name: \"X\" },\n    \"zh-Hans\": { name: \"甲\", description: \"乙\", labels: { \"mode.a\": \"丙\" } },\n"), two);
        // 같은 언어(대소문자 무시)는 그 값만 바뀐다
        var replaced = ScriptDeclarationEdit.SetTranslation(two, "EN", new ScriptTranslation("Y", null, labels))!;
        Assert.Equal(two.Replace("en: { name: \"X\" }", "en: { name: \"Y\" }"), replaced);
        // 지우면 그 줄이 빠지고, 마지막 언어를 지우면 translations째로
        Assert.Equal(added.Replace("\"X\"", "\"Y\""), ScriptDeclarationEdit.SetTranslation(replaced, "zh-Hans", null));
        Assert.Equal(multi, ScriptDeclarationEdit.SetTranslation(added, "en", null));
        Assert.Equal(multi, ScriptDeclarationEdit.SetTranslation(multi, "en", new ScriptTranslation(null, null, labels)));

        // 통째로 맞추기: 같은 언어는 글자 그대로, 빠진 언어는 지우고 새 언어는 더한다
        var synced = ScriptDeclarationEdit.SetTranslations(two, [
            new("en", new ScriptTranslation("X", null, labels)),
            new("ja", new ScriptTranslation("エックス", null, labels)),
        ])!;
        Assert.Equal(added.Replace("    en: { name: \"X\" },\n", "    en: { name: \"X\" },\n    ja: { name: \"エックス\" },\n"), synced);
        var parsed = ScriptEffectProgram.Parse(synced).Program!;
        Assert.Equal(["en", "ja"], parsed.Translations.Keys);

        Assert.Null(ScriptDeclarationEdit.SetTranslation(multi, "not a code", new ScriptTranslation("X", null, labels)));
        Assert.Null(ScriptDeclarationEdit.SetTranslation("effect({ name: 'x', translations: T, render() {} });", "en", new ScriptTranslation("X", null, labels)));
        Assert.Null(ScriptDeclarationEdit.SetTranslation("effect({ name: 'x', render( {} });", "en", new ScriptTranslation("X", null, labels)));
    }
}
