using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>설정 값의 종류(<see cref="EffectSetting.Kind"/>).</summary>
public enum EffectSettingKind
{
    /// <summary>정수(효과가 범위 밖 값을 잘라 냄 — <see cref="EffectSetting.Min"/>·<see cref="EffectSetting.Max"/>).</summary>
    Number,
    Boolean,
    /// <summary>"#RRGGBB" / "#RRGGBBAA".</summary>
    Color,
    /// <summary>정해진 이름 중 하나(<see cref="EffectSetting.Choices"/>).</summary>
    Choice,
    Text,
    /// <summary>JSON 배열(색 목록, 색 쌍 목록).</summary>
    List,
    /// <summary>JSON 객체(색 설정별 그라데이션).</summary>
    Object,
}

/// <summary>효과 종류 하나의 설정 하나: 파일 형식(.gpixel·사용자 정의 효과) 키, 종류, 기본값, 고를 수 있는 값, 잘리는 범위.</summary>
public sealed record EffectSetting(string Key, EffectSettingKind Kind, JsonNode? Default, IReadOnlyList<string>? Choices, int? Min, int? Max);

/// <summary><see cref="EffectSettings.Merge"/>가 받은 설정의 문제.</summary>
public enum EffectSettingError
{
    /// <summary>이 효과 종류에 없는 키(<see cref="EffectSettingException.Detail"/> = 있는 키들).</summary>
    UnknownKey,
    /// <summary>종류·켜짐·프레임 범위·클리핑 키 — 설정이 아니라 따로 정한다.</summary>
    ReservedKey,
    /// <summary>값의 JSON 종류가 다름(<see cref="EffectSettingException.Detail"/> = 기대한 <see cref="EffectSettingKind"/>).</summary>
    WrongType,
    /// <summary>고를 수 있는 이름이 아님(<see cref="EffectSettingException.Detail"/> = 이름들).</summary>
    NotAChoice,
    InvalidColor,
    /// <summary>목록의 일부 항목이 버려짐(잘못된 색·모양, 개수 제한).</summary>
    InvalidEntries,
    /// <summary>파일 형식으로 읽을 수 없음(<see cref="EffectSettingException.Detail"/> = 원인).</summary>
    InvalidValue,
}

/// <summary>효과 설정 오류. 메시지는 앱이 <see cref="Error"/>·<see cref="Key"/>·<see cref="Detail"/>로 화면 언어 문구를 만든다.</summary>
public sealed class EffectSettingException(string key, EffectSettingError error, string? detail = null)
    : ArgumentException($"{key}: {error}{(detail is null ? "" : " (" + detail + ")")}")
{
    public string Key { get; } = key;
    public EffectSettingError Error { get; } = error;
    public string? Detail { get; } = detail;
}

/// <summary>
/// 레이어 효과 설정을 프로젝트 파일과 같은 JSON(<see cref="ProjectIO.EffectDto"/>, camelCase)으로 읽고 고친다 — AI 도구가 효과 종류마다 따로 쓰지 않고
/// 모든 효과 종류를 같은 형식으로 다룬다. 종류·켜짐·프레임 범위·클리핑은 설정 JSON에 넣지 않는다(<see cref="ReservedKeys"/>).
/// 기본값이면 파일에 쓰지 않는 설정(그림자 디더, 외곽선 원본 지우기 등)과 고를 수 있는 이름·잘리는 범위는 기본 효과의 속성을 바꿔 보며 찾는다.
/// </summary>
public static class EffectSettings
{
    /// <summary>모든 효과에 있는 색 설정별 그라데이션 키(파일 형식 그대로의 객체).</summary>
    public const string GradientsKey = "colorGradients";

    /// <summary>설정 JSON에서 빼는 파일 키(효과 종류·켜짐·클리핑·프레임 범위).</summary>
    public static readonly IReadOnlyList<string> ReservedKeys = ["type", "enabled", "clipToPrevious", "frames"];

    /// <summary>범위 찾기에 넣어 보는 값(이보다 작게 잘리면 최대·최소로 본다).</summary>
    private const int RangeProbe = 1_000_000;

