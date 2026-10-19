using System.Text.Json;
using System.Text.Json.Nodes;

namespace Gpixel.App;

/// <summary>단축키 하나(키 + 수정자).</summary>
public readonly record struct KeyChord(Key Key, KeyModifiers Mods)
{
    public override string ToString() => Shortcuts.Format(this);
}

/// <summary>단축키를 바꿀 수 있는 명령 하나. <see cref="Id"/> = "분류/이름"(설정·JSON 파일의 키).</summary>
public sealed record ShortcutCommand(string Id, string Category, string Name, IReadOnlyList<KeyChord> Defaults);

/// <summary>
/// 단축키 표기·읽기와 JSON 내보내기·불러오기. 표기는 OS와 무관하게 "Ctrl"이 명령 키(Windows Ctrl, macOS ⌘)를 뜻해
/// 같은 파일을 두 OS에서 쓸 수 있다(macOS의 실제 Control 키는 "Control").
/// </summary>
public static class Shortcuts
{
    public const int FileVersion = 1;

    private static readonly (Key Key, string Text)[] Names =
    [
        (Key.OemComma, ","), (Key.OemPeriod, "."), (Key.OemPlus, "="), (Key.OemMinus, "-"),
        (Key.OemOpenBrackets, "["), (Key.OemCloseBrackets, "]"), (Key.OemQuestion, "/"), (Key.OemSemicolon, ";"),
        (Key.OemQuotes, "'"), (Key.OemPipe, "\\"), (Key.OemTilde, "`"), (Key.Back, "Backspace"), (Key.Return, "Enter"),
        (Key.Escape, "Esc"), (Key.Add, "Num+"), (Key.Subtract, "Num-"), (Key.Multiply, "Num*"), (Key.Divide, "Num/"),
        (Key.D0, "0"), (Key.D1, "1"), (Key.D2, "2"), (Key.D3, "3"), (Key.D4, "4"),
        (Key.D5, "5"), (Key.D6, "6"), (Key.D7, "7"), (Key.D8, "8"), (Key.D9, "9"),
    ];

    /// <summary>수정자만 누른 상태(단축키로 쓸 수 없음).</summary>
    public static bool IsModifierKey(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.None;

    public static string Format(KeyChord c)
    {
        var parts = new List<string>(4);
        var mods = c.Mods;
        if ((mods & Platform.CommandModifier) != 0) { parts.Add("Ctrl"); mods &= ~Platform.CommandModifier; }
        if ((mods & KeyModifiers.Control) != 0) parts.Add("Control");
        if ((mods & KeyModifiers.Meta) != 0) parts.Add("Meta");
        if ((mods & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((mods & KeyModifiers.Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName(c.Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key k)
    {
        foreach (var (key, text) in Names) if (key == k) return text;
        return k.ToString();
    }

    /// <summary>표기를 읽는다(<see cref="Format"/>의 반대). 알 수 없으면 null.</summary>
    public static KeyChord? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        // 키 이름 자체에 '+'가 들어갈 수 있으므로(Num+) 알려진 수정자를 앞에서부터 떼어 내고 남은 것이 키다.
        var mods = KeyModifiers.None;
        while (true)
        {
            int plus = text.IndexOf('+');
            if (plus <= 0 || plus == text.Length - 1) break;
            var head = text[..plus];
            KeyModifiers? m = head switch
            {
                "Ctrl" => Platform.CommandModifier,
                "Control" => KeyModifiers.Control,
                "Meta" or "Cmd" => KeyModifiers.Meta,
                "Alt" => KeyModifiers.Alt,
                "Shift" => KeyModifiers.Shift,
                _ => null,
            };
            if (m is null) break;
            mods |= m.Value;
            text = text[(plus + 1)..];
        }
        foreach (var (key, name) in Names) if (name == text) return new KeyChord(key, mods);
        return Enum.TryParse<Key>(text, ignoreCase: false, out var k) && !IsModifierKey(k) && Enum.IsDefined(k) ? new KeyChord(k, mods) : null;
    }

    /// <summary>모든 명령의 지금 단축키를 JSON으로 쓴다(<c>{"version":1,"shortcuts":{"분류/이름":["Ctrl+Z"]}}</c>).</summary>
    public static string ToJson(IEnumerable<ShortcutCommand> commands, IReadOnlyDictionary<string, List<KeyChord>> current)
    {
        var map = new JsonObject();
        foreach (var c in commands)
            map[c.Id] = new JsonArray((current.TryGetValue(c.Id, out var keys) ? keys : c.Defaults).Select(k => (JsonNode?)Format(k)).ToArray());
        var root = new JsonObject { ["version"] = FileVersion, ["shortcuts"] = map };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>
    /// JSON을 읽어 알려진 명령의 단축키 표를 돌려준다. 모르는 명령은 <paramref name="unknown"/>에, 읽을 수 없는 키 표기는
    /// <paramref name="invalid"/>에 담고 건너뛴다. 형식이 틀리면 <see cref="JsonException"/>.
    /// </summary>
    public static Dictionary<string, List<KeyChord>> FromJson(string json, IEnumerable<ShortcutCommand> commands, List<string> unknown, List<string> invalid)
    {
        var known = commands.Select(c => c.Id).ToHashSet();
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("JSON 객체가 아닙니다.");
        var map = root["shortcuts"] as JsonObject ?? throw new JsonException("\"shortcuts\" 항목이 없습니다.");
        var result = new Dictionary<string, List<KeyChord>>();
        foreach (var (id, node) in map)
        {
            if (!known.Contains(id)) { unknown.Add(id); continue; }
            var keys = new List<KeyChord>();
            if (node is JsonArray arr)
                foreach (var item in arr)
                {
                    var text = item?.GetValueKind() == JsonValueKind.String ? item.GetValue<string>() : null;
                    if (Parse(text) is { } k) { if (!keys.Contains(k)) keys.Add(k); }
                    else invalid.Add($"{id}: {text}");
                }
            else if (node is not null) { invalid.Add($"{id}: {node.ToJsonString()}"); continue; }
            result[id] = keys;
        }
        return result;
    }
}
