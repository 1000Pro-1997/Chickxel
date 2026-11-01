using System.Text.Json.Nodes;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Effects;

/// <summary>
/// 스크립트 효과 정의(13.5). 저장 JSON은 다른 효과와 같은 모양에 원문 해시가 붙는다:
/// <code>{ "type": "chickxel.effect.script", "version": 1, "enabled": true, "clipToPrevious": false,
///   "settings": { "angle": 45, "color": "#FFFFFF", …, "easing": "Linear", "repeat": "Once", "repeatCount": 1 }, "script": "&lt;SHA-256&gt;" }</code>
/// 설정은 스크립트 설정(키 순서) 뒤에 움직이는 효과 공통 설정이 붙는다. 실행할 수 있는 원문을 알면 읽을 때 schema에 맞추고,
/// 모르면(원문이 없거나 아직 허락하지 않음) 값을 그대로 보존한다. 해시가 틀린 모양이면 <see cref="MissingEffect"/>로 원본을 보존한다.
/// </summary>
public static class ScriptDefinition
{
    public const string Id = "chickxel.effect.script";

    /// <summary>효과 JSON에서 원문 해시를 담는 맨 위 키.</summary>
    public const string ScriptKey = "script";

    public static EffectDefinition Definition { get; } = EffectDefinition.CreateScripted<ScriptEffect>(Id,
        (image, effect, source, hooks) =>
        {
            // 실패한 실행의 결과(효과를 건너뛴 그림)는 중간 결과·잔상 캐시에 담지 않게 알린다
            if (!ScriptEffectRuntime.Render(image, effect, source, hooks?.Owner, hooks?.Cancellation ?? CancellationToken.None)) hooks?.ReportScriptFailure();
        },
        Padding, UsesOtherFrames, ClipsBySource, ErasesSource, new EffectCodecHooks(WriteSettings, WriteRoot, Read));

    // 여백 함수가 실패하면 넓히지 않는다 — 실패는 기억해 두고 렌더러가 그 효과의 실패로 센다(ScriptEffectRuntime.Render)
    private static EffectPadding Padding(ScriptEffect effect, int width, int height)
        => ScriptLibrary.Runnable(effect.ScriptHash) is { } program ? ScriptLibrary.Padding(effect.ScriptHash, program, effect.Settings, width, height) : default;

    // 실행하는 스크립트가 ctx.frameSource나 ctx.renderedFrameSource를 쓰면 다른 프레임 원본을 넘긴다(원문에 그 이름이 나오는지로 정함)
    private static bool UsesOtherFrames(ScriptEffect effect)
        => ScriptLibrary.TrustOf(effect.ScriptHash) == ScriptTrust.Trusted
           && ScriptLibrary.InfoOf(effect.ScriptHash) is { } info && (info.ReadsOtherFrames || info.ReadsRenderedFrames);

    // 클리핑 방식과 원본 지우기는 선언(clipsBySource·erasesSource)을 따른다. 실행할 수 없는 원문은 그리지 않으므로 기본 방식
    private static bool ClipsBySource(ScriptEffect effect) => ScriptLibrary.Runnable(effect.ScriptHash) is { ClipsBySource: true };

    private static bool ErasesSource(ScriptEffect effect)
        => ScriptLibrary.Runnable(effect.ScriptHash) is { } program && program.ErasesSourceWith(effect.Settings);

    /// <summary>
    /// 실행할 수 있는 스크립트가 <c>deferred: true</c>를 선언했는지 — 다른 효과를 모두 입힌 뒤에 그린다(<see cref="LayerEffects.IsDeferred"/>).
    /// 선언을 알아야 하므로 실행할 수 있는 원문만 해석 결과로 본다(아니면 그리지 않는 효과라 차례가 상관없음).
    /// </summary>
    public static bool IsDeferred(ScriptEffect effect) => ScriptLibrary.Runnable(effect.ScriptHash) is { Deferred: true };

    /// <summary>실행하는 스크립트가 <c>ctx.renderedFrameSource</c>를 쓰는지 — 미루는 스크립트면 다른 프레임에 다른 효과를 입힌 그림을 넘긴다.</summary>
    internal static bool ReadsRenderedFrames(ScriptEffect effect)
        => ScriptLibrary.TrustOf(effect.ScriptHash) == ScriptTrust.Trusted && ScriptLibrary.InfoOf(effect.ScriptHash) is { ReadsRenderedFrames: true };

    /// <summary>실행하는 스크립트가 다른 레이어(<c>ctx.layer</c> 등)나 <c>ctx.slice</c>를 읽는지 — 그릴 때 <see cref="ScriptLayerContext"/>를 넘긴다.</summary>
    public static bool UsesOtherLayers(ScriptEffect effect)
        => ScriptLibrary.TrustOf(effect.ScriptHash) == ScriptTrust.Trusted
           && ScriptLibrary.InfoOf(effect.ScriptHash) is { } info && (info.ReadsOtherLayers || info.ReadsSlice);

    /// <summary>실행하는 스크립트가 다른 레이어 함수(<c>ctx.layer</c>·<c>ctx.layerWithEffects</c>·<c>ctx.layerEffects</c>)를 쓰는지.</summary>
    internal static bool ReadsOtherLayers(ScriptEffect effect)
        => ScriptLibrary.TrustOf(effect.ScriptHash) == ScriptTrust.Trusted && ScriptLibrary.InfoOf(effect.ScriptHash) is { ReadsOtherLayers: true };

    /// <summary>실행하는 스크립트가 다른 레이어를 효과까지 입혀 읽는지(<c>ctx.layerWithEffects</c>).</summary>
    internal static bool ReadsRenderedLayers(ScriptEffect effect)
        => ScriptLibrary.TrustOf(effect.ScriptHash) == ScriptTrust.Trusted && ScriptLibrary.InfoOf(effect.ScriptHash) is { ReadsRenderedLayers: true };

    private static JsonObject WriteSettings(LayerEffect effect) => ((ScriptEffect)effect).Settings.ToJson();

    private static void WriteRoot(LayerEffect effect, JsonObject root) => root[ScriptKey] = ((ScriptEffect)effect).ScriptHash;

    private static LayerEffect? Read(LayerEffect effect, JsonObject settings, JsonObject? root)
    {
        var script = (ScriptEffect)effect;
        var hash = script.ScriptHash;
        if (root is not null)
        {
            hash = EffectJson.String(root[ScriptKey]) ?? "";
            if (!ScriptLibrary.IsHash(hash)) return null;
        }
        var values = ScriptSettingValues.FromJson(settings, key => !ScriptEffectProgram.ReservedSettingKeys.Contains(key));
        if (ScriptLibrary.Runnable(hash) is { } program) values = ScriptSettingValues.Normalized(program, values);
        return script with { ScriptHash = hash, Settings = values };
    }
}