    private static readonly HashSet<string> ReservedNormalized = ["type", "enabled", "cliptoprevious", "cliptoabove", "frames"];
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<EffectSetting>> Cache = new();
    private static readonly MethodInfo CloneMethod = typeof(LayerEffect).GetMethod("<Clone>$")!;

    /// <summary>파일 형식의 효과 종류 이름("outline", "hitflash" …).</summary>
    public static string TypeId(LayerEffect effect) => ProjectIO.EffectDto.From(effect).Type!;

    /// <summary>종류 이름의 기본 효과(모든 프레임, 켜짐). 모르는 종류나 예전 이름(다른 종류로 읽히는 것)이면 null.</summary>
    public static LayerEffect? Create(string typeId)
    {
        var id = typeId.Trim().ToLowerInvariant();
        var effect = new ProjectIO.EffectDto { Type = id }.ToEffect();
        return effect is not null && TypeId(effect) == id ? effect : null;
    }

    /// <summary>효과 설정(파일 형식, <see cref="ReservedKeys"/> 뺌).</summary>
    public static JsonObject ToJson(LayerEffect effect)
    {
        var json = Full(effect);
        foreach (var key in ReservedKeys) json.Remove(key);
        return json;
    }

    /// <summary>효과 종류의 설정 목록(기본값은 그 종류의 기본 효과 값). 마지막은 늘 <see cref="GradientsKey"/>.</summary>
    public static IReadOnlyList<EffectSetting> Describe(LayerEffect effect) => Cache.GetOrAdd(effect.GetType(), Build);

    /// <summary>
    /// <paramref name="settings"/>(일부 키만, 키는 대소문자·밑줄 무시)를 <paramref name="effect"/> 위에 덮은 새 효과. 켜짐·프레임 범위·클리핑은 그대로다.
    /// 값이 null인 키는 그 종류의 기본값으로 돌아간다. 효과가 범위에 맞춰 자른 정수 설정은 <c>Clamped</c>에 "키 = 값"으로 돌려준다.
    /// 잘못된 키·값이면 <see cref="EffectSettingException"/>.
    /// </summary>
    public static (LayerEffect Effect, IReadOnlyList<string> Clamped) Merge(LayerEffect effect, JsonObject? settings)
    {
        if (settings is null || settings.Count == 0) return (effect, []);
        var info = Describe(effect);
        var full = Full(effect);
        var given = new List<(EffectSetting Setting, JsonNode Value)>();
        foreach (var (rawKey, value) in settings)
        {
            var normalized = Normalize(rawKey);
            if (ReservedNormalized.Contains(normalized)) throw new EffectSettingException(rawKey, EffectSettingError.ReservedKey);
            var setting = info.FirstOrDefault(s => Normalize(s.Key) == normalized)
                          ?? throw new EffectSettingException(rawKey, EffectSettingError.UnknownKey, string.Join(", ", info.Select(s => s.Key)));
            if (value is null)
            {
                full.Remove(setting.Key);
                continue;
            }
            var checkedValue = Check(setting, value);
            full[setting.Key] = checkedValue;
            given.Add((setting, checkedValue));
        }

        LayerEffect merged;
        try
        {
            merged = Read(full) ?? throw new EffectSettingException(TypeId(effect), EffectSettingError.InvalidValue);
        }
        catch (JsonException ex)
        {
            var key = ex.Path is { Length: > 2 } path ? path.TrimStart('$', '.').Split('.', '[')[0] : "settings";
            throw new EffectSettingException(key, EffectSettingError.InvalidValue, ex.Message);
        }

        var output = ToJson(merged);
        var clamped = new List<string>();
        foreach (var (setting, value) in given)
        {
            var result = output[setting.Key];
            if (setting.Kind == EffectSettingKind.Number && !JsonNode.DeepEquals(result, value))
                clamped.Add($"{setting.Key} = {result?.ToJsonString() ?? "null"}");
            else if (setting.Kind == EffectSettingKind.List && value is JsonArray asked && (result as JsonArray)?.Count != asked.Count)
                throw new EffectSettingException(setting.Key, EffectSettingError.InvalidEntries, $"{(result as JsonArray)?.Count ?? 0}/{asked.Count}");
        }
        return (merged, clamped);
    }

    // ───────────── 내부 ─────────────

