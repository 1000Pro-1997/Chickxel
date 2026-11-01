using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Effects;

/// <summary>
/// C# 효과 정의의 중앙 목록. 앱이 시작하기 전에 정적 목록으로 정해진다(어셈블리를 훑지 않음).
/// <para>
/// 예전 기본 효과(외곽선·그림자 … 홀로그램)는 공식 스크립트 효과(<see cref="OfficialEffects"/>, 기본 설치 패키지 <see cref="Assets.OfficialEffectPackages"/>)로 옮겨 갔다.
/// 그 C# 모델·정의는 이제 두 가지로만 남는다: (1) 예전 파일을 읽는 길 — 프로젝트·레시피·실행취소 기록·클립보드·AI의 효과 JSON은
/// <see cref="EffectCodec.Read"/>가 이 정의로 읽은 뒤 같은 그림의 공식 스크립트로 바꾼다(<see cref="LegacyEffects"/>), (2) 픽셀 비교 기준 — 시험
/// (<c>OfficialEffectParity*</c>)이 C# 렌더러와 공식 스크립트의 결과가 바이트까지 같은지 본다. 효과 창·AI 도구·프리셋 어디에서도 고를 수 없다.
/// 공식 스크립트가 없는 C# 효과(글씨)만 <see cref="Selectable"/>로 고를 수 있다.
/// </para>
/// </summary>
public static class EffectRegistry
{
    /// <summary>
    /// 앱에 들어 있는 C# 효과(예전 기본 효과 차례: 고정 효과 먼저, 움직이는 효과는 뒤에). 공식 스크립트로 옮긴 효과는 읽기·비교용이다(위 설명).
    /// </summary>
    public static IReadOnlyList<EffectDefinition> BuiltIn { get; } =
    [
        OutlineDefinition.Definition,
        ShadowDefinition.Definition,
        RimLightDefinition.Definition,
        ColorOverlayDefinition.Definition,
        InnerGlowDefinition.Definition,
        PaletteSwapDefinition.Definition,
        ColorAdjustDefinition.Definition,
        ReflectionDefinition.Definition,
        TextDefinition.Definition,
        ExplodeDefinition.Definition,
        ShineDefinition.Definition,
        SquashDefinition.Definition,
        HitFlashDefinition.Definition,
        BlinkDefinition.Definition,
        ShakeDefinition.Definition,
        BobDefinition.Definition,
        AppearDefinition.Definition,
        FadeDefinition.Definition,
        MeltDefinition.Definition,
        RotateDefinition.Definition,
        AfterimageDefinition.Definition,
        ColorCycleDefinition.Definition,
        ShockwaveDefinition.Definition,
        HologramDefinition.Definition,
    ];

    /// <summary>
    /// 등록된 모든 효과 정의: 기본 효과와 스크립트 효과(<see cref="ScriptDefinition"/> — 원문이 있어야 만들 수 있어 효과 창에서 따로 더한다).
    /// 저장·읽기·렌더링이 이 목록으로 정의를 찾는다.
    /// </summary>
    public static IReadOnlyList<EffectDefinition> All { get; } = [.. BuiltIn, ScriptDefinition.Definition];

    private static readonly Lazy<EffectDefinition[]> s_selectable = new(() => [.. BuiltIn.Where(d => OfficialEffects.IdOfType(d.TypeId) is null)]);

    /// <summary>
    /// 효과 창·AI 도구에서 새로 넣을 수 있는 C# 효과: 공식 스크립트가 없는 것(글씨). 나머지 효과는 설치된 스크립트 효과로 넣는다.
    /// </summary>
    public static IReadOnlyList<EffectDefinition> Selectable => s_selectable.Value;

    private static readonly Dictionary<string, EffectDefinition> ById = Index<string>(All, d => [d.TypeId], StringComparer.Ordinal);
    private static readonly Dictionary<string, EffectDefinition> ByLegacyId = Index<string>(All, d => d.LegacyIds, StringComparer.Ordinal);
    private static readonly Dictionary<Type, EffectDefinition> ByType = Index(All, d => [d.ModelType], EqualityComparer<Type>.Default);

    /// <summary>앱 이름이 Gpixel이던 때의 종류 ID 앞머리(<c>gpixel.effect.outline</c>). 읽을 때만 지금 앞머리로 바꿔 찾고, 쓸 때는 늘 <see cref="EffectDefinition.TypeId"/>.</summary>
    private const string FormerPrefix = "gpixel.effect.";
    private const string CurrentPrefix = "chickxel.effect.";

