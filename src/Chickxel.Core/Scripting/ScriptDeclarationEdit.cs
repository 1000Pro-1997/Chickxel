using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 원문의 <c>effect({ … })</c> 선언을 글자 그대로 고친다(효과 만들기에서 정보 칸·병아리·미리보기 칸 수를 바꾸거나 미리보기 설정 값을 기본값으로 쓸 때
/// — 나머지 원문은 손대지 않음).
/// </summary>
public static class ScriptDeclarationEdit
{
    private static readonly JsonSerializerOptions LiteralOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>원문에서 고칠 수 있는 선언(<c>effect({ … })</c> 객체)을 찾을 수 있는지. 문법 오류가 있으면 false(선언 값이 틀린 것은 상관없음).</summary>
    public static bool HasDeclaration(string source) => Declaration(source) is not null;

    /// <summary>선언의 <c>previewChick</c>을 <paramref name="chickId"/>로 바꾼다(<see cref="SetString"/>).</summary>
    public static string? SetPreviewChick(string source, string chickId) => SetString(source, "previewChick", chickId);

    /// <summary>
    /// 선언의 <c>previewFrames</c>를 정수 <paramref name="frames"/>로 바꾼다. 없으면 <c>previewChick</c> 다음에(없으면 <c>name</c> 다음에) 더한다
    /// (<see cref="SetNumber"/>와 같은 모양). 원문을 해석하지 못하면 null.
    /// </summary>
    public static string? SetPreviewFrames(string source, int frames)
        => SetLiteral(source, "previewFrames", NumberLiteral(frames), ["previewChick", "name"]);

    /// <summary>
    /// 선언의 <paramref name="key"/> 값을 문자열 리터럴 <paramref name="value"/>로 바꾼다. 없으면 <c>name</c> 다음에(<c>name</c>을 더할 때나 <c>name</c>이 없으면
    /// 선언 맨 앞에) 더한다 — 여러 줄 선언이면 같은 들여쓰기의 새 줄로. <paramref name="value"/>가 null이면 그 속성을 지운다(없으면 그대로).
    /// 원문을 해석하지 못하거나 선언 객체를 찾지 못하면 null.
    /// </summary>
    public static string? SetString(string source, string key, string? value)
        => SetLiteral(source, key, value is null ? null : JsonSerializer.Serialize(value, LiteralOptions), ["name"]);

    /// <summary>
    /// 선언의 <paramref name="key"/> 값을 숫자 리터럴(정수면 정수로, 아니면 불변 문화권의 짧은 꼴)로 바꾼다. 더하는 자리와 null(지움)은 <see cref="SetString"/>과 같다.
    /// 원문을 해석하지 못하거나 <paramref name="value"/>가 유한한 수가 아니면 null.
    /// </summary>
    public static string? SetNumber(string source, string key, double? value)
    {
        if (value is { } v && !double.IsFinite(v)) return null;
        return SetLiteral(source, key, value is { } n ? NumberLiteral(n) : null, ["name"]);
    }

    /// <summary>
    /// 선언의 <c>tags</c>를 문자열 배열 리터럴 <c>["a", "b"]</c>(<paramref name="tags"/> 순서 그대로, 값은 고치지 않음)로 바꾼다. 없으면 <c>description</c> 다음에
    /// (없으면 <c>name</c> 다음에, 그것도 없으면 선언 맨 앞에) 더한다. <paramref name="tags"/>가 비었으면 그 속성을 지운다(없으면 그대로).
    /// 원문을 해석하지 못하거나 선언 객체를 찾지 못하면 null(<see cref="SetString"/>과 같음).
    /// </summary>
    public static string? SetTags(string source, IReadOnlyList<string> tags)
        => SetLiteral(source, "tags", tags.Count == 0 ? null : "[" + string.Join(", ", tags.Select(t => JsonSerializer.Serialize(t, LiteralOptions))) + "]",
            ["description", "name"]);

    /// <summary>
    /// <c>settings</c> 목록에서 <c>key</c>가 <paramref name="key"/>인 항목의 <c>default</c> 값을 <paramref name="value"/>로 바꾼다
    /// (<c>default</c>가 없으면 그 항목의 마지막 속성 다음에 더함 — 여러 줄 항목이면 같은 들여쓰기의 새 줄로).
    /// 값은 설정 종류의 C# 값이다: <see cref="bool"/>은 <c>true</c>·<c>false</c>, 정수(<see cref="int"/>·<see cref="long"/>)는 정수, 다른 수는 불변 문화권의 짧은 꼴,
    /// <see cref="Color32"/>는 <c>"#rrggbb"</c>(알파가 255가 아니면 <c>"#rrggbbaa"</c> — 있던 값이 대문자 16진수면 대문자), 문자열(고르기 값)은 문자열.
    /// 있던 값이 작은따옴표 문자열이면 바꿀 문자열도 작은따옴표로 둔다(이스케이프가 필요 없을 때).
    /// 원문을 해석하지 못하거나, 그런 항목이 없거나, 값을 리터럴로 쓸 수 없으면 null.
    /// </summary>
    public static string? SetSettingDefault(string source, string key, object value)
    {
        if (Declaration(source) is not { } declaration
            || declaration.Properties.OfType<ObjectProperty>().FirstOrDefault(p => KeyName(p) == "settings")?.Value is not ArrayExpression settings)
            return null;
        var item = settings.Elements.OfType<ObjectExpression>().FirstOrDefault(o => o.Properties.OfType<ObjectProperty>()
            .Any(p => KeyName(p) == "key" && p.Value is StringLiteral literal && literal.Value == key));
        if (item is null) return null;
        var existing = item.Properties.OfType<ObjectProperty>().FirstOrDefault(p => KeyName(p) == "default");
        if (ValueLiteral(value, existing is null ? null : source[existing.Value.Start..existing.Value.End]) is not { } text) return null;
        if (existing is not null) return string.Concat(source.AsSpan(0, existing.Value.Start), text, source.AsSpan(existing.Value.End));
        return InsertAfter(source, item, item.Properties[^1], "default: " + text);
    }

    /// <summary>
    /// <see cref="SetSettingDefault"/>를 차례로 여러 키에 한다(효과 만들기의 "기본값으로"). 하나라도 못 하면 null.
    /// </summary>
    public static string? SetSettingDefaults(string source, IEnumerable<KeyValuePair<string, object>> values)
    {
        string? edited = source;
        foreach (var (key, value) in values)
        {
            edited = SetSettingDefault(edited, key, value);
            if (edited is null) return null;
        }
        return edited;
    }

    /// <summary>
    /// 선언의 <paramref name="key"/> 값을 리터럴 <paramref name="literal"/>로 바꾼다(null이면 지움). 없으면 <paramref name="after"/> 중 먼저 있는 키 다음에,
    /// 그런 키가 없으면 선언 맨 앞에 더한다.
    /// </summary>
    private static string? SetLiteral(string source, string key, string? literal, string[] after)
    {
        if (Declaration(source) is not { } declaration) return null;
        var properties = declaration.Properties.OfType<ObjectProperty>().ToList();
        var existing = properties.FirstOrDefault(p => KeyName(p) == key);
        if (literal is null) return existing is null ? source : Remove(source, existing);
        if (existing is not null) return string.Concat(source.AsSpan(0, existing.Value.Start), literal, source.AsSpan(existing.Value.End));

        string property = key + ": " + literal;
        if (after.Where(k => k != key).Select(k => properties.FirstOrDefault(p => KeyName(p) == k)).FirstOrDefault(p => p is not null) is { } anchor)
            return InsertAfter(source, declaration, anchor, property);

        // 선언 맨 앞: "{" 다음이 줄바꿈이면 첫 속성과 같은 들여쓰기의 새 줄로
        int open = declaration.Start + 1;
        if (properties.Count == 0) return source.Insert(open, " " + property + " ");
        var first = properties[0];
        int firstLine = source.LastIndexOf('\n', Math.Max(0, first.Start - 1)) + 1;
        if (firstLine > open && string.IsNullOrWhiteSpace(source[firstLine..first.Start]))
            return source.Insert(firstLine, source[firstLine..first.Start] + property + ",\n");
        return source.Insert(first.Start, property + ", ");
    }

    /// <summary>
    /// <paramref name="owner"/> 객체의 속성 <paramref name="anchor"/> 다음에 <paramref name="property"/>를 더한다: <paramref name="anchor"/>가 제 줄에 있으면
    /// 같은 들여쓰기의 새 줄로, 아니면 같은 줄에. 뒤 쉼표가 있으면 그 쉼표 다음에 넣고 새 속성 뒤에도 쉼표를 둔다.
    /// </summary>
    private static string InsertAfter(string source, ObjectExpression owner, Node anchor, string property)
    {
        int lineStart = source.LastIndexOf('\n', Math.Max(0, anchor.Start - 1)) + 1;
        bool ownLine = lineStart > owner.Start && string.IsNullOrWhiteSpace(source[lineStart..anchor.Start]);
        string indent = ownLine ? source[lineStart..anchor.Start] : "";
        int comma = SkipBlanks(source, anchor.End);
        if (comma < source.Length && source[comma] == ',')
            return source.Insert(comma + 1, ownLine ? "\n" + indent + property + "," : " " + property + ",");
        return source.Insert(anchor.End, ownLine ? ",\n" + indent + property : ", " + property);
    }

    private static string NumberLiteral(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>설정 값의 리터럴(<see cref="SetSettingDefault"/>). <paramref name="old"/>는 있던 값의 원문 글(따옴표·16진수 대소문자를 따름).</summary>
    private static string? ValueLiteral(object value, string? old)
    {
        switch (value)
        {
            case bool b:
                return b ? "true" : "false";
            case int or long or short or byte:
                return Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            case double or float or decimal:
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return double.IsFinite(number) ? NumberLiteral(number) : null;
            case Color32 color:
                return ColorLiteral(color, old);
            case string text:
                return StringLiteral(text, old);
            // 색 목록 ["#…", …]·색 쌍 목록 [{ from: "#…", to: "#…" }, …]: 따옴표·16진수 대소문자는 있던 값을 따른다
            case IReadOnlyList<Color32> colors:
                return "[" + string.Join(", ", colors.Select(c => ColorLiteral(c, old))) + "]";
            case IReadOnlyList<ScriptColorPair> pairs:
                return "[" + string.Join(", ", pairs.Select(p => "{ from: " + ColorLiteral(p.From, old) + ", to: " + ColorLiteral(p.To, old) + " }")) + "]";
            default:
                return null;
        }
    }

    private static string ColorLiteral(Color32 color, string? old)
    {
        bool upper = old is not null && old.Any(c => c is >= 'A' and <= 'F');
        string hex = color.A == 255 ? $"#{color.R:x2}{color.G:x2}{color.B:x2}" : $"#{color.R:x2}{color.G:x2}{color.B:x2}{color.A:x2}";
        // 목록의 있던 원문은 첫 글자가 [라 따옴표를 그 안의 첫 문자열에서 고른다
        string? quote = old is null ? null : old.TrimStart('[', '{', ' ').StartsWith('\'') ? "'" : "\"";
        return StringLiteral(upper ? hex.ToUpperInvariant() : hex, quote);
    }

    /// <summary>문자열 리터럴: 있던 값이 작은따옴표 문자열이고 이스케이프가 필요 없으면 작은따옴표로, 아니면 큰따옴표(JSON 꼴).</summary>
    private static string StringLiteral(string value, string? old)
        => old is ['\'', ..] && !value.Any(c => c is '\'' or '\\' or < ' ' or '\u2028' or '\u2029')
            ? "'" + value + "'"
            : JsonSerializer.Serialize(value, LiteralOptions);

    // ───────────── 번역(translations) ─────────────

    /// <summary>
    /// 선언 <c>translations</c>의 언어 하나를 바꾼다: <paramref name="translation"/>이 null이거나 비었으면 그 언어를 지우고(마지막 언어면 <c>translations</c>째로),
    /// 아니면 그 언어 항목의 값을 통째로 새로 쓰거나(언어 코드는 대소문자를 가리지 않고 찾음) 마지막 언어 다음에 더한다. <c>translations</c>가 없으면
    /// <c>description</c>(없으면 <c>name</c>) 다음에 만든다 — 여러 줄 선언이면 여러 줄로. 다른 언어와 나머지 원문은 그대로다.
    /// 원문을 해석하지 못하거나, <c>translations</c>가 객체 리터럴이 아니거나, 언어 코드가 틀리면 null.
    /// </summary>
    public static string? SetTranslation(string source, string language, ScriptTranslation? translation)
    {
        if (!ScriptTranslations.IsLanguageCode(language) || Declaration(source) is not { } declaration) return null;
        bool remove = translation is null || (translation.Name is null && translation.Description is null && translation.Labels.Count == 0);
        var property = declaration.Properties.OfType<ObjectProperty>().FirstOrDefault(p => KeyName(p) == "translations");
        if (property is null)
        {
            if (remove) return source;
            var anchor = declaration.Properties.OfType<ObjectProperty>().FirstOrDefault(p => KeyName(p) == "description")
                         ?? declaration.Properties.OfType<ObjectProperty>().FirstOrDefault(p => KeyName(p) == "name");
            string literal = ObjectLiteral(source, declaration, anchor ?? declaration.Properties.FirstOrDefault(), PropertyKey(language) + ": " + EntryLiteral(translation!));
            return SetLiteral(source, "translations", literal, ["description", "name"]);
        }
        if (property.Value is not ObjectExpression all) return null;
        var entries = all.Properties.OfType<ObjectProperty>().ToList();
        var existing = entries.FirstOrDefault(p => string.Equals(KeyName(p), language, StringComparison.OrdinalIgnoreCase));
        if (remove)
        {
            if (existing is null) return source;
            return entries.Count == 1 ? Remove(source, property) : Remove(source, existing);
        }
        string entry = EntryLiteral(translation!);
        if (existing is not null) return string.Concat(source.AsSpan(0, existing.Value.Start), entry, source.AsSpan(existing.Value.End));
        if (all.Properties.Count == 0)
            return string.Concat(source.AsSpan(0, all.Start), ObjectLiteral(source, declaration, property, PropertyKey(language) + ": " + entry), source.AsSpan(all.End));
        return InsertAfter(source, all, all.Properties[^1], PropertyKey(language) + ": " + entry);
    }

    /// <summary>
    /// 선언 <c>translations</c>를 <paramref name="translations"/>와 같게 한다: 없어진 언어는 지우고, 달라진 언어만 다시 쓰고, 새 언어는 차례대로 더한다
    /// (<see cref="SetTranslation"/>을 차례로 — 같은 언어는 글자 그대로 둠). 못 하면 null.
    /// </summary>
    public static string? SetTranslations(string source, IReadOnlyList<KeyValuePair<string, ScriptTranslation>> translations)
    {
        if (Declaration(source) is not { } declaration) return null;
        var current = new Dictionary<string, ScriptTranslation?>(StringComparer.OrdinalIgnoreCase);
        if (declaration.Properties.OfType<ObjectProperty>().FirstOrDefault(p => KeyName(p) == "translations") is { } property)
        {
            if (property.Value is not ObjectExpression all) return null;
            foreach (var entry in all.Properties.OfType<ObjectProperty>())
                if (KeyName(entry) is { } code) current[code] = ReadEntry(entry.Value);
        }
        string edited = source;
        foreach (var code in current.Keys.Where(code => !translations.Any(t => string.Equals(t.Key, code, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            if (SetTranslation(edited, code, null) is not { } removed) return null;
            edited = removed;
        }
        foreach (var (code, translation) in translations)
        {
            if (current.TryGetValue(code, out var old) && old is not null && old.SameAs(translation)) continue;
            if (SetTranslation(edited, code, translation) is not { } changed) return null;
            edited = changed;
        }
        return edited;
    }

    /// <summary>
    /// 선언 <c>translations</c>의 언어 <paramref name="language"/> 항목에서 <paramref name="field"/>(<c>"name"</c> 또는 <c>"description"</c>) 하나만 바꾼다
    /// — 그 항목의 다른 키(<c>labels</c>, 모르는 키)와 다른 언어는 글자 그대로 둔다. 있으면 값만 바꾸고, 없으면 항목의 마지막 속성 다음에 더하고(<c>description</c>은
    /// <c>name</c> 다음에), 항목이 없으면 <see cref="SetTranslation"/>처럼 새로 만든다. <paramref name="value"/>가 null이거나 공백뿐이면 그 키를 지우고,
    /// 항목이 비면 항목을(마지막 언어면 <c>translations</c>째로) 지운다. 값은 앞뒤 공백을 뗀다.
    /// 원문을 해석하지 못하거나, 언어 코드·키가 틀리거나, <c>translations</c>나 그 항목이 객체 리터럴이 아니면 null.
    /// </summary>
    public static string? SetTranslationField(string source, string language, string field, string? value)
    {
        if (field is not ("name" or "description") || !ScriptTranslations.IsLanguageCode(language) || Declaration(source) is not { } declaration) return null;
        string? text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var property = declaration.Properties.OfType<ObjectProperty>().FirstOrDefault(p => KeyName(p) == "translations");
        if (property is not null && property.Value is not ObjectExpression) return null;
        var existing = (property?.Value as ObjectExpression)?.Properties.OfType<ObjectProperty>()
            .FirstOrDefault(p => string.Equals(KeyName(p), language, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            if (text is null) return source;
            return SetTranslation(source, language, new ScriptTranslation(field == "name" ? text : null, field == "description" ? text : null,
                new Dictionary<string, string>(StringComparer.Ordinal)));
        }
        if (existing.Value is not ObjectExpression entry) return null;
        var keys = entry.Properties.OfType<ObjectProperty>().ToList();
        var current = keys.FirstOrDefault(p => KeyName(p) == field);
        if (text is null)
        {
            if (current is null) return source;
            // 이 키뿐이면 항목째(마지막 언어면 translations째로) 지운다
            return entry.Properties.Count == 1 ? SetTranslation(source, language, null) : Remove(source, current);
        }
        string literal = JsonSerializer.Serialize(text, LiteralOptions);
        if (current is not null) return string.Concat(source.AsSpan(0, current.Value.Start), literal, source.AsSpan(current.Value.End));
        if (entry.Properties.Count == 0) return string.Concat(source.AsSpan(0, entry.Start), "{ " + field + ": " + literal + " }", source.AsSpan(entry.End));
        var anchor = field == "description" ? keys.FirstOrDefault(p => KeyName(p) == "name") ?? entry.Properties[^1] : entry.Properties[^1];
        return InsertAfter(source, entry, anchor, field + ": " + literal);
    }

    /// <summary>번역 항목 리터럴 <c>{ name: "…", description: "…", labels: { 키: "…", "키.값": "…" } }</c>(빈 것은 뺌).</summary>
    private static string EntryLiteral(ScriptTranslation translation)
    {
        var parts = new List<string>();
        if (translation.Name is { } name) parts.Add("name: " + JsonSerializer.Serialize(name, LiteralOptions));
        if (translation.Description is { } description) parts.Add("description: " + JsonSerializer.Serialize(description, LiteralOptions));
        if (translation.Labels.Count > 0)
            parts.Add("labels: { " + string.Join(", ", translation.Labels.Select(pair => PropertyKey(pair.Key) + ": " + JsonSerializer.Serialize(pair.Value, LiteralOptions))) + " }");
        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>
    /// 속성 하나짜리 객체 리터럴. <paramref name="anchor"/>(옆에 둘 속성)가 제 줄에 있으면 여러 줄(그 들여쓰기 + 한 단계), 아니면 한 줄.
    /// </summary>
    private static string ObjectLiteral(string source, ObjectExpression owner, Node? anchor, string property)
    {
        if (anchor is null) return "{ " + property + " }";
        int lineStart = source.LastIndexOf('\n', Math.Max(0, anchor.Start - 1)) + 1;
        if (lineStart <= owner.Start || !string.IsNullOrWhiteSpace(source[lineStart..anchor.Start])) return "{ " + property + " }";
        string indent = source[lineStart..anchor.Start];
        string step = indent.StartsWith('\t') ? "\t" : "  ";
        return "{\n" + indent + step + property + ",\n" + indent + "}";
    }

    /// <summary>객체 키: 식별자 모양이면 그대로, 아니면 문자열.</summary>
    private static string PropertyKey(string key)
        => key.Length > 0 && (char.IsAsciiLetter(key[0]) || key[0] is '_' or '$') && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$')
            ? key
            : JsonSerializer.Serialize(key, LiteralOptions);

    /// <summary>글자 그대로 적은 번역 항목(문자열 리터럴만)을 읽는다. 다른 모양이면 null(그 언어는 다시 씀).</summary>
    private static ScriptTranslation? ReadEntry(Node value)
    {
        if (value is not ObjectExpression entry) return null;
        string? name = null, description = null;
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in entry.Properties)
        {
            if (node is not ObjectProperty property) return null;
            switch (KeyName(property), property.Value)
            {
                case ("name", StringLiteral literal):
                    name = literal.Value;
                    break;
                case ("description", StringLiteral literal):
                    description = literal.Value;
                    break;
                case ("labels", ObjectExpression labelObject):
                    foreach (var labelNode in labelObject.Properties)
                    {
                        if (labelNode is not ObjectProperty label || KeyName(label) is not { } key || label.Value is not StringLiteral text) return null;
                        labels[key] = text.Value;
                    }
                    break;
                default:
                    return null;
            }
        }
        return new ScriptTranslation(name, description, labels);
    }

    /// <summary>속성 하나를 지운다: 뒤 쉼표까지, 그 줄에 다른 것이 없으면 줄째로.</summary>
    private static string Remove(string source, ObjectProperty property)
    {
        int start = property.Start, end = SkipBlanks(source, property.End);
        if (end < source.Length && source[end] == ',') end++;
        int lineStart = source.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        int after = SkipBlanks(source, end);
        if (string.IsNullOrWhiteSpace(source[lineStart..start]) && (after >= source.Length || source[after] is '\n' or '\r'))
        {
            if (after < source.Length && source[after] == '\r') after++;
            if (after < source.Length && source[after] == '\n') after++;
            return source.Remove(lineStart, after - lineStart);
        }
        return source.Remove(start, SkipBlanks(source, end) - start);
    }

    private static int SkipBlanks(string source, int i)
    {
        while (i < source.Length && source[i] is ' ' or '\t') i++;
        return i;
    }

    private static ObjectExpression? Declaration(string source)
    {
        Script script;
        try
        {
            script = new Parser().ParseScript(source, "effect.js", strict: true);
        }
        catch (ParseErrorException)
        {
            return null;
        }
        foreach (var statement in script.Body)
        {
            if (statement is ExpressionStatement { Expression: CallExpression { Callee: Identifier { Name: "effect" } } call }
                && call.Arguments.Count == 1 && call.Arguments[0] is ObjectExpression declaration)
                return declaration;
        }
        return null;
    }

    private static string? KeyName(ObjectProperty property) => property switch
    {
        { Computed: false, Key: Identifier id } => id.Name,
        { Computed: false, Key: StringLiteral literal } => literal.Value,
        _ => null,
    };
}