    private static JsonObject Full(LayerEffect effect)
        => (JsonObject)JsonSerializer.SerializeToNode(ProjectIO.EffectDto.From(effect), ProjectIO.Options)!;

    private static LayerEffect? Read(JsonObject full) => full.Deserialize<ProjectIO.EffectDto>(ProjectIO.Options)?.ToEffect();

    /// <summary>키 비교용: 밑줄·빼기·공백을 빼고 소문자("repeat_count" = "repeatCount").</summary>
    private static string Normalize(string key)
        => string.Concat(key.Where(c => c is not ('_' or '-' or ' '))).ToLowerInvariant();

    private static JsonNode Check(EffectSetting setting, JsonNode value)
    {
        var key = setting.Key;
        switch (setting.Kind)
        {
            case EffectSettingKind.Number:
                if (value is JsonValue v)
                {
                    if (v.TryGetValue(out int i)) return JsonValue.Create(i);
                    if (v.TryGetValue(out double d) && d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue) return JsonValue.Create((int)d);
                    if (v.TryGetValue(out string? s) && int.TryParse(s.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out i))
                        return JsonValue.Create(i);
                }
                break;
            case EffectSettingKind.Boolean:
                if (value is JsonValue bv)
                {
                    if (bv.TryGetValue(out bool b)) return JsonValue.Create(b);
                    if (bv.TryGetValue(out string? s) && bool.TryParse(s.Trim(), out b)) return JsonValue.Create(b);
                }
                break;
            case EffectSettingKind.Choice:
                if (AsString(value) is { } name)
                {
                    var wanted = Normalize(name);
                    return JsonValue.Create(setting.Choices!.FirstOrDefault(c => Normalize(c) == wanted)
                                            ?? throw new EffectSettingException(key, EffectSettingError.NotAChoice, string.Join(", ", setting.Choices!)));
                }
                break;
            case EffectSettingKind.Color:
                if (AsString(value) is { } text)
                {
                    var t = text.Trim();
                    if (t.Equals("transparent", StringComparison.OrdinalIgnoreCase) || t.Equals("none", StringComparison.OrdinalIgnoreCase))
                        return JsonValue.Create(Color32.Transparent.ToHex());
                    if (!Color32.TryParseHex(t, out var color)) throw new EffectSettingException(key, EffectSettingError.InvalidColor, text);
                    return JsonValue.Create(color.ToHex());
                }
                break;
            case EffectSettingKind.Text:
                if (AsString(value) is { } plain) return JsonValue.Create(plain);
                break;
            case EffectSettingKind.List:
                if (value is JsonArray) return value.DeepClone();
                break;
            case EffectSettingKind.Object:
                if (value is JsonObject) return value.DeepClone();
                break;
        }
        throw new EffectSettingException(key, EffectSettingError.WrongType, setting.Kind.ToString());
    }

    private static string? AsString(JsonNode node) => node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    /// <summary>
    /// 기본 효과의 설정 목록: 파일에 쓰이는 키 + 기본값이면 쓰이지 않는 키(속성을 바꿔 보면 나타남). 열거형 속성은 고를 이름, 정수는 잘리는 범위를 찾는다.
    /// </summary>
    private static IReadOnlyList<EffectSetting> Build(Type type)
    {
        var def = (LayerEffect)Activator.CreateInstance(type)!;
        var json = ToJson(def);
        var probed = Probe(def, json);
        var result = new List<EffectSetting>();
        foreach (var (key, value) in json)
        {
            if (key == GradientsKey) continue;
            probed.TryGetValue(key, out var p);
            var kind = p.Kind ?? Guess(value);
            var (min, max) = kind == EffectSettingKind.Number ? Range(def, key) : (null, null);
            result.Add(new EffectSetting(key, kind, value?.DeepClone(), kind == EffectSettingKind.Choice ? p.Choices : null, min, max));
        }
        foreach (var (key, p) in probed)
        {
            if (json.ContainsKey(key) || p.Kind is not { } kind) continue;
            var (min, max) = kind == EffectSettingKind.Number ? Range(def, key) : (null, null);
            result.Add(new EffectSetting(key, kind, p.Default, p.Choices, min, max));
        }
        result.Add(new EffectSetting(GradientsKey, EffectSettingKind.Object, null, null, null, null));
        return result;
    }

