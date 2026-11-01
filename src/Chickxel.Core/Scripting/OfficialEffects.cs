using System.Collections.Frozen;
using Chickxel.Core.Effects;

namespace Chickxel.Core.Scripting;

/// <summary>
/// 앱에 들어 있는 공식 스크립트 효과 원문(<c>Scripting/Official/&lt;ID&gt;.js</c>): 예전 기본(C#) 효과를 JS로 옮긴 것으로, 같은 설정·그라데이션이면
/// 예전 기본 효과와 픽셀 단위로 같다(시험 <c>EffectParity</c>). ID는 예전 기본 효과 TypeId의 끝(<c>chickxel.effect.</c> 뒤, 예: <c>fade</c>, <c>coloroverlay</c>, <c>shine</c>)이고,
/// 설정 키는 그 기본 효과의 저장 키(<see cref="Effects.EffectParameter.Key"/>)와 같다.
/// <para>
/// 앱은 이 원문을 공식 패키지(<see cref="Assets.OfficialEffectPackages"/>)로 기본 설치해 효과 탭에 보이고, 예전 기본 효과가 든 파일은 읽을 때 이 원문의 스크립트 효과로
/// 바뀐다(<see cref="LegacyEffects"/>). 이 원문과 예전 앱이 내보낸 공식 원문(<see cref="ShippedFileName"/>에 적은 해시)은 묻지 않고 실행을 허락한다
/// (<see cref="ScriptLibrary.TrustOf"/>) — 공식 원문을 고친 뒤에도 예전 원문을 담은 프로젝트·레시피가 그대로 열린다.
/// </para>
/// </summary>
public static class OfficialEffects
{
    private const string Prefix = "Chickxel.Scripting.Official.", Suffix = ".js";
    private const string TypePrefix = "chickxel.effect.";

    /// <summary>
    /// 지금까지 앱에 넣어 내보낸 공식 원문의 해시 목록(<c>Scripting/Official/hashes.txt</c>, 한 줄에 "효과 ID 해시", <c>#</c> 줄은 설명). 줄을 지우지 않는다 —
    /// <c>tools/CatalogBuilder</c>가 지금 원문과 카탈로그 공식 패키지의 해시를 더하고, <c>--check</c>가 빠진 해시를 찾으면 실패한다.
    /// </summary>
    public const string ShippedFileName = "hashes.txt";

