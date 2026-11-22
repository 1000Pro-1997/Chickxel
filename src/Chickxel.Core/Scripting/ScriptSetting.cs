using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 스크립트 설정 값의 종류. 선언의 <c>type</c> 문자열 <c>int</c>, <c>number</c>, <c>bool</c>, <c>color</c>, <c>choice</c>, <c>colorList</c>, <c>colorPairs</c>, <c>layer</c>, <c>text</c>, <c>font</c>, <c>position</c>.
/// </summary>
public enum ScriptSettingType
{
    /// <summary>정수. C# 값은 <see cref="int"/>, JS 값은 number.</summary>
    Int,
    /// <summary>실수. C# 값은 <see cref="double"/>, JS 값은 number.</summary>
    Number,
    /// <summary>켜고 끔. C# 값은 <see cref="bool"/>, JS 값은 boolean.</summary>
    Bool,
    /// <summary>색. C# 값은 <see cref="Color32"/>, JS 값은 고정된 <c>{ r, g, b, a }</c>(0~255).</summary>
    Color,
    /// <summary>목록에서 고르기. C# 값과 JS 값 모두 고른 항목의 <see cref="ScriptChoice.Value"/> 문자열.</summary>
    Choice,
    /// <summary>
    /// 색 목록(순서 있음). C# 값은 <see cref="IReadOnlyList{T}"/>(<see cref="Color32"/>), JS 값은 고정된 <c>[{ r, g, b, a }, …]</c>.
    /// 저장 JSON은 <c>["#RRGGBB", …]</c>(기본 효과의 색 목록과 같은 모양). 개수는 <see cref="ScriptSetting.Min"/>~<see cref="ScriptSetting.Max"/>.
    /// </summary>
    ColorList,
    /// <summary>
    /// 색 쌍 목록(순서 있음, 바꿀 색 → 바뀐 색). C# 값은 <see cref="IReadOnlyList{T}"/>(<see cref="ScriptColorPair"/>), JS 값은 고정된
    /// <c>[{ from: { r, g, b, a }, to: { r, g, b, a } }, …]</c>. 저장 JSON은 <c>[["#바꿀 색", "#바뀐 색"], …]</c>(기본 효과의 색 쌍 목록과 같은 모양).
    /// </summary>
    ColorPairs,
    /// <summary>
    /// 레이어 고르기. C# 값과 JS 값 모두 레이어 이름 문자열(빈 문자열 = 고르지 않음, 앞뒤 공백 없이 <see cref="ScriptSetting.MaxLayerNameLength"/>자까지).
    /// 효과 창은 지금 문서의 레이어 이름(효과 만들기는 미리보기 장면의 레이어 이름)을 고르기 상자로 보이고, 스크립트는
    /// <c>ctx.layer("키")</c>·<c>ctx.layerWithEffects("키")</c>·<c>ctx.layerEffects("키")</c>로 그 레이어를 읽는다(<see cref="Model.ScriptLayerContext"/>).
    /// </summary>
    Layer,
    /// <summary>여러 줄 글. C# 값과 JS 값 모두 문자열, 선언의 <c>max</c>는 최대 글자 수.</summary>
    Text,
    /// <summary>
    /// 글꼴 고르기. C# 값과 JS 값 모두 글꼴 이름 문자열(<see cref="Chickxel.Core.Text.FontLibrary"/>의 저장 이름, 앞뒤 공백 없이 <see cref="TextEffect.MaxFontNameLength"/>자까지,
    /// 기본 <see cref="Chickxel.Core.Text.FontLibrary.DefaultName"/>). 효과 창은 글씨 효과와 같은 글꼴 목록(앱 글꼴 + 더한 글꼴)을 보이고, 스크립트는
    /// <c>ctx.text(글, { font: s.키 })</c>로 그린다. 이 컴퓨터에 없는 이름도 그대로 두며 그리기는 기본 글꼴로 한다.
    /// </summary>
    Font,
    /// <summary>
    /// 위치(기준 + 직접 자리). C# 값은 <see cref="ScriptPosition"/>, JS 값은 고정된 <c>{ anchor, x, y }</c>, 저장 JSON은 <c>{ "anchor": "pivot", "x": 0, "y": 0 }</c>.
    /// 효과 창은 위치 기준 고르기(직접 지정·캔버스 중심·피벗·그림 중심)와, 직접 지정일 때 X·Y 칸(범위가 있으면 점 고르기 판)을 보인다.
    /// 스크립트는 <c>ctx.position("키")</c>로 그림 좌표 <c>{ x, y }</c>를 받는다(<see cref="ScriptPosition"/>). <see cref="ScriptSetting.Min"/>·<see cref="ScriptSetting.Max"/>는 X·Y 범위.
    /// </summary>
    Position,
}

