using System.Globalization;
using System.Text;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scripting;

/// <summary>스크립트 API 이름의 종류(자동 완성 아이콘·글자 모양).</summary>
public enum ScriptApiKind
{
    /// <summary>값(숫자·참거짓·문자열).</summary>
    Value,
    /// <summary>객체(점을 찍으면 아래 이름이 나옴).</summary>
    Object,
    /// <summary>함수·메서드.</summary>
    Function,
    /// <summary>선언·설정 객체의 키.</summary>
    Key,
    /// <summary>정해진 문자열 값(설정 <c>type</c>, <c>area</c>, 병아리 ID).</summary>
    Literal,
    /// <summary>JavaScript 키워드(자동 완성에만 나옴).</summary>
    Keyword,
}

/// <summary>
/// 스크립트 API 이름 하나. <see cref="Scope"/>는 이 이름이 나오는 자리(<see cref="ScriptApiCatalog"/>의 상수), <see cref="Signature"/>는 쓰는 모양,
/// <see cref="Members"/>는 점을 찍었을 때 이어지는 자리(없으면 null). 설명은 지금 언어(<see cref="Summary"/>)나 영어(<see cref="EnglishSummary"/>).
/// </summary>
public sealed record ScriptApiMember(string Scope, string Name, ScriptApiKind Kind, string Signature, string? Members = null)
{
    private string Key => "Api_" + (Scope.Length == 0 ? "global" : Scope.Replace('.', '_')) + "_" + Name;

    /// <summary>지금 UI 언어의 한 줄 설명.</summary>
    public string Summary => ScriptApiStrings.GetResourceString(Key) ?? Signature;

    /// <summary>영어 설명(AI 도구 문서용 — 화면 언어와 상관없이 같음).</summary>
    public string EnglishSummary => ScriptApiStrings.ResourceManager.GetString(Key, CultureInfo.GetCultureInfo("en")) ?? Signature;
}

/// <summary>
/// 스크립트 효과가 쓸 수 있는 이름 표 한 벌(설계 4.2): 코드 편집기의 자동 완성·설명 풍선·매개변수 안내, 편집기 위 안내, AI(MCP) 문서가 같이 쓴다.
/// 실제 실행기(<see cref="ScriptSandbox"/>, <see cref="ScriptHostApi"/>, <see cref="ScriptDeclarationReader"/>)와 다르면 시험이 잡는다.
/// </summary>
public static class ScriptApiCatalog
{
    /// <summary>맨 위(전역 이름).</summary>
    public const string Global = "";
    /// <summary><c>effect({ … })</c>에 넘기는 선언 객체의 키.</summary>
    public const string Declaration = "effect";
    /// <summary><c>settings</c> 배열 항목의 키.</summary>
    public const string Setting = "setting";
    /// <summary>설정 항목 <c>type</c>의 값.</summary>
    public const string SettingType = "setting.type";
    /// <summary>선언 <c>area</c>의 값.</summary>
    public const string Area = "effect.area";
    /// <summary>선언 <c>easing</c>의 값.</summary>
    public const string Easing = "effect.easing";
    /// <summary>설정 항목 <c>widget</c>의 값.</summary>
    public const string Widget = "setting.widget";
    /// <summary>선언 <c>translations</c>의 언어 하나(<c>translations.en</c> 등)의 키.</summary>
    public const string Translation = "translation";
    public const string Image = "img";
    public const string Context = "ctx";
    public const string Source = "ctx.source";
    public const string Pivot = "ctx.pivot";
    public const string Center = "ctx.center";
    /// <summary><c>ctx.slice</c>(9-slice 테두리, 없으면 null).</summary>
    public const string Slice = "ctx.slice";
    /// <summary><c>ctx.renderedFrameSource</c>가 돌려주는 객체.</summary>
    public const string Rendered = "rendered";
    /// <summary>색 객체(<c>ctx.color</c>, <c>hsvToRgb</c>, 색 설정 <c>s.키</c>).</summary>
    public const string Color = "color";
    /// <summary><c>rgbToHsv</c>가 돌려주는 객체.</summary>
    public const string Hsv = "hsv";
    /// <summary><c>rgbToOklab</c>가 돌려주는 객체.</summary>
    public const string Oklab = "oklab";
    public const string MathScope = "Math";
    /// <summary><c>s</c>: 선언한 설정 키(원문에서 읽음 — 표에는 없음).</summary>
    public const string Settings = "s";

