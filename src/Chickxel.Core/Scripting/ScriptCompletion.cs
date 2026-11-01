using System.Globalization;
using System.Text.RegularExpressions;
using Chickxel.Core.Assets;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Scripting;

/// <summary>자동 완성 후보 하나: 넣을 글, 종류, 모양(시그니처·설정 종류), 설명.</summary>
public sealed record ScriptCompletionItem(string Name, ScriptApiKind Kind, string Detail, string Summary);

/// <summary>자동 완성 결과: <see cref="Start"/>부터 커서까지를 고른 후보로 바꾼다. 후보가 없으면 빈 목록.</summary>
public sealed record ScriptCompletionResult(int Start, IReadOnlyList<ScriptCompletionItem> Items);

/// <summary>함수를 쓰는 중의 매개변수 안내: 부르는 함수, 지금 몇 번째 인자인지(0부터), 여는 괄호 자리.</summary>
public sealed record ScriptSignatureHelp(ScriptApiMember Member, int Parameter, int OpenParen);

/// <summary>
/// 스크립트 코드 편집기의 자동 완성·매개변수 안내·설명 풍선(설계 4.3). 쓰는 중이라 문법이 깨져 있어도 동작하도록 해석기 대신
/// 글자(문자열·주석을 뺀 코드)를 거꾸로 훑어 문맥을 정한다: <c>img.</c>·<c>ctx.</c>·<c>ctx.source.</c>·<c>Math.</c> 뒤 = 그 객체의 이름,
/// <c>s.</c> 뒤 = 선언의 <c>settings</c>에서 읽은 키, 선언 객체·설정 항목의 키 자리 = 그 키, <c>type: "</c>·<c>area: "</c>·<c>previewChick: "</c>·
/// <c>ctx.color("</c> 안 = 정해진 값, 그 밖 = 전역·이 원문의 이름·키워드. 이름은 <see cref="ScriptApiCatalog"/> 한 벌을 쓴다.
/// </summary>
public static partial class ScriptCompletion
{
    private static readonly string[] Keywords =
        ["const", "let", "function", "return", "if", "else", "for", "while", "do", "break", "continue", "true", "false", "null", "undefined", "typeof"];

    [GeneratedRegex(@"render\s*(?::\s*function\s*)?\(\s*([A-Za-z_$][\w$]*)?\s*(?:,\s*([A-Za-z_$][\w$]*))?\s*(?:,\s*([A-Za-z_$][\w$]*))?")]
    private static partial Regex RenderParams();

    [GeneratedRegex(@"\b(?:const|let|var|function)\s+([A-Za-z_$][\w$]*)")]
    private static partial Regex Declared();

    [GeneratedRegex(@"\b(?:const|let|var)\s+([A-Za-z_$][\w$]*)\s*=\s*([A-Za-z_$][\w$]*(?:\.[A-Za-z_$][\w$]*)*)\s*(\()?")]
    private static partial Regex Assigned();

    [GeneratedRegex(@"\{[^{}]*?\bkey\s*:\s*[""']([A-Za-z_]\w*)[""'][^{}]*?\}")]
    private static partial Regex SettingItem();

    [GeneratedRegex(@"\btype\s*:\s*[""'](\w+)[""']")]
    private static partial Regex SettingTypeValue();

    [GeneratedRegex(@"\btype\s*:\s*$")]
    private static partial Regex BeforeType();

    [GeneratedRegex(@"\barea\s*:\s*$")]
    private static partial Regex BeforeArea();

    [GeneratedRegex(@"\beasing\s*:\s*$")]
    private static partial Regex BeforeEasing();

    [GeneratedRegex(@"\bwidget\s*:\s*$")]
    private static partial Regex BeforeWidget();

    [GeneratedRegex(@"\bpreviewChick\s*:\s*$")]
    private static partial Regex BeforeChick();

    [GeneratedRegex(@"\bsettings\s*:\s*$")]
    private static partial Regex BeforeSettings();

    [GeneratedRegex(@"\beffect\s*\(\s*$")]
    private static partial Regex BeforeDeclaration();

    // ───────────── 바깥에서 부르는 것 ─────────────

