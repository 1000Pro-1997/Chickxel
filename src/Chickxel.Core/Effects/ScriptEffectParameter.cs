using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Effects;

/// <summary>
/// 스크립트가 선언한 설정 하나(<see cref="ScriptSetting"/>)를 기본 효과 설정 schema와 같은 모양으로 보인다 — 레시피 매개변수·AI 도구·검사가
/// 기본 효과와 같은 길로 스크립트 효과 설정을 읽고 고친다. 값은 <see cref="ScriptEffect.Settings"/>에 있고, 넣을 때마다 그 원문의 schema로 맞춘다
/// (<see cref="ScriptSettingValues.Normalized"/> — 정수는 반올림·범위 자르기, 목록은 개수 맞추기, <c>alpha: false</c> 색은 불투명).
/// 정수 범위는 선언의 min·max(없으면 null), 고르기는 선언한 값, 그라데이션 슬롯은 설정 키 그대로(<see cref="ScriptColorSlots"/>).
/// </summary>
internal sealed class ScriptEffectParameter : EffectParameter
{
    private static readonly ConditionalWeakTable<ScriptEffectProgram, IReadOnlyList<EffectParameter>> s_schemas = new();

    private readonly ScriptEffectProgram _program;
    private readonly ScriptSetting _setting;

    private ScriptEffectParameter(ScriptEffectProgram program, ScriptSetting setting)
        : base(setting.Key, KindOf(setting.Type),
            setting.Type == ScriptSettingType.Int ? Bound(setting.Min) : null,
            setting.Type == ScriptSettingType.Int ? Bound(setting.Max) : null,
            setting.Type == ScriptSettingType.Choice ? [.. setting.Choices.Select(c => c.Value)] : null, null)
    {
        _program = program;
        _setting = setting;
        SetDefault(program.CreateEffect());
    }

    /// <summary>
    /// <paramref name="program"/>을 쓰는 효과의 설정 schema: 선언한 설정(선언 순서) 뒤에 움직이는 효과 공통 설정(<see cref="ScriptDefinition"/>의 schema).
    /// 원문마다 한 번만 만든다.
    /// </summary>
    public static IReadOnlyList<EffectParameter> SchemaOf(ScriptEffectProgram program)
        => s_schemas.GetValue(program, p => [.. p.Settings.Select(s => new ScriptEffectParameter(p, s)), .. ScriptDefinition.Definition.Parameters]);

    public override string? ColorSlot => _setting.IsColor ? Key : null;

    internal override JsonNode Write(LayerEffect effect)
    {
        object value = _program.NormalizeSettings(((ScriptEffect)effect).Settings)[Key];
        return _setting.Type switch
        {
            ScriptSettingType.Int => JsonValue.Create(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            ScriptSettingType.Number => JsonValue.Create(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            ScriptSettingType.Bool => JsonValue.Create((bool)value),
            ScriptSettingType.Color => JsonValue.Create(((Color32)value).ToHex()),
            ScriptSettingType.ColorList => ScriptSettingValues.ColorsJson((IReadOnlyList<Color32>)value),
            ScriptSettingType.ColorPairs => ScriptSettingValues.PairsJson((IReadOnlyList<ScriptColorPair>)value),
            _ => JsonValue.Create((string)value),
        };
    }

    internal override bool TryRead(LayerEffect effect, JsonNode? value, out LayerEffect result)
    {
        result = effect;
        if (Parse(value) is not { } parsed) return false;
        var script = (ScriptEffect)effect;
        result = script with { Settings = ScriptSettingValues.Normalized(_program, script.Settings.With(Key, parsed)) };
        return true;
    }

    /// <summary>저장 JSON 값을 설정 종류의 C# 값으로(모양이 틀리면 null). 범위·개수는 넣을 때 schema가 맞춘다. 목록의 잘못된 칸은 버린다.</summary>
    private object? Parse(JsonNode? node)
    {
        switch (_setting.Type)
        {
            case ScriptSettingType.Int:
            case ScriptSettingType.Number:
                return EffectJson.TryNumber(node, out double number) ? number : null;
            case ScriptSettingType.Bool:
                return EffectJson.TryBool(node, out bool flag) ? flag : null;
            case ScriptSettingType.Color:
                return EffectJson.TryColor(node, out var color) ? color : null;
            case ScriptSettingType.ColorList:
                return EffectJson.TryColors(node, out var colors) ? colors : null;
            case ScriptSettingType.ColorPairs:
                return EffectJson.TryColorPairs(node, out var pairs) ? pairs.Select(p => new ScriptColorPair(p.From, p.To)).ToArray() : null;
            default:
                // 고르기: 저장 ID 그대로, 없으면 대소문자만 다른 ID(기본 효과의 열거 이름 읽기와 같게)
                if (EffectJson.String(node) is not { } name) return null;
                return _setting.Choices.FirstOrDefault(c => c.Value == name)?.Value
                       ?? _setting.Choices.FirstOrDefault(c => string.Equals(c.Value, name, StringComparison.OrdinalIgnoreCase))?.Value;
        }
    }

    private static EffectParameterKind KindOf(ScriptSettingType type) => type switch
    {
        ScriptSettingType.Int => EffectParameterKind.Int,
        ScriptSettingType.Number => EffectParameterKind.Number,
        ScriptSettingType.Bool => EffectParameterKind.Bool,
        ScriptSettingType.Color => EffectParameterKind.Color,
        ScriptSettingType.ColorList => EffectParameterKind.ColorList,
        ScriptSettingType.ColorPairs => EffectParameterKind.ColorPairList,
        _ => EffectParameterKind.Choice,
    };

    // 정수 설정의 min·max는 선언에서 정수만 받는다 — int 범위로만 자른다
    private static int? Bound(double? bound) => bound is { } b ? (int)Math.Clamp(b, int.MinValue, int.MaxValue) : null;
}