    private static ScriptApiMember M(string scope, string name, ScriptApiKind kind, string signature, string? members = null)
        => new(scope, name, kind, signature, members);

    public static IReadOnlyList<ScriptApiMember> All { get; } =
    [
        M(Global, "effect", ScriptApiKind.Function, "effect({ name, settings: [...], render(img, ctx, s) { ... } })", Declaration),
        M(Global, "Math", ScriptApiKind.Object, "Math", MathScope),
        M(Global, "lerp", ScriptApiKind.Function, "lerp(a, b, t)"),
        M(Global, "hsvToRgb", ScriptApiKind.Function, "hsvToRgb(h, s, v, a?) → { r, g, b, a }", Color),
        M(Global, "rgbToHsv", ScriptApiKind.Function, "rgbToHsv(r, g, b) → { h, s, v }", Hsv),
        M(Global, "rgbToOklab", ScriptApiKind.Function, "rgbToOklab(r, g, b) → { l, a, b }", Oklab),
        M(Global, "oklabToRgb", ScriptApiKind.Function, "oklabToRgb(l, a, b) → { r, g, b, a }", Color),

        M(Declaration, "name", ScriptApiKind.Key, "name: \"…\""),
        M(Declaration, "author", ScriptApiKind.Key, "author: \"…\""),
        M(Declaration, "language", ScriptApiKind.Key, "language: \"en\""),
        M(Declaration, "description", ScriptApiKind.Key, "description: \"…\""),
        M(Declaration, "tags", ScriptApiKind.Key, "tags: [\"neon\", \"glow\"]"),
        M(Declaration, "translations", ScriptApiKind.Key, "translations: { en: { name, description, labels: { key: \"…\", \"key.choiceValue\": \"…\" } } }", Translation),
        M(Declaration, "animated", ScriptApiKind.Key, "animated: true"),
        M(Declaration, "deferred", ScriptApiKind.Key, "deferred: true"),
        M(Declaration, "motion", ScriptApiKind.Key, "motion: false"),
        M(Declaration, "clipsBySource", ScriptApiKind.Key, "clipsBySource: true"),
        M(Declaration, "erasesSource", ScriptApiKind.Key, "erasesSource: true | \"boolSettingKey\""),
        M(Declaration, "easing", ScriptApiKind.Key, "easing: \"Linear\" | \"EaseIn\" | \"EaseOut\" | \"EaseInOut\"", Easing),
        M(Declaration, "area", ScriptApiKind.Key, "area: \"global\" | \"local\"", Area),
        M(Declaration, "radius", ScriptApiKind.Key, "radius: 0~16384"),
        M(Declaration, "previewChick", ScriptApiKind.Key, "previewChick: \"knight\""),
        M(Declaration, "previewFrames", ScriptApiKind.Key, "previewFrames: 2~20"),
        M(Declaration, "settings", ScriptApiKind.Key, "settings: [{ key, label, type, min, max, step, default, choices, alpha, widget, pair }]", Setting),
        M(Declaration, "padding", ScriptApiKind.Key, "padding: [left, top, right, bottom] | padding(s, width, height) { ... }"),
        M(Declaration, "render", ScriptApiKind.Function, "render(img, ctx, s) { ... }"),

        M(Translation, "name", ScriptApiKind.Key, "name: \"…\""),
        M(Translation, "description", ScriptApiKind.Key, "description: \"…\""),
        M(Translation, "labels", ScriptApiKind.Key, "labels: { key: \"…\", \"key.choiceValue\": \"…\" }"),

        M(Area, "global", ScriptApiKind.Literal, "\"global\""),
        M(Area, "local", ScriptApiKind.Literal, "\"local\""),

        M(Easing, "Linear", ScriptApiKind.Literal, "\"Linear\""),
        M(Easing, "EaseIn", ScriptApiKind.Literal, "\"EaseIn\""),
        M(Easing, "EaseOut", ScriptApiKind.Literal, "\"EaseOut\""),
        M(Easing, "EaseInOut", ScriptApiKind.Literal, "\"EaseInOut\""),

        M(Setting, "key", ScriptApiKind.Key, "key: \"strength\""),
        M(Setting, "label", ScriptApiKind.Key, "label: \"…\""),
        M(Setting, "type", ScriptApiKind.Key, "type: \"int\" | \"number\" | \"bool\" | \"color\" | \"choice\" | \"colorList\" | \"colorPairs\" | \"layer\"", SettingType),
        M(Setting, "min", ScriptApiKind.Key, "min: 0"),
        M(Setting, "max", ScriptApiKind.Key, "max: 100"),
        M(Setting, "step", ScriptApiKind.Key, "step: 0.1"),
        M(Setting, "default", ScriptApiKind.Key, "default: …"),
        M(Setting, "choices", ScriptApiKind.Key, "choices: [{ value: \"a\", label: \"…\" }]"),
        M(Setting, "alpha", ScriptApiKind.Key, "alpha: false"),
        M(Setting, "widget", ScriptApiKind.Key, "widget: \"angle\" | \"point\"", Widget),
        M(Setting, "pair", ScriptApiKind.Key, "pair: \"centerY\""),

        M(Widget, "angle", ScriptApiKind.Literal, "\"angle\""),
        M(Widget, "point", ScriptApiKind.Literal, "\"point\""),

        M(SettingType, "int", ScriptApiKind.Literal, "\"int\""),
        M(SettingType, "number", ScriptApiKind.Literal, "\"number\""),
        M(SettingType, "bool", ScriptApiKind.Literal, "\"bool\""),
        M(SettingType, "color", ScriptApiKind.Literal, "\"color\""),
        M(SettingType, "choice", ScriptApiKind.Literal, "\"choice\""),
        M(SettingType, "colorList", ScriptApiKind.Literal, "\"colorList\""),
        M(SettingType, "colorPairs", ScriptApiKind.Literal, "\"colorPairs\""),
        M(SettingType, "layer", ScriptApiKind.Literal, "\"layer\""),

        M(Image, "width", ScriptApiKind.Value, "img.width"),
        M(Image, "height", ScriptApiKind.Value, "img.height"),
        M(Image, "data", ScriptApiKind.Value, "img.data[(y * img.width + x) * 4 + c]"),
        M(Image, "blank", ScriptApiKind.Function, "img.blank(width, height) → img", Image),
        M(Image, "blendOver", ScriptApiKind.Function, "img.blendOver(src, x, y) → img", Image),

        M(Context, "t", ScriptApiKind.Value, "ctx.t"),
        M(Context, "phase", ScriptApiKind.Value, "ctx.phase"),
        M(Context, "progress", ScriptApiKind.Value, "ctx.progress"),
        M(Context, "frame", ScriptApiKind.Value, "ctx.frame"),
        M(Context, "step", ScriptApiKind.Value, "ctx.step"),
        M(Context, "span", ScriptApiKind.Value, "ctx.span"),
        M(Context, "source", ScriptApiKind.Object, "ctx.source", Source),
        M(Context, "pivot", ScriptApiKind.Object, "ctx.pivot", Pivot),
        M(Context, "center", ScriptApiKind.Object, "ctx.center", Center),
        M(Context, "slice", ScriptApiKind.Object, "ctx.slice → { left, top, right, bottom } | null", Slice),
        M(Context, "color", ScriptApiKind.Function, "ctx.color(key, x, y, bounds?) → { r, g, b, a }", Color),
        M(Context, "colorAt", ScriptApiKind.Function, "ctx.colorAt(key, index, x, y, bounds?) → { r, g, b, a }", Color),
        M(Context, "pairColor", ScriptApiKind.Function, "ctx.pairColor(key, index, \"from\" | \"to\", x, y, bounds?) → { r, g, b, a }", Color),
        M(Context, "hasGradient", ScriptApiKind.Function, "ctx.hasGradient(\"key\" | \"key[i]\" | \"key[i].from\") | ctx.hasGradient(listKey, index, \"from\" | \"to\"?) → true | false"),
        M(Context, "hash", ScriptApiKind.Function, "ctx.hash(x, y, seed, salt?) → 0~1"),
        M(Context, "random", ScriptApiKind.Function, "ctx.random(seed) → 0~1"),
        M(Context, "bayer2", ScriptApiKind.Function, "ctx.bayer2(x, y) → 0~3"),
        M(Context, "bayer4", ScriptApiKind.Function, "ctx.bayer4(x, y) → 0~15"),
        M(Context, "frameSource", ScriptApiKind.Function, "ctx.frameSource(frame) → img | null", Image),
        M(Context, "renderedFrameSource", ScriptApiKind.Function, "ctx.renderedFrameSource(frame) → { image, offsetX, offsetY } | null", Rendered),
        M(Context, "layer", ScriptApiKind.Function, "ctx.layer(key) → img | null", Image),
        M(Context, "layerWithEffects", ScriptApiKind.Function, "ctx.layerWithEffects(key) → img | null", Image),
        M(Context, "layerEffects", ScriptApiKind.Function, "ctx.layerEffects(key) → [effectName, ...]"),

        M(Source, "x", ScriptApiKind.Value, "ctx.source.x"),
        M(Source, "y", ScriptApiKind.Value, "ctx.source.y"),
        M(Source, "w", ScriptApiKind.Value, "ctx.source.w"),
        M(Source, "h", ScriptApiKind.Value, "ctx.source.h"),
        M(Pivot, "x", ScriptApiKind.Value, "ctx.pivot.x"),
        M(Pivot, "y", ScriptApiKind.Value, "ctx.pivot.y"),
        M(Center, "x", ScriptApiKind.Value, "ctx.center.x"),
        M(Center, "y", ScriptApiKind.Value, "ctx.center.y"),
        M(Slice, "left", ScriptApiKind.Value, "ctx.slice.left"),
        M(Slice, "top", ScriptApiKind.Value, "ctx.slice.top"),
        M(Slice, "right", ScriptApiKind.Value, "ctx.slice.right"),
        M(Slice, "bottom", ScriptApiKind.Value, "ctx.slice.bottom"),
        M(Rendered, "image", ScriptApiKind.Object, "image", Image),
        M(Rendered, "offsetX", ScriptApiKind.Value, "offsetX"),
        M(Rendered, "offsetY", ScriptApiKind.Value, "offsetY"),

        M(Color, "r", ScriptApiKind.Value, "r"),
        M(Color, "g", ScriptApiKind.Value, "g"),
        M(Color, "b", ScriptApiKind.Value, "b"),
        M(Color, "a", ScriptApiKind.Value, "a"),
        M(Hsv, "h", ScriptApiKind.Value, "h"),
        M(Hsv, "s", ScriptApiKind.Value, "s"),
        M(Hsv, "v", ScriptApiKind.Value, "v"),
        M(Oklab, "l", ScriptApiKind.Value, "l"),
        M(Oklab, "a", ScriptApiKind.Value, "a"),
        M(Oklab, "b", ScriptApiKind.Value, "b"),

        M(MathScope, "PI", ScriptApiKind.Value, "Math.PI"),
        M(MathScope, "E", ScriptApiKind.Value, "Math.E"),
        M(MathScope, "abs", ScriptApiKind.Function, "Math.abs(x)"),
        M(MathScope, "floor", ScriptApiKind.Function, "Math.floor(x)"),
        M(MathScope, "ceil", ScriptApiKind.Function, "Math.ceil(x)"),
        M(MathScope, "trunc", ScriptApiKind.Function, "Math.trunc(x)"),
        M(MathScope, "round", ScriptApiKind.Function, "Math.round(x)"),
        M(MathScope, "sign", ScriptApiKind.Function, "Math.sign(x)"),
        M(MathScope, "min", ScriptApiKind.Function, "Math.min(a, b, ...)"),
        M(MathScope, "max", ScriptApiKind.Function, "Math.max(a, b, ...)"),
        M(MathScope, "sqrt", ScriptApiKind.Function, "Math.sqrt(x)"),
        M(MathScope, "pow", ScriptApiKind.Function, "Math.pow(x, y)"),
        M(MathScope, "hypot", ScriptApiKind.Function, "Math.hypot(x, y)"),
        M(MathScope, "sin", ScriptApiKind.Function, "Math.sin(radians)"),
        M(MathScope, "cos", ScriptApiKind.Function, "Math.cos(radians)"),
        M(MathScope, "tan", ScriptApiKind.Function, "Math.tan(radians)"),
        M(MathScope, "atan2", ScriptApiKind.Function, "Math.atan2(y, x)"),
        M(MathScope, "exp", ScriptApiKind.Function, "Math.exp(x)"),
        M(MathScope, "log", ScriptApiKind.Function, "Math.log(x)"),
    ];

