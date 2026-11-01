using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.IO;

/// <summary>
/// 스크립트 효과 에셋(<c>effect-script</c>, 계획 13.2): JS 원문 하나 = 효과 하나. 이름·작성자·움직임·미리보기 병아리·설정 schema는 원문의 <c>effect({...})</c> 선언에서 읽고,
/// 정체는 원문 해시(<see cref="ScriptEffectProgram.HashOf"/>)다. 넣을 때는 <see cref="ToRecipe"/>로 기본 설정 효과 하나짜리 레시피가 된다.
/// </summary>
public sealed record ScriptAsset : IAssetData
{
    public string Id { get; init; } = "";
    /// <summary>선언의 <c>name</c>(이름을 바꾸려면 원문을 고친다).</summary>
    public string Name { get; init; } = "";
    public string? Author { get; init; }
    /// <summary>선언의 <c>description</c>(선택).</summary>
    public string? Description { get; init; }
    /// <summary>
    /// 올릴 때 목록 태그의 처음 값: 선언의 <c>tags</c>(<see cref="ScriptEffectProgram.Tags"/>)에, 다른 레이어를 읽는 원문(<see cref="ReadsOtherLayers"/>)이면
    /// <see cref="LayerReferenceTag"/>를 끝에 더한 것(이미 있으면 그대로).
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>다른 레이어를 읽는 스크립트에 늘 붙는 목록 태그(<see cref="AssetPublishCheck"/>가 빠졌으면 막는다).</summary>
    public const string LayerReferenceTag = "layer-reference";
    /// <summary>원문이 다른 레이어 함수(<see cref="ScriptInfo.OtherLayerNames"/>)를 쓰는지(<see cref="ScriptInfo.ReadsOtherLayers"/>).</summary>
    public bool ReadsOtherLayers { get; init; }
    /// <summary>효과 만들기의 미리보기 장면(병아리 둘레 레이어). 없으면 null — 병아리만 놓고 그린다.</summary>
    public PreviewScene? Scene { get; init; }
    /// <summary>정리한 원문(<see cref="ScriptEffectProgram.NormalizeSource"/>).</summary>
    public string Source { get; init; } = "";
    public string Hash { get; init; } = "";
    public bool Animated { get; init; }
    /// <summary>선언의 <c>easing</c>: 넣는 효과의 빠르기 곡선 처음 값(<see cref="ScriptEffectProgram.Easing"/>).</summary>
    public EffectEasing Easing { get; init; } = EffectEasing.Linear;
    /// <summary>선언의 <c>previewChick</c>: 패키지 미리보기 병아리(<see cref="PreviewChicks"/>의 ID).</summary>
    public string PreviewChick { get; init; } = PreviewChicks.Default;
    /// <summary>선언의 <c>previewFrames</c>: 패키지 미리보기에서 효과를 돌리는 칸 수(<see cref="ScriptEffectProgram.PreviewFrames"/>).</summary>
    public int PreviewFrames { get; init; } = AssetPreview.PackageFrames;
    /// <summary>선언의 설정 schema(선언 순서).</summary>
    public IReadOnlyList<ScriptSetting> Settings { get; init; } = [];
    /// <summary>선언의 <c>translations</c>(<see cref="ScriptEffectProgram.Translations"/>). 화면에는 <see cref="NameFor"/>·<see cref="DescriptionFor"/>로 보인다.</summary>
    public IReadOnlyDictionary<string, ScriptTranslation> Translations { get; init; } = ScriptTranslations.Empty;
    /// <summary>선언의 <c>language</c>(<see cref="ScriptEffectProgram.Language"/>, 없으면 null): 선언 값의 언어.</summary>
    public string? Language { get; init; }

    /// <summary><paramref name="culture"/>(보통 화면 언어)의 제목(번역 → 선언의 <see cref="Name"/>). 정체·중복 판정에는 <see cref="Name"/>을 쓴다.</summary>
    public string NameFor(CultureInfo culture) => ScriptTranslations.NameFor(Translations, Name, culture, Language);