    /// <summary>들어 있는 공식 효과 ID(이름 차례).</summary>
    public static IReadOnlyList<string> Ids { get; } = typeof(OfficialEffects).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(Suffix, StringComparison.Ordinal))
        .Select(name => name[Prefix.Length..^Suffix.Length])
        .Order(StringComparer.Ordinal)
        .ToArray();

    // ID → 정리한 원문, 해시 → ID(처음 쓸 때 한 번 읽음)
    private static readonly Lazy<FrozenDictionary<string, string>> s_sources = new(() => Ids.ToFrozenDictionary(id => id, ReadSource, StringComparer.Ordinal));
    private static readonly Lazy<FrozenDictionary<string, string>> s_ids = new(() => s_sources.Value.ToFrozenDictionary(
        pair => ScriptEffectProgram.HashOf(pair.Value), pair => pair.Key, StringComparer.Ordinal));

    /// <summary>
    /// 공식 효과 ID를 효과 창에 보이는 차례로: 예전 기본 효과의 등록 차례(<see cref="EffectRegistry.BuiltIn"/> — 고정 효과 먼저, 움직이는 효과 뒤),
    /// 그 목록에 없는 공식 효과는 끝에 이름 차례.
    /// </summary>
    public static IReadOnlyList<string> Ordered => s_ordered.Value;

    private static readonly Lazy<string[]> s_ordered = new(BuildOrder);

    /// <summary>공식 효과 <paramref name="id"/>의 정리한 원문(<see cref="ScriptEffectProgram.NormalizeSource"/>). 없는 ID면 <see cref="KeyNotFoundException"/>.</summary>
    public static string Source(string id) => s_sources.Value.TryGetValue(id, out var source) ? source : throw new KeyNotFoundException(id);

    /// <summary>공식 효과 <paramref name="id"/>의 원문 해시.</summary>
    public static string HashOf(string id) => ScriptEffectProgram.HashOf(Source(id));

    /// <summary>원문 해시가 이 앱에 든 공식 효과 원문의 것이면 그 ID(아니면 null — 예전 판 원문은 <see cref="IdOfShippedHash"/>).</summary>
    public static string? IdOfHash(string? hash) => hash is not null && s_ids.Value.TryGetValue(hash, out var id) ? id : null;

    /// <summary>원문 해시가 지금이나 예전에 내보낸 공식 효과 원문의 것이면 그 ID(아니면 null). 원문은 지금 판만 있으므로 원문이 필요하면 <see cref="IdOfHash"/>.</summary>
    public static string? IdOfShippedHash(string? hash)
        => IdOfHash(hash) ?? (hash is not null && s_shipped.Value.TryGetValue(hash, out var id) ? id : null);

    /// <summary>원문 해시가 지금이나 예전에 내보낸 공식 효과 원문의 것인지(묻지 않고 실행을 허락함).</summary>
    public static bool IsOfficialHash(string? hash) => IdOfShippedHash(hash) is not null;

    /// <summary>내보낸 공식 원문 해시 목록(해시 → 효과 ID, <see cref="ShippedFileName"/>).</summary>
    public static IReadOnlyDictionary<string, string> ShippedHashes => s_shipped.Value;

    private static readonly Lazy<FrozenDictionary<string, string>> s_shipped = new(() =>
    {
        using var stream = typeof(OfficialEffects).Assembly.GetManifestResourceStream(Prefix + ShippedFileName)
                           ?? throw new InvalidOperationException(ShippedFileName);
        using var reader = new StreamReader(stream);
        return ParseShipped(reader.ReadToEnd()).ToFrozenDictionary(StringComparer.Ordinal);
    });

    /// <summary>
    /// 해시 목록 글을 읽는다(해시 → 효과 ID): 빈 줄과 <c>#</c>로 시작하는 줄은 건너뛰고, 나머지는 "ID 해시"여야 한다(ID는 소문자·숫자, 해시는
    /// <see cref="ScriptLibrary.IsHash"/> 모양). 모양이 틀린 줄이나 한 해시가 두 ID에 적힌 줄은 <see cref="FormatException"/>(줄 번호).
    /// </summary>
    public static Dictionary<string, string> ParseShipped(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts[0].All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9') || !ScriptLibrary.IsHash(parts[1])
                || (result.TryGetValue(parts[1], out var other) && other != parts[0]))
                throw new FormatException($"{ShippedFileName}:{i + 1}: {line}");
            result[parts[1]] = parts[0];
        }
        return result;
    }

    /// <summary>해시 목록 글을 쓴다(효과 ID·해시 차례, 머리 설명 포함). <see cref="ParseShipped"/>로 다시 읽힌다.</summary>
    public static string FormatShipped(IReadOnlyDictionary<string, string> shipped)
    {
        var text = new System.Text.StringBuilder();
        text.Append("# 앱이 지금까지 내보낸 공식 효과 원문의 해시(\"효과 ID SHA-256\"). 이 원문을 담은 프로젝트·레시피는 공식 원문이 바뀐 뒤에도 묻지 않고 실행한다.\n");
        text.Append("# 줄을 지우지 않는다. tools/CatalogBuilder가 새 해시를 더하고, --check는 빠진 해시가 있으면 실패한다.\n");
        foreach (var (hash, id) in shipped.OrderBy(p => p.Value, StringComparer.Ordinal).ThenBy(p => p.Key, StringComparer.Ordinal))
            text.Append(id).Append(' ').Append(hash).Append('\n');
        return text.ToString();
    }

    /// <summary>예전 기본 효과 TypeId(<c>chickxel.effect.outline</c>)에 맞는 공식 효과 ID(없으면 null).</summary>
    public static string? IdOfType(string? typeId)
        => typeId is not null && typeId.StartsWith(TypePrefix, StringComparison.Ordinal) && s_sources.Value.ContainsKey(typeId[TypePrefix.Length..])
            ? typeId[TypePrefix.Length..] : null;

    /// <summary>
    /// 사람·AI가 적은 공식 효과 이름: 앞뒤 공백·대소문자를 무시하고 ID(<c>outline</c>), 예전 기본 효과 TypeId(<c>chickxel.effect.outline</c>),
    /// 예전 짧은 ID, 공식 패키지 ID(<c>chickxel.effects.outline</c>)나 그 에셋 ID로 찾는다(없으면 null).
    /// </summary>
    public static string? Resolve(string? name)
    {
        var key = name?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(key)) return null;
        if (s_sources.Value.ContainsKey(key)) return key;
        if (IdOfType(key) is { } byType) return byType;
        if (Assets.OfficialEffectPackages.IdOfPackage(key) is { } byPackage) return byPackage;
        return EffectRegistry.Resolve(key) is { } legacy ? IdOfType(legacy.TypeId) : null;
    }

    /// <summary>공식 효과 원문을 앱(<see cref="ScriptLibrary"/>)에 알리고 해시를 돌려준다.</summary>
    public static string Register(string id) => ScriptLibrary.Add(Source(id));

    /// <summary>공식 효과 <paramref name="id"/>의 해석 결과(앱에 알리고 해석 — 해시마다 한 번). 공식 원문이 해석되지 않으면 <see cref="InvalidOperationException"/>.</summary>
    public static ScriptEffectProgram Program(string id)
    {
        var parsed = ScriptLibrary.Parse(Register(id)) ?? throw new InvalidOperationException(id);
        return parsed.Program ?? throw new InvalidOperationException($"{id}: {string.Join("; ", parsed.Errors)}");
    }

    private static string ReadSource(string id)
    {
        using var stream = typeof(OfficialEffects).Assembly.GetManifestResourceStream(Prefix + id + Suffix)
                           ?? throw new KeyNotFoundException(id);
        using var reader = new StreamReader(stream);
        return ScriptEffectProgram.NormalizeSource(reader.ReadToEnd());
    }

    private static string[] BuildOrder()
    {
        var known = Ids.ToHashSet(StringComparer.Ordinal);
        var order = EffectRegistry.BuiltIn
            .Select(d => d.TypeId.StartsWith(TypePrefix, StringComparison.Ordinal) ? d.TypeId[TypePrefix.Length..] : null)
            .Where(id => id is not null && known.Contains(id)).Select(id => id!).ToList();
        order.AddRange(Ids.Where(id => !order.Contains(id)));
        return [.. order];
    }
}