/// <summary>설정 칸을 그리는 모양(선언의 <c>widget</c>). 저장 값은 바꾸지 않고 효과 창의 편집기만 바꾼다.</summary>
public enum ScriptSettingWidget
{
    /// <summary>종류의 기본 편집기.</summary>
    None,
    /// <summary>각도 다이얼(int 설정, <c>widget: "angle"</c>). 0 = 오른쪽, 90 = 위.</summary>
    Angle,
    /// <summary>
    /// X·Y 점 고르기(int 설정 둘, <c>widget: "point", pair: "Y 설정 키"</c>). 이 설정이 X, <see cref="ScriptSetting.Pair"/>가 Y다 —
    /// Y 설정은 따로 칸을 두지 않고 이 칸에서 함께 고친다.
    /// </summary>
    Point,
}

/// <summary>
/// 설정 하나가 보이는 조건 하나(선언의 <c>when: { 키: 값 }</c>): 앞에 선언한 고르기·참거짓 설정 <see cref="Key"/>의 값이 <see cref="Values"/> 중 하나일 때.
/// 값은 고르기면 항목 값 문자열, 참거짓이면 <see cref="bool"/>.
/// </summary>
public sealed record ScriptSettingCondition(string Key, IReadOnlyList<object> Values)
{
    public bool Matches(object? value) => value is not null && Values.Contains(value);

    public bool Equals(ScriptSettingCondition? other) => other is not null && Key == other.Key && Values.SequenceEqual(other.Values);

    public override int GetHashCode() => HashCode.Combine(Key, Values.Count);
}

/// <summary>고르기 설정의 항목 하나. <see cref="Value"/>는 저장 ID, <see cref="Label"/>은 화면 이름.</summary>
public sealed record ScriptChoice(string Value, string Label);

/// <summary>색 쌍 목록 설정(<see cref="ScriptSettingType.ColorPairs"/>)의 항목 하나: <see cref="From"/>을 <see cref="To"/>로.</summary>
public readonly record struct ScriptColorPair(Color32 From, Color32 To);

/// <summary>
/// 위치 설정(<see cref="ScriptSettingType.Position"/>) 값: 기준 <see cref="Anchor"/>와, 기준이 <see cref="Custom"/>일 때 쓰는 자리(<see cref="X"/>, <see cref="Y"/> — 효과 원본 왼쪽 위에서 잰 px).
/// 다른 기준이면 자리는 저장만 하고 쓰지 않는다(직접 지정으로 돌아오면 그 자리).
/// </summary>
public readonly record struct ScriptPosition(string Anchor, int X, int Y)
{
    /// <summary>직접 지정: 효과 원본 왼쪽 위(<c>ctx.source</c>) + (X, Y).</summary>
    public const string Custom = "custom";
    /// <summary>캔버스 중심: <c>ctx.center</c>(효과 원본 — 캔버스, 부위면 부위 칸 — 가운데).</summary>
    public const string Center = "center";
    /// <summary>피벗: <c>ctx.pivot</c>.</summary>
    public const string Pivot = "pivot";
    /// <summary>그림 중심: 효과를 입히기 전 그림의 불투명 범위 가운데(비었으면 캔버스 중심).</summary>
    public const string Content = "content";

    /// <summary>고를 수 있는 기준(효과 창 차례).</summary>
    public static IReadOnlyList<string> Anchors { get; } = [Custom, Center, Pivot, Content];

    /// <summary>선언에 <c>default</c>가 없을 때: 캔버스 중심.</summary>
    public static ScriptPosition Default { get; } = new(Center, 0, 0);

    /// <summary>저장 JSON <c>{ "anchor": …, "x": …, "y": … }</c>.</summary>
    public System.Text.Json.Nodes.JsonObject ToJson() => new() { ["anchor"] = Anchor, ["x"] = X, ["y"] = Y };

    /// <summary>
    /// 저장·선언 모양 <c>{ anchor?, x?, y? }</c>을 읽는다: 빠진 칸은 <paramref name="fallback"/>의 값, 수는 반올림해 [<paramref name="min"/>, <paramref name="max"/>]로 자른다.
    /// 객체가 아니거나, 모르는 기준이거나, 칸의 모양이 틀리면 null.
    /// </summary>
    public static ScriptPosition? Read(System.Text.Json.Nodes.JsonNode? node, ScriptPosition fallback, double? min, double? max)
    {
        if (node is not System.Text.Json.Nodes.JsonObject obj) return null;
        string anchor = fallback.Anchor;
        if (obj["anchor"] is { } anchorNode)
        {
            if (anchorNode is not System.Text.Json.Nodes.JsonValue v || !v.TryGetValue(out string? name) || !Anchors.Contains(name)) return null;
            anchor = name;
        }
        int? Coordinate(string field, int keep)
        {
            if (obj[field] is not { } value) return keep;
            if (!Effects.EffectJson.TryNumber(value, out double d) || !double.IsFinite(d)) return null;
            return (int)Math.Clamp(Math.Round(Math.Clamp(d, min ?? int.MinValue, max ?? int.MaxValue)), int.MinValue, int.MaxValue);
        }
        if (Coordinate("x", fallback.X) is not { } x || Coordinate("y", fallback.Y) is not { } y) return null;
        return new ScriptPosition(anchor, x, y);
    }
}

