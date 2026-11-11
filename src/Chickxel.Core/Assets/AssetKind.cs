using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.IO;

namespace Chickxel.Core.Assets;

/// <summary>패키지 안 에셋 하나의 읽은 값(효과 레시피, 그라데이션 …). ID는 manifest의 에셋 ID와 같다.</summary>
public interface IAssetData
{
    string Id { get; }
    string Name { get; }
}

/// <summary>
/// 에셋 종류 하나: manifest의 <c>kind</c> 값, 패키지 안 폴더와 허용 확장자, 파일을 읽고 검사하는 함수.
/// 새 종류는 <see cref="AssetKinds.All"/>에 정의 한 줄을 더한다 — 검사기·저장소·카탈로그는 이 목록만 본다.
/// 개인 에셋 파일(<c>personal-assets.json</c>)의 <c>data</c>는 JSON 객체라, 파일이 JSON이 아닌 종류는 <paramref name="readPersonal"/>·<paramref name="writePersonal"/>로
/// 따로 쓴다(없으면 패키지 파일 JSON 그대로). <paramref name="minAppVersion"/>은 이 종류를 처음 읽는 앱 버전 — 이 종류가 든 패키지는 manifest의
/// 최소 앱 버전이 그보다 낮지 않게 쓴다(<see cref="AssetPackageWriter.Write"/>).
/// </summary>
public sealed class AssetKindDefinition(string kind, string folder, IReadOnlyList<string> extensions,
    Func<ReadOnlyMemory<byte>, AssetReadContext, IAssetData?> read, Func<IAssetData, string, ReadOnlyMemory<byte>> write,
    Func<JsonObject, AssetReadContext, IAssetData?>? readPersonal = null, Func<IAssetData, string, JsonObject>? writePersonal = null,
    string minAppVersion = "0.0.0")
{
    /// <summary>manifest <c>kind</c> 값(영구).</summary>
    public string Kind { get; } = kind;

    /// <summary>이 종류를 처음 읽는 앱 버전(이 종류가 든 패키지의 최소 앱 버전 하한).</summary>
    public string MinAppVersion { get; } = minAppVersion;

    /// <summary>패키지 안 폴더(끝에 '/').</summary>
    public string Folder { get; } = folder;

    /// <summary>허용 확장자(소문자, 점 포함).</summary>
    public IReadOnlyList<string> Extensions { get; } = extensions;

    /// <summary>파일 내용을 읽고 검사한다. 문제가 있으면 context에 남기고 null.</summary>
    public IAssetData? Read(ReadOnlyMemory<byte> data, AssetReadContext context) => read(data, context);

    /// <summary>값을 패키지 파일 내용으로 쓴다(<paramref name="id"/> = 에셋 ID).</summary>
    public ReadOnlyMemory<byte> Write(IAssetData value, string id) => write(value, id);

    /// <summary>개인 에셋 파일의 <c>data</c> 객체를 읽는다.</summary>
    public IAssetData? ReadPersonal(JsonObject json, AssetReadContext context)
        => readPersonal is not null ? readPersonal(json, context) : Read(Encoding.UTF8.GetBytes(json.ToJsonString()), context);

    /// <summary>개인 에셋 파일의 <c>data</c> 객체.</summary>
    public JsonObject WritePersonal(IAssetData value, string id)
        => writePersonal is not null ? writePersonal(value, id) : JsonNode.Parse(Write(value, id).Span)!.AsObject();

    /// <summary>이 종류의 파일이 놓일 수 있는 경로인지(폴더 아래, 허용 확장자).</summary>
    public bool Accepts(string path)
        => path.StartsWith(Folder, StringComparison.Ordinal) && path.Length > Folder.Length
           && Extensions.Any(ext => path.EndsWith(ext, StringComparison.Ordinal));
}

/// <summary>등록된 에셋 종류.</summary>
public static class AssetKinds
{
    public const string EffectRecipe = "effect-recipe";
    public const string Gradient = "gradient";
    /// <summary>JS 스크립트 효과 하나(<c>assets/scripts/*.js</c>, <see cref="ScriptAssetFormat"/>).</summary>
    public const string EffectScript = "effect-script";
    /// <summary>모양 펜 세트 하나(<c>assets/shapes/*.json</c>, <see cref="ShapeSetAssetFormat"/>). 앱 1.14.120부터 읽는다.</summary>
    public const string ShapeSet = "shape-set";

    /// <summary>모양 펜 세트를 처음 읽는 앱 버전.</summary>
    public const string ShapeSetMinAppVersion = "1.14.120";

    public static IReadOnlyList<AssetKindDefinition> All { get; } =
    [
        new(EffectRecipe, "assets/effects/", [".json"],
            (data, context) => AssetJson.Parse(data.Span, context.Path, context.Limits.MaxJsonDepth, context.Limits.MaxStringLength, context.Issues,
                    (EffectRecipeFormat.ScriptsKey, context.Limits.MaxScriptBytes)) is { } node
                ? EffectRecipeFormat.Read(node, context) : null,
            (value, id) => AssetJson.ToUtf8(EffectRecipeFormat.Write((IO.EffectRecipe)value with { Id = id }))),
        new(Gradient, "assets/gradients/", [".json"],
            (data, context) => AssetJson.Parse(data.Span, context.Path, context.Limits.MaxJsonDepth, context.Limits.MaxStringLength, context.Issues) is { } node
                ? GradientAssetFormat.Read(node, context) : null,
            (value, id) => AssetJson.ToUtf8(GradientAssetFormat.Write(id, ((GradientAsset)value).Gradient))),
        new(EffectScript, "assets/scripts/", [".js"],
            ScriptAssetFormat.Read,
            (value, _) => ScriptAssetFormat.Write((ScriptAsset)value),
            ScriptAssetFormat.ReadPersonal,
            (value, id) => ScriptAssetFormat.WritePersonal((ScriptAsset)value, id)),
        new(ShapeSet, "assets/shapes/", [".json"],
            ShapeSetAssetFormat.Read,
            (value, _) => ShapeSetAssetFormat.Write(((ShapeSetAsset)value).Set),
            minAppVersion: ShapeSetMinAppVersion),
    ];

    public static AssetKindDefinition? Find(string? kind) => All.FirstOrDefault(definition => definition.Kind == kind);

    /// <summary>
    /// <paramref name="manifest"/>의 에셋 종류가 요구하는 최소 앱 버전을 반영한 최소 앱 버전: manifest의 값과 종류마다 <see cref="AssetKindDefinition.MinAppVersion"/> 중 가장 높은 것.
    /// </summary>
    public static string RequiredAppVersion(AssetManifest manifest)
    {
        string result = manifest.MinAppVersion;
        SemVersion.TryParse(result, out var best);
        foreach (var entry in manifest.Assets)
            if (Find(entry.Kind) is { } definition && SemVersion.TryParse(definition.MinAppVersion, out var required) && required > best)
                (best, result) = (required, definition.MinAppVersion);
        return result;
    }
}