    /// <summary><paramref name="scope"/> 자리에 나오는 이름들(표 차례).</summary>
    public static IReadOnlyList<ScriptApiMember> In(string scope) => All.Where(member => member.Scope == scope).ToList();

    /// <summary><paramref name="scope"/>의 <paramref name="name"/>(없으면 null).</summary>
    public static ScriptApiMember? Find(string scope, string name) => All.FirstOrDefault(member => member.Scope == scope && member.Name == name);

    /// <summary>
    /// AI 도구 문서(영어 Markdown): 선언 형식, 자리별 이름·모양·설명, 제약(문자열 코드 실행·시간·난수 없음)과 실행 한도.
    /// 화면 언어와 상관없이 같은 글이다.
    /// </summary>
    public static string ToMarkdown(ScriptLimits? limits = null)
    {
        limits ??= ScriptLimits.Default;
        var md = new StringBuilder();
        md.AppendLine("# Chickxel script effect API");
        md.AppendLine();
        md.AppendLine("A script effect is one JavaScript file that calls `effect({...})` once. `render(img, ctx, s)` changes `img.data` (RGBA numbers, row by row) in place for one frame.");
        md.AppendLine("Values written to `img.data` are rounded and clamped to 0-255 like Uint8ClampedArray.");
        md.AppendLine();
        md.AppendLine("Rules: strict mode; no eval/Function, no Date, no Math.random (use ctx.hash/ctx.random), typed arrays only as Float64Array/Int32Array/Uint8Array, no file or network access.");
        md.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"Limits per call: {limits.MaxStatements:N0} statements, {limits.Timeout.TotalSeconds:0.#} s, {limits.MemoryBytes / (1024 * 1024)} MB of allocations, call depth {limits.MaxRecursion}; render() gets {limits.StatementsPerPixel:N0} more statements, {limits.TimePerPixel.TotalMicroseconds:0} µs and {limits.MemoryPerPixel / 1024} KB more per pixel of the (padded) image. Over a limit the effect is skipped for that frame."));
        md.AppendLine("Keep render() numeric (numbers, loops, Math, img.data indexing) for speed.");
        md.AppendLine();
        foreach (var group in All.GroupBy(member => member.Scope))
        {
            md.AppendLine(group.Key.Length == 0 ? "## Globals" : $"## {group.Key}");
            foreach (var member in group) md.AppendLine($"- `{member.Signature}` - {member.EnglishSummary}");
            md.AppendLine();
        }
        md.AppendLine("## Example");
        md.AppendLine("```js");
        // 공식 원문(목록 태그는 패키지가 따로 정함)에 AI가 늘 적어야 하는 tags를 넣어 보인다 — 공식 원문·해시는 그대로
        var example = OfficialEffects.Source("shine");
        md.Append((ScriptDeclarationEdit.SetTags(example, ["shine", "light", "sweep", "sparkle"]) ?? example).TrimEnd());
        md.AppendLine();
        md.AppendLine("```");
        return md.ToString();
    }
}