/// <summary>
/// 스크립트가 선언한 설정 하나(5.4 명시적 설정 schema와 같은 정보). 효과 창의 범용 편집기, 저장과 검증이 이것을 쓴다.
/// <para>
/// <see cref="Key"/>는 영구 저장 키(영문자나 _로 시작). <see cref="Min"/>, <see cref="Max"/>, <see cref="Step"/>은 숫자 설정의 범위이며,
/// 색 목록·색 쌍 목록은 <see cref="Min"/>·<see cref="Max"/>가 항목 수 범위, 글 설정은 <see cref="Max"/>가 최대 글자 수다.
/// <see cref="Default"/>는 <see cref="Type"/>에 맞는 C# 값이다(<see cref="ScriptSettingType"/> 설명 참고). <see cref="Choices"/>는
/// <see cref="ScriptSettingType.Choice"/>에만 있고 나머지는 빈 목록이다.
/// </para>
/// </summary>
public sealed record ScriptSetting(
    string Key,
    string Label,
    ScriptSettingType Type,
    double? Min,
    double? Max,
    double? Step,
    object Default,
    IReadOnlyList<ScriptChoice> Choices)
{
    /// <summary>색 목록·색 쌍 목록 항목 수의 최대(선언 <c>max</c>의 한도).</summary>
    public const int MaxListCount = 64;

    /// <summary>색 목록·색 쌍 목록의 선언에 <c>max</c>가 없을 때의 항목 수 최대.</summary>
    public const int DefaultMaxListCount = 16;

    /// <summary>레이어 고르기 설정(<see cref="ScriptSettingType.Layer"/>) 값의 최대 글자 수.</summary>
    public const int MaxLayerNameLength = 64;
    /// <summary>글 설정의 선언에 <c>max</c>가 없을 때의 최대 글자 수 및 선언 허용 최대.</summary>
    public const int MaxTextLength = TextEffect.MaxLength;

    /// <summary>레이어 고르기 설정 값을 맞춘다: 문자열이면 앞뒤 공백을 지운 이름(빈 문자열 = 고르지 않음), 너무 길거나 문자열이 아니면 null.</summary>
    public static string? LayerName(object? value)
        => value is string text && text.Trim() is { Length: <= MaxLayerNameLength } name ? name : null;

    /// <summary>글꼴 설정 값을 맞춘다: 문자열이면 앞뒤 공백을 지운 이름, 비었거나 너무 길거나 문자열이 아니면 null(기본값으로).</summary>
    public static string? FontName(object? value)
        => value is string text && text.Trim() is { Length: > 0 and <= TextEffect.MaxFontNameLength } name ? name : null;

    /// <summary>
    /// 색의 알파를 고를 수 있는지(선언의 <c>alpha</c>, 기본 true). false면 색·색 목록·색 쌍 목록의 모든 색이 불투명(알파 255)으로 맞춰진다.
    /// </summary>
    public bool Alpha { get; init; } = true;

    /// <summary>효과 창 편집기 모양(선언의 <c>widget</c>, 기본 <see cref="ScriptSettingWidget.None"/>).</summary>
    public ScriptSettingWidget Widget { get; init; }

    /// <summary><see cref="ScriptSettingWidget.Point"/>의 Y 설정 키(선언의 <c>pair</c>). 아니면 null.</summary>
    public string? Pair { get; init; }

    /// <summary>
    /// 보이는 조건(선언의 <c>when</c>, 없으면 빈 목록 = 늘 보임): 모두 맞을 때만 설정 칸에 보인다(<see cref="ScriptEffectProgram.IsShown"/>).
    /// 숨겨도 값은 그대로 저장되고 render에서 읽힌다.
    /// </summary>
    public IReadOnlyList<ScriptSettingCondition> When { get; init; } = [];

    /// <summary>
    /// 묶음 제목(선언의 <c>group</c>, 없으면 null): 이어서 선언한 같은 제목의 설정을 설정 칸에서 제목 달린 테두리 상자 하나로 둘러싼다.
    /// 값과 저장에는 상관없다. 번역은 <c>translations.언어.labels["group:제목"]</c>(<see cref="ScriptTranslation.GroupKey"/>).
    /// </summary>
    public string? Group { get; init; }

    /// <summary>색이 들어 있는 설정(색·색 목록·색 쌍 목록) — 그라데이션 슬롯을 가진다(<see cref="ScriptColorSlots"/>).</summary>
    public bool IsColor => Type is ScriptSettingType.Color or ScriptSettingType.ColorList or ScriptSettingType.ColorPairs;

    /// <summary>항목 수가 바뀌는 목록 설정(색 목록·색 쌍 목록).</summary>
    public bool IsList => Type is ScriptSettingType.ColorList or ScriptSettingType.ColorPairs;

    // 목록은 참조가 아니라 항목으로 비교한다(schema 비교·변경 감지)
    public bool Equals(ScriptSetting? other)
        => other is not null && Key == other.Key && Label == other.Label && Type == other.Type && Min == other.Min && Max == other.Max
            && Step == other.Step && SameValue(Default, other.Default) && Choices.SequenceEqual(other.Choices)
            && Alpha == other.Alpha && Widget == other.Widget && Pair == other.Pair && When.SequenceEqual(other.When) && Group == other.Group;

    public override int GetHashCode() => HashCode.Combine(Key, Label, Type, Min, Max, Step, Choices.Count, HashCode.Combine(Alpha, Widget, Pair, Group));

    /// <summary>두 설정 값(C# 값)이 같은지: 색 목록·색 쌍 목록은 항목으로 비교한다.</summary>
    public static bool SameValue(object a, object b) => (a, b) switch
    {
        (IReadOnlyList<Color32> x, IReadOnlyList<Color32> y) => x.SequenceEqual(y),
        (IReadOnlyList<ScriptColorPair> x, IReadOnlyList<ScriptColorPair> y) => x.SequenceEqual(y),
        _ => a.Equals(b),
    };
}