    private static EffectSettingKind Guess(JsonNode? value) => value switch
    {
        JsonArray => EffectSettingKind.List,
        JsonObject => EffectSettingKind.Object,
        JsonValue v => v.GetValueKind() switch
        {
            JsonValueKind.Number => EffectSettingKind.Number,
            JsonValueKind.True or JsonValueKind.False => EffectSettingKind.Boolean,
            _ => v.TryGetValue(out string? s) && s.StartsWith('#') && Color32.TryParseHex(s, out _) ? EffectSettingKind.Color : EffectSettingKind.Text,
        },
        _ => EffectSettingKind.Text,
    };

    /// <summary>
    /// 기본 효과의 공개 init 속성(열거형·bool·정수·색)을 하나씩 바꿔 보고 달라진 설정 키를 찾는다: 키 → 종류, 기본값(속성 값), 열거형이면 이름들.
    /// 파일에 쓰이지 않는 속성(그릴 때만 쓰는 값)은 아무 키도 바뀌지 않아 빠진다.
    /// </summary>
    private static Dictionary<string, (EffectSettingKind? Kind, JsonNode? Default, IReadOnlyList<string>? Choices)> Probe(LayerEffect def, JsonObject json)
    {
        var found = new Dictionary<string, (EffectSettingKind?, JsonNode?, IReadOnlyList<string>?)>();
        foreach (var prop in def.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.SetMethod is not { IsPublic: true } || prop.GetIndexParameters().Length > 0) continue;
            var type = prop.PropertyType;
            var current = prop.GetValue(def);
            object[] tries;
            EffectSettingKind kind;
            JsonNode? defaultValue;
            if (type.IsEnum)
            {
                tries = Enum.GetValues(type).Cast<object>().Where(v => !v.Equals(current)).ToArray();
                kind = EffectSettingKind.Choice;
                defaultValue = JsonValue.Create(current!.ToString());
            }
            else if (type == typeof(bool))
            {
                tries = [!(bool)current!];
                kind = EffectSettingKind.Boolean;
                defaultValue = JsonValue.Create((bool)current!);
            }
            else if (type == typeof(int))
            {
                tries = [(int)current! + 1, (int)current! - 1];
                kind = EffectSettingKind.Number;
                defaultValue = JsonValue.Create((int)current!);
            }
            else if (type == typeof(Color32))
            {
                var c = (Color32)current!;
                tries = [c == new Color32(1, 2, 3) ? new Color32(4, 5, 6) : new Color32(1, 2, 3)];
                kind = EffectSettingKind.Color;
                defaultValue = JsonValue.Create(c.ToHex());
            }
            else continue;

            string? key = null;
            foreach (var value in tries)
            {
                var copy = (LayerEffect)CloneMethod.Invoke(def, null)!;
                try
                {
                    prop.SetValue(copy, value);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }
                key = Changed(json, ToJson(copy));
                if (key is not null) break;
            }
            if (key is null || found.ContainsKey(key)) continue;
            found[key] = (kind, defaultValue, type.IsEnum ? Enum.GetNames(type) : null);
        }
        return found;
    }

    /// <summary>바뀐(또는 새로 생긴) 첫 키.</summary>
    private static string? Changed(JsonObject before, JsonObject after)
    {
        foreach (var (key, value) in after)
            if (!before.TryGetPropertyValue(key, out var old) || !JsonNode.DeepEquals(old, value))
                return key;
        foreach (var (key, _) in before)
            if (!after.ContainsKey(key))
                return key;
        return null;
    }

    /// <summary>정수 설정을 아주 크게·작게 넣어 효과가 잘라 내는 최소·최대(잘리지 않는 쪽은 null).</summary>
    private static (int? Min, int? Max) Range(LayerEffect def, string key)
    {
        int? Clamp(int probe)
        {
            try
            {
                var full = Full(def);
                full[key] = probe;
                return Read(full) is { } e && ToJson(e)[key] is JsonValue v && v.TryGetValue(out int got) ? got : null;
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
            {
                return null;
            }
        }
        int? max = Clamp(RangeProbe), min = Clamp(-RangeProbe);
        return (min is { } lo && lo > -RangeProbe ? lo : null, max is { } hi && hi < RangeProbe ? hi : null);
    }
}