    /// <summary>종류 ID가 정확히 <paramref name="typeId"/>인 정의(파일 형식용, 예전 앞머리 <c>gpixel.effect.</c>도 받음, 없으면 null).</summary>
    public static EffectDefinition? Find(string? typeId)
    {
        if (typeId is null) return null;
        if (ById.TryGetValue(typeId, out var d)) return d;
        return typeId.StartsWith(FormerPrefix, StringComparison.Ordinal)
            && ById.TryGetValue(string.Concat(CurrentPrefix, typeId.AsSpan(FormerPrefix.Length)), out d) ? d : null;
    }

    /// <summary>효과 모델의 정의(모르는 효과·<see cref="MissingEffect"/>면 null).</summary>
    public static EffectDefinition? Of(LayerEffect effect) => ByType.GetValueOrDefault(effect.GetType());

    /// <summary>
    /// 효과 하나의 설정 schema: 스크립트 효과는 그 원문이 선언한 설정 뒤에 움직이는 효과 공통 설정, 다른 효과는 정의의 schema(<see cref="EffectDefinition.Parameters"/>),
    /// 모르는 효과는 빈 목록. 스크립트 원문은 <paramref name="programs"/>(해시 → 해석 결과)로 찾고, 주지 않으면 실행을 허락한 원문만(<see cref="ScriptLibrary.Runnable"/>).
    /// </summary>
    public static IReadOnlyList<EffectParameter> ParametersOf(LayerEffect effect, Func<string, ScriptEffectProgram?>? programs = null)
        => ParametersOf(effect, programs, out _);

    /// <summary>
    /// 위 <c>ParametersOf</c>와 같고, <paramref name="complete"/> = false면 schema를 다 모른다:
    /// 모르는 효과이거나, 원문을 찾지 못한 스크립트 효과(움직임 설정만 돌려줌 — 스크립트 설정 키가 맞는지는 알 수 없음).
    /// </summary>
    public static IReadOnlyList<EffectParameter> ParametersOf(LayerEffect effect, Func<string, ScriptEffectProgram?>? programs, out bool complete)
    {
        complete = false;
        if (Of(effect) is not { } definition) return [];
        if (effect is ScriptEffect script)
        {
            if ((programs ?? ScriptLibrary.Runnable)(script.ScriptHash) is not { } program) return definition.Parameters;
            complete = true;
            return ScriptEffectParameter.SchemaOf(program);
        }
        complete = true;
        return definition.Parameters;
    }

    /// <summary>효과의 종류 ID(<see cref="MissingEffect"/>면 원본의 ID).</summary>
    public static string TypeIdOf(LayerEffect effect)
        => effect is MissingEffect missing ? missing.TypeId : Of(effect)?.TypeId ?? effect.GetType().Name;

    /// <summary>
    /// 사람·AI가 적은 기본 효과 종류 이름: 앞뒤 공백과 대소문자를 무시하고 종류 ID, 없으면 그 효과의 예전 짧은 ID("outline")로 찾는다.
    /// 다른 효과로 바뀌어 읽히는 없어진 효과의 ID("gradient")와 스크립트 효과는 받지 않는다.
    /// </summary>
    public static EffectDefinition? Resolve(string name)
    {
        var id = name.Trim().ToLowerInvariant();
        foreach (var d in BuiltIn)
            if (d.TypeId.Equals(id, StringComparison.OrdinalIgnoreCase) || d.LegacyIds[0] == id)
                return d;
        return null;
    }

    /// <summary>예전 파일 형식의 짧은 종류 ID(대소문자·앞뒤 공백 무시)로 읽을 정의와 그 ID.</summary>
    internal static (EffectDefinition Definition, string LegacyId)? FindLegacy(string? id)
    {
        var key = id?.Trim().ToLowerInvariant();
        return key is not null && ByLegacyId.TryGetValue(key, out var d) ? (d, key) : null;
    }

    private static Dictionary<TKey, EffectDefinition> Index<TKey>(IReadOnlyList<EffectDefinition> all, Func<EffectDefinition, IEnumerable<TKey>> keys,
        IEqualityComparer<TKey> comparer) where TKey : notnull
    {
        var index = new Dictionary<TKey, EffectDefinition>(comparer);
        foreach (var d in all)
            foreach (var key in keys(d))
                if (!index.TryAdd(key, d))
                    throw new InvalidOperationException($"Duplicate effect registration: {key}");
        return index;
    }
}