/// <summary>
/// 스크립트 색 설정의 그라데이션 슬롯 이름(<see cref="LayerEffect.ColorGradients"/>의 키). 색은 설정 키 그대로(<c>color</c>),
/// 색 목록은 <c>키[번호]</c>(<c>colors[0]</c>), 색 쌍 목록은 <c>키[번호].from</c>·<c>키[번호].to</c>.
/// </summary>
public static class ScriptColorSlots
{
    /// <summary>색 목록 항목 <paramref name="index"/>의 슬롯.</summary>
    public static string Item(string key, int index) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{key}[{index}]");

    /// <summary>색 쌍 목록 항목 <paramref name="index"/>의 바꿀 색(<paramref name="to"/> = false)이나 바뀐 색 슬롯.</summary>
    public static string PairItem(string key, int index, bool to) => Item(key, index) + (to ? ".to" : ".from");

    /// <summary>
    /// <paramref name="slot"/>이 <paramref name="settings"/>의 어느 색 슬롯인지: 설정, 항목 번호(색이면 -1), 쌍의 바뀐 색인지. 아니면 false.
    /// 번호는 그 설정의 최대 항목 수(<see cref="ScriptSetting.Max"/>) 안이어야 한다.
    /// </summary>
    public static bool TryParse(IReadOnlyList<ScriptSetting> settings, string slot, out ScriptSetting setting, out int index, out bool to)
    {
        index = -1;
        to = false;
        int open = slot.IndexOf('[');
        string key = open < 0 ? slot : slot[..open];
        setting = null!;
        foreach (var s in settings)
            if (s.Key == key && s.IsColor)
            {
                setting = s;
                break;
            }
        if (setting is null) return false;
        if (open < 0) return setting.Type == ScriptSettingType.Color;
        if (setting.Type == ScriptSettingType.Color) return false;
        int close = slot.IndexOf(']', open + 1);
        if (close < 0 || !int.TryParse(slot.AsSpan(open + 1, close - open - 1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out index) || index >= (setting.Max ?? 0)
            || close - open - 1 != index.ToString(System.Globalization.CultureInfo.InvariantCulture).Length) return false;
        var rest = slot.AsSpan(close + 1);
        if (setting.Type == ScriptSettingType.ColorList) return rest.Length == 0;
        to = rest.SequenceEqual(".to");
        return to || rest.SequenceEqual(".from");
    }
}