    /// <summary><paramref name="caret"/> 자리의 자동 완성 후보(앞부분이 맞는 것, 표 차례 → 이름 차례).</summary>
    public static ScriptCompletionResult Suggest(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var code = Scan(text, caret);
        int start = caret;
        while (start > 0 && IsIdent(text[start - 1])) start--;
        string prefix = text[start..caret];
        if (code.InComment) return new(caret, []);
        var names = new Names(text);

        IEnumerable<ScriptCompletionItem> items;
        if (code.StringStart is int quote)
        {
            if (start <= quote) return new(caret, []);
            string before = text[..quote].TrimEnd();
            items = BeforeType().IsMatch(before) ? Members(ScriptApiCatalog.SettingType, quoted: false)
                : BeforeArea().IsMatch(before) ? Members(ScriptApiCatalog.Area, quoted: false)
                : BeforeEasing().IsMatch(before) ? Members(ScriptApiCatalog.Easing, quoted: false)
                : BeforeWidget().IsMatch(before) ? Members(ScriptApiCatalog.Widget, quoted: false)
                : BeforeChick().IsMatch(before) ? Chicks()
                : before.EndsWith('(') && CalleeBefore(text, code, before.Length - 1) is [var head, var method] && head == names.Ctx
                    ? method switch
                    {
                        "color" => names.Settings.Where(s => s.Type == "color").Select(SettingItem),
                        "colorAt" => names.Settings.Where(s => s.Type == "colorList").Select(SettingItem),
                        "pairColor" => names.Settings.Where(s => s.Type == "colorPairs").Select(SettingItem),
                        "hasGradient" => names.Settings.Where(s => s.Type is "color" or "colorList" or "colorPairs").Select(SettingItem),
                        _ => [],
                    }
                : [];
        }
        else if (PreviousCode(text, code, start) is int dot && text[dot] == '.')
        {
            items = ScopeOfChain(text, code, dot, names) is { } scope ? ScopeItems(scope, names) : [];
        }
        else if (KeyContext(text, code, start) is { } keys)
        {
            items = Members(keys, quoted: false);
        }
        else
        {
            items = Members(ScriptApiCatalog.Global, quoted: false)
                .Concat(names.Locals.Select(local => new ScriptCompletionItem(local, ScriptApiKind.Value, ScriptApiStrings.Completion_Local, "")))
                .Concat(Keywords.Select(keyword => new ScriptCompletionItem(keyword, ScriptApiKind.Keyword, ScriptApiStrings.Completion_Keyword, "")));
        }
        var list = items
            .Where(item => item.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && item.Name != prefix)
            .DistinctBy(item => item.Name)
            .ToList();
        return new(start, list);
    }

    /// <summary><paramref name="caret"/>가 표에 있는 함수의 괄호 안이면 그 함수와 지금 인자 번호. 아니면 null.</summary>
    public static ScriptSignatureHelp? Signature(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var code = Scan(text, caret);
        if (code.InComment) return null;
        int depth = 0, commas = 0;
        for (int i = caret - 1; i >= 0; i--)
        {
            if (!code.IsCode(i)) continue;
            char c = text[i];
            if (c is ')' or ']' or '}') depth++;
            else if (c is '[' or '{')
            {
                if (depth == 0) return null;
                depth--;
            }
            else if (c == '(')
            {
                if (depth > 0)
                {
                    depth--;
                    continue;
                }
                var names = new Names(text);
                var chain = CalleeBefore(text, code, i);
                if (chain is null) return null;
                var member = Resolve(chain, names);
                return member is { Kind: ScriptApiKind.Function } ? new ScriptSignatureHelp(member, commas, i) : null;
            }
            else if (c == ',' && depth == 0) commas++;
            else if (c == ';' && depth == 0) return null;
        }
        return null;
    }

    /// <summary>설명 풍선: <paramref name="offset"/>에 놓인 이름이 표에 있으면 그 항목(설정 키는 null).</summary>
    public static ScriptApiMember? Describe(string text, int offset)
    {
        if (offset < 0 || offset >= text.Length || !IsIdent(text[offset])) return null;
        int start = offset, end = offset;
        while (start > 0 && IsIdent(text[start - 1])) start--;
        while (end < text.Length && IsIdent(text[end])) end++;
        var code = Scan(text, end);
        if (!code.IsCode(start)) return null;
        var names = new Names(text);
        var chain = ChainEndingAt(text, code, end);
        if (chain is null) return null;
        if (chain.Count == 1 && KeyContext(text, code, start) is { } keys) return ScriptApiCatalog.Find(keys, chain[0]);
        return Resolve(chain, names);
    }

    // ───────────── 이름 풀기 ─────────────

    /// <summary>원문에서 읽은 이름들: render 매개변수 이름, 선언한 이름, 결과 종류를 아는 변수, 설정 키.</summary>
    private sealed class Names
    {
        public string Image = "img", Ctx = "ctx", SettingsParam = "s";
        public readonly List<string> Locals = [];
        public readonly Dictionary<string, string> Variables = new(StringComparer.Ordinal);
        public readonly List<(string Key, string Type)> Settings = [];