    /// <summary><paramref name="culture"/>의 설명(번역 → 선언의 <see cref="Description"/>).</summary>
    public string? DescriptionFor(CultureInfo culture) => ScriptTranslations.DescriptionFor(Translations, Description, culture, Language);

    /// <summary><paramref name="culture"/>의 설정 이름(번역 → 선언의 <c>label</c>).</summary>
    public string LabelFor(ScriptSetting setting, CultureInfo culture) => ScriptTranslations.LabelFor(Translations, setting, culture, Language);

    /// <summary>원문으로 만든 값(이름·작성자·schema는 <paramref name="program"/>의 선언, 장면은 없음).</summary>
    public static ScriptAsset From(string id, ScriptEffectProgram program)
    {
        bool layers = ScriptLibrary.InfoOf(ScriptLibrary.Add(program.Source)) is { ReadsOtherLayers: true };
        return new()
        {
            Id = id, Name = program.Name.Trim(), Author = program.Author, Description = program.Description,
            Tags = layers && !program.Tags.Contains(LayerReferenceTag) ? [.. program.Tags, LayerReferenceTag] : program.Tags, ReadsOtherLayers = layers,
            Source = program.Source, Hash = program.ContentHash, Animated = program.Animated, Easing = program.Easing, PreviewChick = program.PreviewChick,
            PreviewFrames = program.PreviewFrames, Settings = program.Settings, Translations = program.Translations, Language = program.Language,
        };
    }

    /// <summary>
    /// 프로젝트에 넣을 레시피: 선언의 기본값으로 둔 스크립트 효과 하나와 그 원문. 기본값은 해석해 둔 schema에서 바로 만들므로
    /// 실행을 허락하지 않은 원문도 허락한 뒤와 같은 효과 값이 된다.
    /// </summary>
    public EffectRecipe ToRecipe() => new()
    {
        Id = Id,
        Name = Name,
        Effects = [new ScriptEffect
        {
            ScriptHash = Hash, Settings = ScriptSettingValues.From(Settings.Select(s => new KeyValuePair<string, object>(s.Key, s.Default))), Easing = Easing,
        }],
        Scripts = new SortedDictionary<string, string>(StringComparer.Ordinal) { [Hash] = Source },
    };
}

/// <summary>
/// 스크립트 원문 읽기와 검사. 패키지 파일 <c>assets/scripts/&lt;이름&gt;.js</c>는 UTF-8 원문 그대로이고(ID는 manifest가 붙임),
/// 개인 에셋 파일에는 <code>{ "schemaVersion": 1, "id": "personal.…", "source": "…" }</code>로 둔다.
/// <para>
/// 검사(<see cref="Check"/>)는 크기(<see cref="AssetPackageLimits.MaxScriptBytes"/>)를 보고 <see cref="ScriptEffectProgram.Parse"/>로 해석한다.
/// 해석은 선언을 읽으려고 원문의 맨 위를 한 번 실행한다 — 실행은 앱 렌더링과 같은 샌드박스(.NET·파일·네트워크 접근 없음, <c>eval</c>·<c>Date</c>·
/// <c>Math.random</c> 없음)에서 렌더링보다 훨씬 좁은 <see cref="CheckLimits"/> 안에서만 하고, 신뢰 결정(<see cref="ScriptLibrary.TrustOf"/>)과 무관하다.
/// 맨 위에서 오래 돌거나 메모리를 많이 쓰는 원문은 그 한도에 걸려 설치 전에 거부된다. <c>render</c>·<c>padding</c>은 부르지 않는다.
/// </para>
/// </summary>
public static class ScriptAssetFormat
{
    public const int SchemaVersion = 1;

    /// <summary>
    /// 설치 전 해석(맨 위 한 번)의 한도. 선언만 하는 정상 원문(빛 지나가기: 문장 수십 개)의 수만 배 여유이고 렌더링 한도(<see cref="ScriptLimits.Default"/>)보다
    /// 문장 50배·메모리 16배 좁다. 시간 한도는 기계마다 다른 지점에서 멈추지 않도록 문장 한도보다 늦게 걸리게 둔다.
    /// </summary>
    public static ScriptLimits CheckLimits { get; } = new()
    {
        Timeout = TimeSpan.FromSeconds(1), MaxStatements = 1_000_000, MemoryBytes = 16L << 20, MaxRecursion = 64,
    };

