using System.Text.Json.Nodes;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Effects;

/// <summary>
/// 예전 기본(C#) 효과 → 같은 그림을 내는 공식 스크립트 효과(<see cref="OfficialEffects"/>) 바꾸기. 프로젝트·레시피·실행취소 기록·클립보드·AI가 읽는 효과 JSON은
/// 모두 <see cref="EffectCodec.Read"/>를 거치므로 거기서 바꾸고, 원문은 부르는 쪽이 자기 스크립트 표에 넣는다(<see cref="OfficialEffects.Source"/> — 저장하면 스크립트로 씀).
/// <list type="bullet">
/// <item>설정: 기본 효과의 저장 설정 JSON(<see cref="EffectDefinition.WriteSettings"/>)을 키 그대로 스크립트 설정으로(스크립트 설정 키 = 기본 효과 저장 키, 시험이 확인).</item>
/// <item>켜짐·프레임 범위·클리핑·레시피 묶음 자리, 움직이는 효과의 빠르기 곡선·반복·반복 횟수.</item>
/// <item>색 그라데이션: 슬롯 이름을 <see cref="ScriptSlot"/>로(<c>Color</c> → <c>color</c>, <c>Colors[0]</c> → <c>colors[0]</c>, <c>Pairs[0].From</c> → <c>pairs[0].from</c>).</item>
/// </list>
/// 공식 원문이 없는 기본 효과(글씨 등)는 바꾸지 않는다.
/// </summary>
public static class LegacyEffects
{
    /// <summary>효과가 공식 스크립트로 바꿀 예전 기본 효과인지.</summary>
    public static bool IsLegacy(LayerEffect effect)
        => effect is not ScriptEffect && EffectRegistry.Of(effect) is { } d && OfficialEffects.IdOfType(d.TypeId) is not null;

    /// <summary>예전 기본 효과의 공식 효과 ID(바꿀 효과가 아니면 null).</summary>
    public static string? OfficialIdOf(LayerEffect effect)
        => effect is ScriptEffect ? null : EffectRegistry.Of(effect) is { } d ? OfficialEffects.IdOfType(d.TypeId) : null;

    /// <summary>예전 기본 효과면 공식 스크립트 효과로, 아니면 그대로.</summary>
    public static LayerEffect Convert(LayerEffect effect)
        => OfficialIdOf(effect) is { } id ? ToScript(effect, OfficialEffects.Program(id)) : effect;

    /// <summary>목록의 예전 기본 효과를 모두 바꾼다(바꿀 것이 없으면 같은 목록).</summary>
    public static IReadOnlyList<LayerEffect> Convert(IReadOnlyList<LayerEffect> effects)
    {
        LayerEffect[]? result = null;
        for (int i = 0; i < effects.Count; i++)
        {
            if (!IsLegacy(effects[i])) continue;
            result ??= [.. effects];
            result[i] = Convert(effects[i]);
        }
        return result ?? effects;
    }

    /// <summary>
    /// 기본 효과 <paramref name="builtIn"/>와 같은 설정의 스크립트 효과(<paramref name="program"/> — 보통 그 효과의 공식 원문).
    /// 스크립트가 선언하지 않은 설정 키는 버리고, 선언했지만 기본 효과에 없는 키는 선언의 기본값이다.
    /// </summary>
    public static ScriptEffect ToScript(LayerEffect builtIn, ScriptEffectProgram program)
    {
        var definition = EffectRegistry.Of(builtIn) ?? throw new ArgumentException(builtIn.GetType().Name, nameof(builtIn));
        // 저장 파일처럼 글로 썼다 읽는다(메모리의 JsonValue<int>는 double로 읽히지 않음)
        var json = (JsonObject)JsonNode.Parse(definition.WriteSettings(builtIn).ToJsonString())!;
        var values = ScriptSettingValues.FromJson(json, key => !ScriptEffectProgram.ReservedSettingKeys.Contains(key));
        var script = program.CreateEffect(values) with
        {
            Enabled = builtIn.Enabled,
            Frames = builtIn.Frames,
            ClipToPrevious = builtIn.ClipToPrevious,
            Recipe = builtIn.Recipe,
            // 그라데이션이 없으면 빈 표를 그대로(새 사전을 만들면 같은 효과 값이 달라 보임)
            ColorGradients = builtIn.ColorGradients.Count == 0 ? builtIn.ColorGradients : builtIn.ColorGradients.ToDictionary(e => ScriptSlot(e.Key), e => e.Value),
        };
        return builtIn is AnimatedEffect animated
            ? script with { Easing = animated.Easing, Repeat = animated.Repeat, RepeatCount = animated.RepeatCount }
            : script;
    }

    /// <summary>기본 효과의 그라데이션 슬롯 이름 → 스크립트 슬롯 이름: 첫 글자를 소문자로, <c>.From</c>·<c>.To</c> → <c>.from</c>·<c>.to</c>.</summary>
    public static string ScriptSlot(string builtInSlot)
    {
        if (builtInSlot.Length == 0) return builtInSlot;
        var slot = char.ToLowerInvariant(builtInSlot[0]) + builtInSlot[1..];
        return slot.EndsWith(".From", StringComparison.Ordinal) ? slot[..^5] + ".from"
            : slot.EndsWith(".To", StringComparison.Ordinal) ? slot[..^3] + ".to"
            : slot;
    }
}
