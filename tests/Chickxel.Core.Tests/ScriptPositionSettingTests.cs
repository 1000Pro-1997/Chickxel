using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

public class ScriptPositionSettingTests
{
    // render가 ctx.position("pos")의 그림 좌표를 왼쪽 위 두 픽셀의 R·G에 적는다(빠른 경로·Jint 비교용)
    private const string Source = """
        effect({ name: "where", settings: [
          { key: "pos", label: "위치", type: "position", min: -10, max: 40, default: { anchor: "custom", x: 3, y: 5 }, group: "위치" },
          { key: "size", label: "크기", type: "int", min: 1, max: 9, default: 2, group: "위치" },
        ], render(img, ctx, s) {
          const p = ctx.position("pos");
          const d = img.data;
          d[0] = p.x; d[1] = p.y; d[2] = s.pos.anchor === "pivot" ? 1 : 0; d[3] = 255;
          d[4] = s.pos.x; d[5] = s.pos.y; d[7] = 255;
        } });
        """;

    private static string Unique(string source, [CallerMemberName] string name = "") => source + "\n// " + name;

    private static ScriptEffectProgram Parse(string source)
    {
        var result = ScriptEffectProgram.Parse(source);
        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => e.Message)));
        return result.Program!;
    }

    private static (Color32 First, Color32 Second) Render(ScriptEffectProgram program, ScriptPosition value, PointF? pivot, bool jint)
    {
        // 16×12 그림, 불투명 범위는 (8, 4)~(11, 9) — 그림 중심은 (10, 7)
        var image = new PixelImage(16, 12);
        for (int y = 4; y < 10; y++)
            for (int x = 8; x < 12; x++)
                image[x, y] = new Color32(9, 9, 9, 255);
        var values = program.NormalizeSettings(new Dictionary<string, object> { ["pos"] = value });
        var frame = ScriptFrameContext.Still with { Pivot = pivot };
        var source = new Rectangle(2, 1, 12, 10);
        if (jint) program.RenderOnJint(image, source, frame, values, CancellationToken.None);
        else program.Render(image, source, frame, values);
        return (image[0, 0], image[1, 0]);
    }

    [Fact]
    public void Position_resolves_each_anchor_the_same_on_the_fast_path_and_jint()
    {
        var program = Parse(Unique(Source));
        Assert.True(program.ExecutionPath.Compiled, program.ExecutionPath.Reason);
        Assert.Equal(new ScriptPosition("custom", 3, 5), program.Settings[0].Default);
        Assert.Equal(("위치", "위치"), (program.Settings[0].Group, program.Settings[1].Group));

        foreach (bool jint in new[] { false, true })
        {
            // 직접 지정: 원본 왼쪽 위 (2, 1) + (3, 5)
            Assert.Equal(new Color32(5, 6, 0, 255), Render(program, new("custom", 3, 5), null, jint).First);
            // 캔버스 중심: 원본 (2, 1, 12, 10)의 가운데
            Assert.Equal(new Color32(8, 6, 0, 255), Render(program, new("center", 3, 5), null, jint).First);
            // 피벗: 원본 기준 (4, 2) → 그림 (6, 3), anchor 비교도 같음
            Assert.Equal(new Color32(6, 3, 1, 255), Render(program, new("pivot", 3, 5), new PointF(4, 2), jint).First);
            // 그림 중심: 불투명 범위 (8, 4, 4, 6)의 가운데
            Assert.Equal(new Color32(10, 7, 0, 255), Render(program, new("content", 3, 5), null, jint).First);
            // s.pos는 저장 값 그대로(기준이 달라도 X·Y는 남음)
            Assert.Equal(new Color32(3, 5, 0, 255), Render(program, new("content", 3, 5), null, jint).Second);
        }
    }

    [Fact]
    public void Position_values_fill_missing_fields_clamp_and_round_trip_through_json()
    {
        var program = Parse(Unique(Source));
        ScriptPosition Normalize(object value) => (ScriptPosition)program.NormalizeSettings(new Dictionary<string, object> { ["pos"] = value })["pos"];
        // 빠진 칸은 선언의 기본값, 범위 밖은 자르고 소수는 반올림, 모르는 기준·모양이 틀린 값은 기본값
        Assert.Equal(new ScriptPosition("pivot", 3, 5), Normalize(JsonNode.Parse("""{ "anchor": "pivot" }""")!));
        Assert.Equal(new ScriptPosition("custom", 40, -10), Normalize(JsonNode.Parse("""{ "x": 99, "y": -99 }""")!));
        Assert.Equal(new ScriptPosition("custom", 2, 5), Normalize(JsonNode.Parse("""{ "x": 2.4 }""")!));
        Assert.Equal(new ScriptPosition("custom", 3, 5), Normalize(JsonNode.Parse("""{ "anchor": "middle" }""")!));
        Assert.Equal(new ScriptPosition("custom", 3, 5), Normalize("pivot"));

        var effect = program.CreateEffect(new Dictionary<string, object> { ["pos"] = new ScriptPosition("content", 7, 8) });
        var json = EffectCodec.Write(effect);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "anchor": "content", "x": 7, "y": 8 }"""), ((JsonObject)json[EffectCodec.SettingsKey]!)["pos"]));
        var parameter = EffectRegistry.ParametersOf(effect, _ => program).Single(p => p.Key == "pos");
        Assert.Equal(EffectParameterKind.Position, parameter.Kind);
        Assert.False(parameter.Exposed);

        // 효과 만들기 "기본값으로": 선언의 default를 객체 리터럴로
        var edited = ScriptDeclarationEdit.SetSettingDefault(program.Source, "pos", new ScriptPosition("pivot", -2, 9))!;
        Assert.Equal(new ScriptPosition("pivot", -2, 9), Parse(edited).Settings[0].Default);
    }

    [Theory]
    [InlineData("step: 1", "step")]
    [InlineData("choices: ['a']", "choices")]
    [InlineData("min: 0.5", "min")]
    [InlineData("min: 5, max: 1", "max")]
    [InlineData("default: 'pivot'", "default")]
    [InlineData("default: { anchor: 'middle' }", "default")]
    [InlineData("max: 4, default: { x: 5 }", "default")]
    [InlineData("default: { x: 1.5 }", "default")]
    public void Invalid_position_declarations_are_rejected(string fields, string field)
    {
        var result = ScriptEffectProgram.Parse($"effect({{ name: 'x', settings: [{{ key: 'pos', type: 'position', {fields} }}], render() {{}} }});");
        Assert.False(result.Success);
        Assert.Contains(result.Errors, error => error.Kind == ScriptErrorKind.Declaration && error.Message.Contains(field, StringComparison.Ordinal));
    }

    [Fact]
    public void Group_titles_are_translated_through_group_labels_and_must_be_text()
    {
        var program = Parse(Unique("""
            effect({ name: "g", language: "ko", settings: [{ key: "a", type: "bool", group: "  위치 " }],
              translations: { en: { labels: { "group:위치": "Position" } } }, render() {} });
            """));
        Assert.Equal("위치", program.Settings[0].Group);
        Assert.Equal("Position", program.GroupLabelFor("위치", System.Globalization.CultureInfo.GetCultureInfo("en")));
        Assert.Equal("위치", program.GroupLabelFor("위치", System.Globalization.CultureInfo.GetCultureInfo("ko")));

        var bad = ScriptEffectProgram.Parse("effect({ name: 'x', settings: [{ key: 'a', type: 'bool', group: 3 }], render() {} });");
        Assert.Contains(bad.Errors, error => error.Kind == ScriptErrorKind.Declaration && error.Message.Contains("group", StringComparison.Ordinal));
        // 번역 키는 선언한 묶음 제목만
        var unknown = ScriptEffectProgram.Parse("effect({ name: 'x', settings: [{ key: 'a', type: 'bool' }], translations: { en: { labels: { 'group:위치': 'P' } } }, render() {} });");
        Assert.Contains(unknown.Errors, error => error.Kind == ScriptErrorKind.Declaration);
    }
}