    /// <summary>해시별 검사 결과(설치된 패키지를 다시 읽을 때마다 해석하지 않게). 원문이 같으면 결과도 같다.</summary>
    private static readonly ConcurrentDictionary<string, ScriptParseResult> s_checked = new(StringComparer.Ordinal);
    private const int MaxChecked = 256;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly HashSet<string> PersonalKeys = ["schemaVersion", "id", "source"];

    /// <summary>
    /// 원문 하나를 검사한다: UTF-8 크기 한도, 해석(문법·선언, 맨 위 실행의 한도). 문제는 <paramref name="field"/>를 붙여 <paramref name="context"/>에 남기고 null.
    /// </summary>
    public static ScriptEffectProgram? Check(string source, string field, AssetReadContext context)
    {
        if (Encoding.UTF8.GetByteCount(source) > context.Limits.MaxScriptBytes)
        {
            context.Add(AssetIssueCode.StringTooLong, $"{field}: {context.Limits.MaxScriptBytes.ToString(CultureInfo.InvariantCulture)}");
            return null;
        }
        var text = ScriptEffectProgram.NormalizeSource(source);
        var hash = ScriptEffectProgram.HashOf(text);
        if (!s_checked.TryGetValue(hash, out var parsed))
        {
            parsed = ScriptEffectProgram.Parse(text, CheckLimits);
            if (s_checked.Count >= MaxChecked) s_checked.Clear();
            s_checked[hash] = parsed;
        }
        if (parsed.Program is { } program) return program;
        foreach (var error in parsed.Errors)
            context.Add(AssetIssueCode.InvalidScript, string.Create(CultureInfo.InvariantCulture, $"{field}:{error.Line}:{error.Column}: {error.Message}"));
        return null;
    }

    /// <summary>패키지 파일(UTF-8 JS, BOM 허용)을 읽는다. ID는 <see cref="AssetReadContext.AssetId"/>.</summary>
    public static ScriptAsset? Read(ReadOnlyMemory<byte> data, AssetReadContext context)
    {
        string source;
        try
        {
            source = StrictUtf8.GetString(data.Span);
        }
        catch (DecoderFallbackException)
        {
            context.Add(AssetIssueCode.InvalidScript, "UTF-8");
            return null;
        }
        return FromSource(source, "source", context);
    }

    public static ReadOnlyMemory<byte> Write(ScriptAsset asset) => Encoding.UTF8.GetBytes(asset.Source);

    /// <summary>개인 에셋 파일의 한 줄.</summary>
    public static JsonObject WritePersonal(ScriptAsset asset, string id) => new()
    {
        ["schemaVersion"] = SchemaVersion,
        ["id"] = id,
        ["source"] = asset.Source,
    };

    public static ScriptAsset? ReadPersonal(JsonObject json, AssetReadContext context)
    {
        if (!AssetJson.TryInt(json["schemaVersion"], out int schema) || schema < 1)
        {
            context.Bad("schemaVersion");
            return null;
        }
        if (schema > SchemaVersion)
        {
            context.Add(AssetIssueCode.FutureSchema, schema.ToString(CultureInfo.InvariantCulture));
            return null;
        }
        if (context.Strict)
            foreach (var (key, _) in json)
                if (!PersonalKeys.Contains(key)) context.Bad(key);
        if (AssetJson.String(json["source"]) is not { } source)
        {
            context.Bad("source");
            return null;
        }
        return FromSource(source, "source", context);
    }

    private static ScriptAsset? FromSource(string source, string field, AssetReadContext context)
    {
        int before = context.Issues.Count;
        if (Check(source, field, context) is not { } program) return null;
        if (!AssetManifest.IsValidName(program.Name)) context.Bad("name");
        if (context.Issues.Count > before) return null;
        // 렌더러·효과 이름이 해시로 찾게 원문을 앱에 알린다(실행하지 않음 — 실행 허락은 따로).
        ScriptLibrary.Add(program.Source);
        return ScriptAsset.From(context.AssetId, program);
    }
}
