using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Chickxel.App.Localization;

namespace Chickxel.App;

/// <summary>
/// 앱이 Core 효과 정의에 같은 TypeId로 붙이는 표시 정보의 중앙 목록: 고를 수 있는 C# 효과(<see cref="EffectRegistry.Selectable"/> — 글씨)와 스크립트 효과.
/// 효과마다 자기 패널 파일(<c>EffectPanels/XPanel.cs</c>)에 <see cref="EffectUi"/> 하나를 두고 <see cref="EffectUis"/>에 한 줄로 등록한다.
/// 예전 기본 효과는 공식 스크립트 효과로 바뀌어 읽히므로(<see cref="LegacyEffects"/>) 스크립트 설정 칸을 쓴다 — 타임라인 색만 예전 효과 색을 공식 효과 ID로 잇는다.
/// 효과 창 목록·설정 칸, 타임라인 효과 줄 색, 프리셋 매개변수 창의 효과 이름이 여기서 나온다.
/// </summary>
public sealed partial class LayerEffectsDialog
{
    /// <summary>효과 종류 하나의 앱 표시 정보: 현재 언어 이름·한 줄 설명, 타임라인 색, 설정 칸 만들기, 목록 요약 글.</summary>
    internal sealed record EffectUi(string TypeId, string Name, string Description, Avalonia.Media.Color TimelineColor,
        Action<LayerEffectsDialog, LayerEffect, Action<Func<LayerEffect, LayerEffect>>> Panel, Func<LayerEffect, string> Summary)
    {
        /// <summary>모델 타입 <typeparamref name="T"/>의 패널·요약 함수로 만든다.</summary>
        public static EffectUi For<T>(string typeId, string name, string description, Avalonia.Media.Color timelineColor,
            Action<LayerEffectsDialog, T, Action<Func<LayerEffect, LayerEffect>>> panel, Func<T, string> summary) where T : LayerEffect
            => new(typeId, name, description, timelineColor, (dialog, effect, set) => panel(dialog, (T)effect, set), effect => summary((T)effect));
    }

    /// <summary>모르는 효과(<see cref="MissingEffect"/>)의 타임라인 색.</summary>
    private static readonly Avalonia.Media.Color MissingColor = Avalonia.Media.Color.FromRgb(128, 128, 128);

    /// <summary>TypeId → 표시 정보. 고를 수 있는 C# 효과 + 스크립트 효과와 하나씩 맞아야 한다(빠지거나 남으면 처음 쓸 때 예외).</summary>
    private static readonly Dictionary<string, EffectUi> EffectUis = Index(
    [
        TextUi, ScriptUi,
    ]);

    /// <summary>공식 스크립트 효과(ID)의 타임라인 색: 예전 기본 효과 때의 색 그대로(다른 스크립트 효과는 스크립트 색).</summary>
    private static readonly Dictionary<string, Avalonia.Media.Color> OfficialColors = new(StringComparer.Ordinal)
    {
        ["outline"] = Avalonia.Media.Color.FromRgb(232, 160, 72),
        ["shadow"] = Avalonia.Media.Color.FromRgb(118, 124, 196),
        ["rimlight"] = Avalonia.Media.Color.FromRgb(226, 210, 110),
        ["coloroverlay"] = Avalonia.Media.Color.FromRgb(196, 96, 170),
        ["innerglow"] = Avalonia.Media.Color.FromRgb(250, 236, 170),
        ["paletteswap"] = Avalonia.Media.Color.FromRgb(150, 110, 230),
        ["coloradjust"] = Avalonia.Media.Color.FromRgb(160, 160, 160),
        ["reflection"] = Avalonia.Media.Color.FromRgb(70, 150, 200),
        ["explode"] = Avalonia.Media.Color.FromRgb(236, 72, 40),
        ["shine"] = Avalonia.Media.Color.FromRgb(120, 214, 236),
        ["squash"] = Avalonia.Media.Color.FromRgb(110, 200, 120),
        ["hitflash"] = Avalonia.Media.Color.FromRgb(250, 250, 250),
        ["blink"] = Avalonia.Media.Color.FromRgb(200, 170, 255),
        ["shake"] = Avalonia.Media.Color.FromRgb(186, 84, 60),
        ["bob"] = Avalonia.Media.Color.FromRgb(160, 236, 214),
        ["appear"] = Avalonia.Media.Color.FromRgb(206, 232, 86),
        ["fade"] = Avalonia.Media.Color.FromRgb(120, 194, 220),
        ["melt"] = Avalonia.Media.Color.FromRgb(160, 112, 72),
        ["rotate"] = Avalonia.Media.Color.FromRgb(70, 96, 232),
        ["afterimage"] = Avalonia.Media.Color.FromRgb(40, 190, 160),
        ["colorcycle"] = Avalonia.Media.Color.FromRgb(230, 60, 120),
        ["shockwave"] = Avalonia.Media.Color.FromRgb(255, 214, 40),
        ["hologram"] = Avalonia.Media.Color.FromRgb(0, 180, 255),
    };

    private static Dictionary<string, EffectUi> Index(EffectUi[] uis)
    {
        var index = uis.ToDictionary(ui => ui.TypeId, StringComparer.Ordinal);
        var registered = EffectRegistry.Selectable.Select(d => d.TypeId).Append(ScriptDefinition.Id).ToHashSet(StringComparer.Ordinal);
        if (index.Count != uis.Length || !registered.SetEquals(index.Keys))
            throw new InvalidOperationException("Effect editors do not match EffectRegistry.Selectable: "
                                                + string.Join(", ", registered.Except(index.Keys).Concat(index.Keys.Except(registered))));
        return index;
    }

    /// <summary>고를 수 있는 C# 효과·스크립트 효과 정의의 표시 정보.</summary>
    internal static EffectUi UiOf(EffectDefinition definition) => EffectUis[definition.TypeId];

    /// <summary>효과의 표시 정보(모르는 효과, 고를 수 없는 예전 기본 효과면 null — 읽을 때 공식 스크립트로 바뀌므로 보통 없음).</summary>
    private static EffectUi? UiOf(LayerEffect effect) => EffectRegistry.Of(effect) is { } definition ? EffectUis.GetValueOrDefault(definition.TypeId) : null;

    /// <summary>타임라인 효과 줄의 효과 종류 색(공식 스크립트 효과는 예전 기본 효과의 색).</summary>
    internal static Avalonia.Media.Color TimelineColorOf(LayerEffect effect)
        => effect is ScriptEffect script && OfficialEffects.IdOfShippedHash(script.ScriptHash) is { } official && OfficialColors.TryGetValue(official, out var color)
            ? color : UiOf(effect)?.TimelineColor ?? MissingColor;

    /// <summary>모르는 효과 설명(종류 ID 포함): 그리지 않고 고칠 수 없으며 저장하면 그대로 남는다.</summary>
    private static string MissingNote(MissingEffect missing)
        => string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.EffectMissing_Note, missing.TypeId);

    private static string Summary(LayerEffect e)
        => (UiOf(e)?.Summary(e) ?? (e is MissingEffect missing ? $"{e.DisplayName} · {missing.TypeId}" : e.DisplayName))
           + (e.Frames is { } f ? string.Format(global::System.Globalization.CultureInfo.CurrentCulture, UiStrings.S_LayerEffectsDialog_Summary_58CF9C9156, f) : "");
}