        public Names(string text)
        {
            if (RenderParams().Match(text) is { Success: true } render)
            {
                if (render.Groups[1].Success) Image = render.Groups[1].Value;
                if (render.Groups[2].Success) Ctx = render.Groups[2].Value;
                if (render.Groups[3].Success) SettingsParam = render.Groups[3].Value;
            }
            foreach (Match item in SettingItem().Matches(text))
            {
                var type = SettingTypeValue().Match(item.Value);
                Settings.Add((item.Groups[1].Value, type.Success ? type.Groups[1].Value : ""));
            }
            foreach (Match declared in Declared().Matches(text))
                if (!Locals.Contains(declared.Groups[1].Value)) Locals.Add(declared.Groups[1].Value);
            foreach (string name in new[] { Image, Ctx, SettingsParam })
                if (!Locals.Contains(name)) Locals.Add(name);
            // const c = ctx.color(…) → c는 색, const src = ctx.source → src는 원본 자리
            foreach (Match assigned in Assigned().Matches(text))
            {
                var chain = assigned.Groups[2].Value.Split('.');
                string? scope = assigned.Groups[3].Success ? Resolve(chain, this)?.Members : ScopeOf(chain, this);
                if (scope is not null) Variables[assigned.Groups[1].Value] = scope;
            }
        }
    }

    /// <summary>점으로 이은 이름(<c>ctx.source</c>)이 가리키는 객체의 자리. 모르면 null.</summary>
    private static string? ScopeOf(IReadOnlyList<string> chain, Names names)
    {
        if (chain.Count == 0) return null;
        string head = chain[0];
        string? scope = head == names.Image ? ScriptApiCatalog.Image
            : head == names.Ctx ? ScriptApiCatalog.Context
            : head == names.SettingsParam ? ScriptApiCatalog.Settings
            : head == "Math" ? ScriptApiCatalog.MathScope
            : names.Variables.GetValueOrDefault(head);
        for (int i = 1; i < chain.Count && scope is not null; i++)
        {
            if (scope == ScriptApiCatalog.Settings)
                scope = names.Settings.FirstOrDefault(s => s.Key == chain[i]).Type == "color" ? ScriptApiCatalog.Color : null;
            else scope = ScriptApiCatalog.Find(scope, chain[i])?.Members;
        }
        return scope;
    }

    /// <summary>점으로 이은 이름의 마지막이 표의 어느 항목인지(<c>ctx.color</c> → ctx의 color). 모르면 null.</summary>
    private static ScriptApiMember? Resolve(IReadOnlyList<string> chain, Names names)
    {
        if (chain.Count == 1) return ScriptApiCatalog.Find(ScriptApiCatalog.Global, chain[0]);
        return ScopeOf(chain.Take(chain.Count - 1).ToList(), names) is { } scope ? ScriptApiCatalog.Find(scope, chain[^1]) : null;
    }

    private static IEnumerable<ScriptCompletionItem> ScopeItems(string scope, Names names)
        => scope == ScriptApiCatalog.Settings ? names.Settings.Select(SettingItem) : Members(scope, quoted: false);

    private static IEnumerable<ScriptCompletionItem> Members(string scope, bool quoted)
        => ScriptApiCatalog.In(scope).Select(m => new ScriptCompletionItem(quoted ? $"\"{m.Name}\"" : m.Name, m.Kind, m.Signature, m.Summary));

    private static ScriptCompletionItem SettingItem((string Key, string Type) setting)
        => new(setting.Key, setting.Type == "color" ? ScriptApiKind.Object : ScriptApiKind.Value,
            ScriptApiStrings.FormatCompletion_Setting(type: setting.Type), "");

    private static IEnumerable<ScriptCompletionItem> Chicks()
        => PreviewChicks.Ids.Select((id, i) => new ScriptCompletionItem(id, ScriptApiKind.Literal,
            ScriptApiStrings.FormatCompletion_Chick(number: (i + 1).ToString(CultureInfo.CurrentCulture)), ""));

    // ───────────── 글자 훑기 ─────────────

    private static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';

    /// <summary>앞에서부터 <c>end</c>까지 훑은 결과: 자리마다 코드인지(문자열·주석이 아닌지), 끝 자리가 주석·문자열 안인지.</summary>
    private sealed class CodeMap(bool[] code, bool inComment, int? stringStart)
    {
        public bool InComment { get; } = inComment;
        /// <summary>끝 자리가 문자열 안이면 그 문자열의 여는 따옴표 자리.</summary>
        public int? StringStart { get; } = stringStart;
        public bool IsCode(int i) => i >= 0 && i < code.Length && code[i];
    }

