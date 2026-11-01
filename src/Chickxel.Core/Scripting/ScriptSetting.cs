using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 스크립트 설정 값의 종류. 선언의 <c>type</c> 문자열 <c>int</c>, <c>number</c>, <c>bool</c>, <c>color</c>, <c>choice</c>, <c>colorList</c>, <c>colorPairs</c>, <c>layer</c>.
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

/// <summary>고르기 설정의 항목 하나. <see cref="Value"/>는 저장 ID, <see cref="Label"/>은 화면 이름.</summary>
public sealed record ScriptChoice(string Value, string Label);

/// <summary>색 쌍 목록 설정(<see cref="ScriptSettingType.ColorPairs"/>)의 항목 하나: <see cref="From"/>을 <see cref="To"/>로.</summary>
public readonly record struct ScriptColorPair(Color32 From, Color32 To);

/// <summary>
/// 스크립트가 선언한 설정 하나(5.4 명시적 설정 schema와 같은 정보). 효과 창의 범용 편집기, 저장과 검증이 이것을 쓴다.
/// <para>
/// <see cref="Key"/>는 영구 저장 키(영문자나 _로 시작). <see cref="Min"/>, <see cref="Max"/>, <see cref="Step"/>은 <see cref="ScriptSettingType.Int"/>,
/// <see cref="ScriptSettingType.Number"/>에만 있고 없으면 null이다. 색 목록·색 쌍 목록은 <see cref="Min"/>·<see cref="Max"/>가 항목 수 범위(정수, 늘 있음)다.
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

    /// <summary>레이어 고르기 설정 값을 맞춘다: 문자열이면 앞뒤 공백을 지운 이름(빈 문자열 = 고르지 않음), 너무 길거나 문자열이 아니면 null.</summary>
    public static string? LayerName(object? value)
        => value is string text && text.Trim() is { Length: <= MaxLayerNameLength } name ? name : null;

    /// <summary>
    /// 색의 알파를 고를 수 있는지(선언의 <c>alpha</c>, 기본 true). false면 색·색 목록·색 쌍 목록의 모든 색이 불투명(알파 255)으로 맞춰진다.
    /// </summary>
    public bool Alpha { get; init; } = true;

    /// <summary>효과 창 편집기 모양(선언의 <c>widget</c>, 기본 <see cref="ScriptSettingWidget.None"/>).</summary>
    public ScriptSettingWidget Widget { get; init; }

    /// <summary><see cref="ScriptSettingWidget.Point"/>의 Y 설정 키(선언의 <c>pair</c>). 아니면 null.</summary>
    public string? Pair { get; init; }

    /// <summary>색이 들어 있는 설정(색·색 목록·색 쌍 목록) — 그라데이션 슬롯을 가진다(<see cref="ScriptColorSlots"/>).</summary>
    public bool IsColor => Type is ScriptSettingType.Color or ScriptSettingType.ColorList or ScriptSettingType.ColorPairs;

    /// <summary>항목 수가 바뀌는 목록 설정(색 목록·색 쌍 목록).</summary>
    public bool IsList => Type is ScriptSettingType.ColorList or ScriptSettingType.ColorPairs;

    // 목록은 참조가 아니라 항목으로 비교한다(schema 비교·변경 감지)
    public bool Equals(ScriptSetting? other)
        => other is not null && Key == other.Key && Label == other.Label && Type == other.Type && Min == other.Min && Max == other.Max
            && Step == other.Step && SameValue(Default, other.Default) && Choices.SequenceEqual(other.Choices)
            && Alpha == other.Alpha && Widget == other.Widget && Pair == other.Pair;

    public override int GetHashCode() => HashCode.Combine(Key, Label, Type, Min, Max, Step, Choices.Count, HashCode.Combine(Alpha, Widget, Pair));

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