    private static CodeMap Scan(string text, int end)
    {
        var code = new bool[end];
        int i = 0;
        int? stringStart = null;
        bool inComment = false;
        while (i < end)
        {
            char c = text[i];
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                int stop = text.IndexOf('\n', i);
                if (stop < 0 || stop >= end)
                {
                    inComment = true;
                    break;
                }
                i = stop;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                int stop = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (stop < 0 || stop + 2 > end)
                {
                    inComment = true;
                    break;
                }
                i = stop + 2;
                continue;
            }
            if (c is '"' or '\'' or '`')
            {
                int j = i + 1;
                while (j < end && text[j] != c && !(c != '`' && text[j] == '\n'))
                    j += text[j] == '\\' ? 2 : 1;
                if (j >= end)
                {
                    stringStart = i;
                    break;
                }
                i = j + 1;
                continue;
            }
            code[i] = true;
            i++;
        }
        return new CodeMap(code, inComment, stringStart);
    }

    /// <summary><paramref name="start"/> 앞의 공백이 아닌 코드 글자 자리(없으면 null).</summary>
    private static int? PreviousCode(string text, CodeMap code, int start)
    {
        for (int i = start - 1; i >= 0; i--)
            if (code.IsCode(i) && !char.IsWhiteSpace(text[i])) return i;
        return null;
    }

    /// <summary><paramref name="end"/>에서 끝나는 점 사슬(<c>ctx.source.x</c> → [ctx, source, x]). 부르기 결과(<c>f().</c>)면 그 함수의 사슬을 쓴다.</summary>
    private static List<string>? ChainEndingAt(string text, CodeMap code, int end)
    {
        var parts = new List<string>();
        int i = end;
        while (true)
        {
            int stop = i;
            while (i > 0 && IsIdent(text[i - 1])) i--;
            if (i == stop) return null;
            parts.Insert(0, text[i..stop]);
            if (PreviousCode(text, code, i) is int dot && text[dot] == '.')
            {
                i = dot;
                while (i > 0 && char.IsWhiteSpace(text[i - 1])) i--;
                continue;
            }
            return parts;
        }
    }

    /// <summary><paramref name="dot"/>(점) 앞의 사슬이 가리키는 객체의 자리. <c>ctx.color("c").</c>처럼 부르기 결과면 그 함수의 결과 자리.</summary>
    private static string? ScopeOfChain(string text, CodeMap code, int dot, Names names)
    {
        int end = dot;
        while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
        if (end > 0 && text[end - 1] == ')')
        {
            int open = MatchingOpen(text, code, end - 1);
            return open >= 0 && CalleeBefore(text, code, open) is { } callee ? Resolve(callee, names)?.Members : null;
        }
        return ChainEndingAt(text, code, end) is { } chain ? ScopeOf(chain, names) : null;
    }

    /// <summary><paramref name="open"/>(여는 괄호) 바로 앞의 함수 사슬.</summary>
    private static List<string>? CalleeBefore(string text, CodeMap code, int open)
    {
        int end = open;
        while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
        return ChainEndingAt(text, code, end);
    }

    /// <summary><paramref name="close"/>(닫는 괄호)와 짝인 여는 괄호 자리(없으면 −1).</summary>
    private static int MatchingOpen(string text, CodeMap code, int close)
    {
        int depth = 0;
        for (int i = close; i >= 0; i--)
        {
            if (!code.IsCode(i)) continue;
            if (text[i] is ')' or ']' or '}') depth++;
            else if (text[i] is '(' or '[' or '{' && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary><paramref name="start"/>가 선언 객체·설정 항목의 키 자리면 그 키의 자리(<see cref="ScriptApiCatalog.Declaration"/>·<see cref="ScriptApiCatalog.Setting"/>).</summary>
    private static string? KeyContext(string text, CodeMap code, int start)
    {
        if (PreviousCode(text, code, start) is not int previous || text[previous] is not ('{' or ',')) return null;
        int depth = 0;
        for (int i = start - 1; i >= 0; i--)
        {
            if (!code.IsCode(i)) continue;
            char c = text[i];
            if (c is ')' or ']' or '}') depth++;
            else if (c is '(' or '[' or '{')
            {
                if (depth-- > 0) continue;
                if (c != '{') return null;
                string before = text[..i].TrimEnd();
                if (BeforeDeclaration().IsMatch(before)) return ScriptApiCatalog.Declaration;
                // 설정 항목: { 바로 앞이 settings: [ … 안
                int bracket = OpenerBefore(text, code, i);
                return bracket >= 0 && text[bracket] == '[' && BeforeSettings().IsMatch(text[..bracket].TrimEnd()) ? ScriptApiCatalog.Setting : null;
            }
        }
        return null;
    }

    /// <summary><paramref name="position"/>를 감싼 가장 안쪽 여는 괄호 자리(없으면 −1).</summary>
    private static int OpenerBefore(string text, CodeMap code, int position)
    {
        int depth = 0;
        for (int i = position - 1; i >= 0; i--)
        {
            if (!code.IsCode(i)) continue;
            if (text[i] is ')' or ']' or '}') depth++;
            else if (text[i] is '(' or '[' or '{' && depth-- == 0) return i;
        }
        return -1;
    }
}
