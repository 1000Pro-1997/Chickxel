using System.Text.RegularExpressions;
using Acornima.Ast;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using Jint.Runtime;

namespace Chickxel.Core.Scripting;

/// <summary>
/// <c>effect({...})</c> 선언을 읽고 검사한다(13.2). 잘못된 곳을 모두 모아 위치와 함께 돌려준다.
/// 고칠 곳을 한 번에 보여 주려고 첫 오류에서 멈추지 않는다.
/// </summary>
internal static partial class ScriptDeclarationReader
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex SettingKey();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ChickId();

    public static ScriptParseResult Read(ScriptSandbox sandbox, Prepared<Script> script, string source, string hash)
    {
        var declaration = sandbox.Declare(script);
        var errors = new List<ScriptError>();
        void Fail(string message, params object[] path)
        {
            var (line, column) = ScriptSourceLocator.Declaration(script.Program!, path);
            errors.Add(new(ScriptErrorKind.Declaration, message, line, column));
        }

        var nameValue = declaration.Get("name");
        string name = nameValue is JsString ? nameValue.AsString().Trim() : "";
        if (name.Length == 0) Fail(DomainStrings.Script_InvalidName, "name");

        var authorValue = declaration.Get("author");
        string? author = authorValue is JsString && authorValue.AsString().Trim() is { Length: > 0 } a ? a : null;
        if (!authorValue.IsUndefined() && authorValue is not JsString) Fail(DomainStrings.Script_InvalidAuthor, "author");

        var descriptionValue = declaration.Get("description");
        string? description = descriptionValue is JsString && descriptionValue.AsString().Trim() is { Length: > 0 } d ? d : null;
        if ((!descriptionValue.IsUndefined() && descriptionValue is not JsString) || description?.Length > ScriptEffectProgram.MaxDescriptionLength)
            Fail(DomainStrings.FormatScript_InvalidDescription(max: ScriptEffectProgram.MaxDescriptionLength), "description");

        var animatedValue = declaration.Get("animated");
        bool animated = animatedValue.IsBoolean() && animatedValue.AsBoolean();
        if (!animatedValue.IsUndefined() && !animatedValue.IsBoolean()) Fail(DomainStrings.Script_InvalidAnimated, "animated");

        var chickValue = declaration.Get("previewChick");
        string? previewChick = chickValue is JsString && chickValue.AsString() is { Length: > 0 and <= 32 } chick && ChickId().IsMatch(chick) ? chick : null;
        if (!chickValue.IsUndefined() && previewChick is null) Fail(DomainStrings.Script_InvalidPreviewChick, "previewChick");

        // 여러 칸에 걸쳐 그림을 바꾸는 효과의 끝 그림 병아리(미리보기 마지막 칸, 없으면 null)
        var endChickValue = declaration.Get("previewEndChick");
        string? previewEndChick = endChickValue is JsString && endChickValue.AsString() is { Length: > 0 and <= 32 } endChick && ChickId().IsMatch(endChick) ? endChick : null;
        if (!endChickValue.IsUndefined() && previewEndChick is null) Fail(DomainStrings.Script_InvalidPreviewEndChick, "previewEndChick");

        var framesValue = declaration.Get("previewFrames");
        int previewFrames = AssetPreview.PackageFrames;
        if (!framesValue.IsUndefined())
        {
            if (Whole(framesValue) is { } f and >= ScriptEffectProgram.MinPreviewFrames and <= ScriptEffectProgram.MaxPreviewFrames) previewFrames = (int)f;
            else Fail(DomainStrings.FormatScript_InvalidPreviewFrames(min: ScriptEffectProgram.MinPreviewFrames, max: ScriptEffectProgram.MaxPreviewFrames), "previewFrames");
        }

        // 미리보기 칸마다 병아리를 옮기는 양(없으면 움직이지 않음)
        var previewMotionValue = declaration.Get("previewMotion");
        var previewMotion = PreviewMotion.None;
        if (!previewMotionValue.IsUndefined())
        {
            if (ReadPreviewMotion(previewMotionValue) is { } m) previewMotion = m;
            else Fail(DomainStrings.FormatScript_InvalidPreviewMotion(max: PreviewMotion.MaxOffset, frames: ScriptEffectProgram.MaxPreviewFrames), "previewMotion");
        }

        // 미리보기 자리·피벗: 끝 그림 병아리 자리, 시작·끝 칸 피벗을 기본 자리에서 옮기는 [x, y](없으면 0)
        System.Drawing.Point Offset(string key)
        {
            var value = declaration.Get(key);
            if (value.IsUndefined()) return default;
            if (ReadOffset(value) is { } offset) return offset;
            Fail(DomainStrings.FormatScript_InvalidPreviewOffset(key: key, max: PreviewPlacement.MaxOffset), key);
            return default;
        }
        var previewPlacement = new PreviewPlacement(Offset(PreviewPlacement.EndOffsetKey), Offset(PreviewPlacement.PivotKey), Offset(PreviewPlacement.EndPivotKey));

        var areaValue = declaration.Get("area");
        var area = ScriptEffectArea.Global;
        if (areaValue is JsString && areaValue.AsString() == "local") area = ScriptEffectArea.Local;
        else if (!areaValue.IsUndefined() && !(areaValue is JsString && areaValue.AsString() == "global")) Fail(DomainStrings.Script_InvalidArea, "area");
        int radius = 0;
        if (area == ScriptEffectArea.Local)
        {
            if (Whole(declaration.Get("radius")) is { } r and >= 0 and <= ScriptEffectProgram.MaxPadding) radius = (int)r;
            else Fail(DomainStrings.FormatScript_InvalidRadius(max: ScriptEffectProgram.MaxPadding), "radius");
        }

        if (!declaration.Get("render").IsCallable()) Fail(DomainStrings.Script_MissingRender, "render");

        var paddingValue = declaration.Get("padding");
        EffectPadding? fixedPadding = paddingValue.IsUndefined() ? default(EffectPadding) : null;
        if (paddingValue is ArrayInstance)
        {
            fixedPadding = ReadPadding(paddingValue);
            if (fixedPadding is null) Fail(DomainStrings.FormatScript_InvalidPadding(max: ScriptEffectProgram.MaxPadding), "padding");
        }
        else if (!paddingValue.IsUndefined() && !paddingValue.IsCallable())
        {
            Fail(DomainStrings.FormatScript_InvalidPadding(max: ScriptEffectProgram.MaxPadding), "padding");
        }

        var settings = new List<ScriptSetting>();
        var settingsValue = declaration.Get("settings");
        if (settingsValue is ArrayInstance list)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (uint i = 0, count = ScriptHostApi.Length(list); i < count; i++)
                if (ReadSetting(list[i], (int)i, keys, Fail) is { } setting) settings.Add(setting);
        }
        else if (!settingsValue.IsUndefined())
        {
            Fail(DomainStrings.Script_InvalidSettings, "settings");
        }
        // 점 고르기의 Y 설정: 다른 int 설정이고, 그 자신은 모양을 정하지 않았고, 두 X가 같은 Y를 나눠 갖지 않는다
        for (int i = 0; i < settings.Count; i++)
        {
            if (settings[i].Pair is not { } pairKey) continue;
            var y = settings.FirstOrDefault(s => s.Key == pairKey);
            if (y is not { Type: ScriptSettingType.Int, Widget: ScriptSettingWidget.None } || settings.Count(s => s.Pair == pairKey) > 1)
                Fail(DomainStrings.FormatScript_InvalidSettingWidget(key: settings[i].Key), "settings", i, "pair");
        }

        // 보이는 조건: 앞에 선언한 고르기·참거짓 설정의 있는 값만(자기 자신·뒤 설정을 가리키면 돌 수 있어 막는다)
        for (int i = 0; i < settings.Count; i++)
            foreach (var condition in settings[i].When)
            {
                int at = settings.FindIndex(s => s.Key == condition.Key);
                var target = at >= 0 && at < i ? settings[at] : null;
                bool ok = target switch
                {
                    { Type: ScriptSettingType.Choice } choice => condition.Values.All(v => v is string text && choice.Choices.Any(c => c.Value == text)),
                    { Type: ScriptSettingType.Bool } => condition.Values.All(v => v is bool),
                    _ => false,
                };
                if (!ok) Fail(DomainStrings.FormatScript_InvalidSettingWhen(key: settings[i].Key), "settings", i, "when");
            }

        var deferredValue = declaration.Get("deferred");
        bool deferred = deferredValue.IsBoolean() && deferredValue.AsBoolean();
        if (!deferredValue.IsUndefined() && !deferredValue.IsBoolean()) Fail(DomainStrings.Script_InvalidDeferred, "deferred");

        // motion: false = 빠르기 곡선·반복을 쓰지 않는 움직이는 효과(편집기에서 숨김, 저장 값은 그대로)
        var motionValue = declaration.Get("motion");
        bool motion = !motionValue.IsBoolean() || motionValue.AsBoolean();
        if (!motionValue.IsUndefined() && !motionValue.IsBoolean()) Fail(DomainStrings.Script_InvalidMotion, "motion");

        var clipsValue = declaration.Get("clipsBySource");
        bool clipsBySource = clipsValue.IsBoolean() && clipsValue.AsBoolean();
        if (!clipsValue.IsUndefined() && !clipsValue.IsBoolean()) Fail(DomainStrings.Script_InvalidClipsBySource, "clipsBySource");

        // 원본 지우기: true(늘) 또는 bool 설정 키(그 설정이 참일 때)
        var erasesValue = declaration.Get("erasesSource");
        bool erasesSource = erasesValue.IsBoolean() && erasesValue.AsBoolean();
        string? erasesSourceSetting = null;
        if (erasesValue is JsString erasesKey && settings.Any(s => s.Key == erasesKey.ToString() && s.Type == ScriptSettingType.Bool))
            erasesSourceSetting = erasesKey.ToString();
        else if (!erasesValue.IsUndefined() && !erasesValue.IsBoolean()) Fail(DomainStrings.Script_InvalidErasesSource, "erasesSource");

        var easingValue = declaration.Get("easing");
        var easing = EffectEasing.Linear;
        if (!easingValue.IsUndefined())
        {
            if (easingValue is JsString && Enum.TryParse<EffectEasing>(easingValue.AsString(), ignoreCase: false, out var parsedEasing)
                && Enum.IsDefined(parsedEasing) && easingValue.AsString() == parsedEasing.ToString())
                easing = parsedEasing;
            else Fail(DomainStrings.FormatScript_InvalidEasing(names: string.Join(", ", Enum.GetNames<EffectEasing>())), "easing");
        }

        // 새로 넣는 효과의 반복·반복 횟수·피벗 같이 옮기기 처음 값
        var repeatValue = declaration.Get("repeat");
        var repeat = EffectRepeat.Once;
        if (!repeatValue.IsUndefined())
        {
            if (repeatValue is JsString && Enum.TryParse<EffectRepeat>(repeatValue.AsString(), ignoreCase: false, out var parsedRepeat)
                && Enum.IsDefined(parsedRepeat) && repeatValue.AsString() == parsedRepeat.ToString())
                repeat = parsedRepeat;
            else Fail(DomainStrings.FormatScript_InvalidRepeat(names: string.Join(", ", Enum.GetNames<EffectRepeat>())), "repeat");
        }
        var repeatCountValue = declaration.Get("repeatCount");
        int repeatCount = 1;
        if (!repeatCountValue.IsUndefined())
        {
            if (Whole(repeatCountValue) is { } count and >= 1 and <= AnimatedEffect.MaxRepeatCount) repeatCount = (int)count;
            else Fail(DomainStrings.FormatScript_InvalidRepeatCount(max: AnimatedEffect.MaxRepeatCount), "repeatCount");
        }
        var movePivotValue = declaration.Get("movePivot");
        bool movePivot = !movePivotValue.IsBoolean() || movePivotValue.AsBoolean();
        if (!movePivotValue.IsUndefined() && !movePivotValue.IsBoolean()) Fail(DomainStrings.Script_InvalidMovePivot, "movePivot");

        var translations = ReadTranslations(declaration.Get("translations"), settings, Fail);
        var tags = ReadTags(declaration.Get("tags"), Fail);

        // language: 선언에 적은 이름·설명·설정 이름의 언어(번역 키와 같은 언어 코드 모양)
        var languageValue = declaration.Get("language");
        string? language = languageValue is JsString && ScriptTranslations.IsLanguageCode(languageValue.AsString()) ? languageValue.AsString() : null;
        if (!languageValue.IsUndefined() && language is null) Fail(DomainStrings.Script_InvalidLanguage, "language");

        return errors.Count > 0
            ? new(null, errors)
            : new(new ScriptEffectProgram(script, source, hash, sandbox.Limits, name, author, description, animated, area, radius, settings, fixedPadding,
                previewChick ?? PreviewChicks.Default, previewFrames, translations, deferred, easing, clipsBySource, erasesSource, erasesSourceSetting)
            {
                Motion = motion, Tags = tags, Language = language, PreviewEndChick = previewEndChick, PreviewMotion = previewMotion,
                PreviewPlacement = previewPlacement.IsNone ? PreviewPlacement.None : previewPlacement,
                Repeat = repeat, RepeatCount = repeatCount, MovePivot = movePivot,
            }, []);
    }

    /// <summary><c>previewMotion</c>: 이름 문자열(<see cref="PreviewMotion.Presets"/>) 또는 정수 <c>[x, y]</c> 쌍의 배열(<see cref="PreviewMotion.FromOffsets"/>). 아니면 null.</summary>
    private static PreviewMotion? ReadPreviewMotion(JsValue value)
    {
        if (value is JsString) return PreviewMotion.FromPreset(value.AsString());
        if (value is not ArrayInstance list || ScriptHostApi.Length(list) is < 1 or > ScriptEffectProgram.MaxPreviewFrames) return null;
        var offsets = new List<System.Drawing.Point>();
        for (uint i = 0, count = ScriptHostApi.Length(list); i < count; i++)
        {
            if (list[i] is not ArrayInstance pair || ScriptHostApi.Length(pair) != 2
                || Whole(pair[0]) is not { } x || Whole(pair[1]) is not { } y || Math.Abs(x) > PreviewMotion.MaxOffset || Math.Abs(y) > PreviewMotion.MaxOffset)
                return null;
            offsets.Add(new((int)x, (int)y));
        }
        return PreviewMotion.FromOffsets(offsets);
    }

    /// <summary>정수 <c>[x, y]</c> 쌍 하나(각 ±<see cref="PreviewPlacement.MaxOffset"/>). 아니면 null.</summary>
    private static System.Drawing.Point? ReadOffset(JsValue value)
        => value is ArrayInstance pair && ScriptHostApi.Length(pair) == 2 && Whole(pair[0]) is { } x && Whole(pair[1]) is { } y
            && Math.Abs(x) <= PreviewPlacement.MaxOffset && Math.Abs(y) <= PreviewPlacement.MaxOffset
            ? new((int)x, (int)y) : null;

    /// <summary>
    /// <c>tags: ["neon", "glow"]</c>: 올릴 때 쓸 목록 태그. 문자열 배열이고 <see cref="RemoteCatalogFormat.MaxTags"/>개까지.
    /// 각각 앞뒤 공백과 맨 앞 '#'을 떼고 소문자로 바꾼 뒤 <see cref="RemoteCatalogFormat.IsValidTag"/>를 지켜야 하고, 바꾼 뒤 겹치면 안 된다. 없으면 빈 목록.
    /// </summary>
    private static IReadOnlyList<string> ReadTags(JsValue value, Action<string, object[]> fail)
    {
        if (value.IsUndefined()) return [];
        if (value is not ArrayInstance list || ScriptHostApi.Length(list) > RemoteCatalogFormat.MaxTags)
        {
            fail(DomainStrings.FormatScript_InvalidTags(max: RemoteCatalogFormat.MaxTags), ["tags"]);
            return [];
        }
        var tags = new List<string>();
        for (uint i = 0, count = ScriptHostApi.Length(list); i < count; i++)
        {
            var item = list[i];
            if (item is not JsString)
            {
                fail(DomainStrings.FormatScript_InvalidTags(max: RemoteCatalogFormat.MaxTags), ["tags", (int)i]);
                continue;
            }
            string tag = NormalizeTag(item.AsString());
            if (!RemoteCatalogFormat.IsValidTag(tag))
                fail(DomainStrings.FormatScript_InvalidTag(tag: item.AsString(), max: RemoteCatalogFormat.MaxTagLength), ["tags", (int)i]);
            else if (tags.Contains(tag))
                fail(DomainStrings.FormatScript_DuplicateTag(tag: tag), ["tags", (int)i]);
            else tags.Add(tag);
        }
        return tags;
    }

    /// <summary>태그 한 개 정리: 앞뒤 공백, 맨 앞 '#' 하나, 대문자를 없앤다(<c>" #Neon "</c> → <c>"neon"</c>).</summary>
    internal static string NormalizeTag(string tag)
    {
        var text = tag.Trim();
        if (text.StartsWith('#')) text = text[1..].Trim();
        return text.ToLowerInvariant();
    }

    /// <summary>
    /// <c>translations: { "언어 코드": { name?, description?, labels?: { 설정키: "이름", "설정키.항목값": "이름" } } }</c>.
    /// 언어 코드는 BCP-47 모양(대소문자를 가리지 않고 겹치지 않음), 이름은 1~<see cref="AssetManifest.MaxNameLength"/>자, 설명은
    /// <see cref="ScriptEffectProgram.MaxDescriptionLength"/>자까지, 설정 이름은 선언한 설정 키·고르기 항목만 1~<see cref="ScriptTranslations.MaxLabelLength"/>자.
    /// 언어 항목 안의 모르는 키는 다음 버전을 위해 무시한다. 없으면 빈 표.
    /// </summary>
    private static IReadOnlyDictionary<string, ScriptTranslation> ReadTranslations(JsValue value, IReadOnlyList<ScriptSetting> settings, Action<string, object[]> fail)
    {
        if (value.IsUndefined()) return ScriptTranslations.Empty;
        if (value is not ObjectInstance all || value.IsCallable() || value is ArrayInstance)
        {
            fail(DomainStrings.FormatScript_InvalidTranslations(max: ScriptTranslations.MaxLanguages), ["translations"]);
            return ScriptTranslations.Empty;
        }
        var codes = all.GetOwnPropertyKeys(Types.String);
        if (codes.Count > ScriptTranslations.MaxLanguages)
        {
            fail(DomainStrings.FormatScript_InvalidTranslations(max: ScriptTranslations.MaxLanguages), ["translations"]);
            return ScriptTranslations.Empty;
        }
        var labelKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var setting in settings)
        {
            labelKeys.Add(setting.Key);
            foreach (var choice in setting.Choices) labelKeys.Add(ScriptTranslation.ChoiceKey(setting.Key, choice.Value));
        }
        static string? Text(JsValue v, int max) => v is JsString && v.AsString().Trim() is { Length: > 0 } t && t.Length <= max ? t : null;

        var result = new Dictionary<string, ScriptTranslation>(StringComparer.OrdinalIgnoreCase);
        foreach (var codeKey in codes)
        {
            string code = codeKey.AsString();
            if (!ScriptTranslations.IsLanguageCode(code) || result.ContainsKey(code))
            {
                fail(DomainStrings.FormatScript_InvalidTranslationLanguage(code: code), ["translations", code]);
                continue;
            }
            if (all.Get(codeKey) is not ObjectInstance entry || entry.IsCallable() || entry is ArrayInstance)
            {
                fail(DomainStrings.FormatScript_InvalidTranslation(code: code), ["translations", code]);
                continue;
            }
            var nameValue = entry.Get("name");
            string? name = Text(nameValue, AssetManifest.MaxNameLength);
            if (!nameValue.IsUndefined() && name is null)
                fail(DomainStrings.FormatScript_InvalidTranslationName(code: code, max: AssetManifest.MaxNameLength), ["translations", code, "name"]);
            var descriptionValue = entry.Get("description");
            string? description = Text(descriptionValue, ScriptEffectProgram.MaxDescriptionLength);
            if (!descriptionValue.IsUndefined() && description is null)
                fail(DomainStrings.FormatScript_InvalidTranslationDescription(code: code, max: ScriptEffectProgram.MaxDescriptionLength), ["translations", code, "description"]);

            var labels = new Dictionary<string, string>(StringComparer.Ordinal);
            var labelsValue = entry.Get("labels");
            if (labelsValue is ObjectInstance labelObject && !labelsValue.IsCallable() && labelsValue is not ArrayInstance)
            {
                foreach (var labelKey in labelObject.GetOwnPropertyKeys(Types.String))
                {
                    string key = labelKey.AsString();
                    if (labelKeys.Contains(key) && Text(labelObject.Get(labelKey), ScriptTranslations.MaxLabelLength) is { } label) labels[key] = label;
                    else fail(DomainStrings.FormatScript_InvalidTranslationLabel(code: code, key: key, max: ScriptTranslations.MaxLabelLength),
                        ["translations", code, "labels", key]);
                }
            }
            else if (!labelsValue.IsUndefined())
            {
                fail(DomainStrings.FormatScript_InvalidTranslation(code: code), ["translations", code, "labels"]);
            }
            result[code] = new ScriptTranslation(name, description, labels);
        }
        return result;
    }

    /// <summary><c>[왼, 위, 오른, 아래]</c>(0~<see cref="ScriptEffectProgram.MaxPadding"/> 정수 네 개). 아니면 null.</summary>
    public static EffectPadding? ReadPadding(JsValue value)
    {
        if (value is not ArrayInstance sides || ScriptHostApi.Length(sides) != 4) return null;
        Span<int> px = stackalloc int[4];
        for (uint i = 0; i < 4; i++)
        {
            if (Whole(sides[i]) is not { } side || side < 0 || side > ScriptEffectProgram.MaxPadding) return null;
            px[(int)i] = (int)side;
        }
        return new EffectPadding(px[0], px[1], px[2], px[3]);
    }

    private static ScriptSetting? ReadSetting(JsValue value, int index, HashSet<string> keys, Action<string, object[]> fail)
    {
        if (value is not ObjectInstance setting || value.IsCallable())
        {
            fail(DomainStrings.FormatScript_InvalidSetting(index: index), ["settings", index]);
            return null;
        }
        var keyValue = setting.Get("key");
        if (keyValue is not JsString || !SettingKey().IsMatch(keyValue.AsString()))
        {
            fail(DomainStrings.FormatScript_InvalidSettingKey(index: index), ["settings", index, "key"]);
            return null;
        }
        string key = keyValue.AsString();
        if (ScriptEffectProgram.ReservedSettingKeys.Contains(key))
        {
            fail(DomainStrings.FormatScript_ReservedSettingKey(key: key, keys: string.Join(", ", ScriptEffectProgram.ReservedSettingKeys)), ["settings", index, "key"]);
            return null;
        }
        if (!keys.Add(key))
        {
            fail(DomainStrings.FormatScript_DuplicateSettingKey(key: key), ["settings", index, "key"]);
            return null;
        }
        var labelValue = setting.Get("label");
        string label = labelValue is JsString ? labelValue.AsString() : key;
        if (!labelValue.IsUndefined() && labelValue is not JsString)
            fail(DomainStrings.FormatScript_InvalidSettingLabel(key: key), ["settings", index, "label"]);

        var typeValue = setting.Get("type");
        ScriptSettingType? type = typeValue is JsString ? typeValue.AsString() switch
        {
            "int" => ScriptSettingType.Int,
            "number" => ScriptSettingType.Number,
            "bool" => ScriptSettingType.Bool,
            "color" => ScriptSettingType.Color,
            "choice" => ScriptSettingType.Choice,
            "colorList" => ScriptSettingType.ColorList,
            "colorPairs" => ScriptSettingType.ColorPairs,
            "layer" => ScriptSettingType.Layer,
            _ => null,
        } : null;
        if (type is null)
        {
            fail(DomainStrings.FormatScript_InvalidSettingType(key: key), ["settings", index, "type"]);
            return null;
        }

        // alpha: 색이 든 설정만(기본 true). widget: int만 — "angle"이나 "point"(pair = Y 설정 키, 짝 검사는 모든 설정을 읽은 뒤)
        bool isColor = type is ScriptSettingType.Color or ScriptSettingType.ColorList or ScriptSettingType.ColorPairs;
        var alphaValue = setting.Get("alpha");
        bool alpha = !alphaValue.IsBoolean() || alphaValue.AsBoolean();
        if (!alphaValue.IsUndefined() && (!isColor || !alphaValue.IsBoolean()))
            fail(DomainStrings.FormatScript_InvalidSettingAlpha(key: key), ["settings", index, "alpha"]);
        var widgetValue = setting.Get("widget");
        var pairValue = setting.Get("pair");
        var widget = ScriptSettingWidget.None;
        string? pair = null;
        if (!widgetValue.IsUndefined() || !pairValue.IsUndefined())
        {
            string? name = widgetValue is JsString ? widgetValue.AsString() : null;
            if (type == ScriptSettingType.Int && name == "angle" && pairValue.IsUndefined()) widget = ScriptSettingWidget.Angle;
            else if (type == ScriptSettingType.Int && name == "point" && pairValue is JsString pairKey && pairKey.AsString() != key)
            {
                widget = ScriptSettingWidget.Point;
                pair = pairKey.AsString();
            }
            else fail(DomainStrings.FormatScript_InvalidSettingWidget(key: key), ["settings", index, widgetValue.IsUndefined() ? "pair" : "widget"]);
        }
        // when: { 키: 값 | [값, …] } — 보이는 조건(가리키는 설정·값 검사는 모든 설정을 읽은 뒤)
        var whenValue = setting.Get("when");
        var when = new List<ScriptSettingCondition>();
        if (!whenValue.IsUndefined())
        {
            if (whenValue is ObjectInstance whenObject && whenValue is not ArrayInstance && !whenValue.IsCallable())
            {
                foreach (var property in whenObject.GetOwnPropertyKeys(Types.String))
                {
                    var raw = whenObject.Get(property);
                    var items = raw is ArrayInstance array
                        ? Enumerable.Range(0, (int)ScriptHostApi.Length(array)).Select(i => array[(uint)i]).ToList()
                        : [raw];
                    var values = items.Select(v => v is JsString ? v.AsString() : v.IsBoolean() ? v.AsBoolean() : (object?)null).ToList();
                    if (values.Count == 0 || values.Contains(null))
                    {
                        fail(DomainStrings.FormatScript_InvalidSettingWhen(key: key), ["settings", index, "when"]);
                        continue;
                    }
                    when.Add(new ScriptSettingCondition(property.AsString(), values.Select(v => v!).ToArray()));
                }
            }
            else fail(DomainStrings.FormatScript_InvalidSettingWhen(key: key), ["settings", index, "when"]);
        }
        ScriptSetting Done(ScriptSetting result) => result with { Alpha = alpha, Widget = widget, Pair = pair, When = when };
        var defaultValue = setting.Get("default");
        void BadDefault() => fail(DomainStrings.FormatScript_InvalidSettingDefault(key: key), ["settings", index, "default"]);
        switch (type)
        {
            case ScriptSettingType.Int:
            case ScriptSettingType.Number:
            {
                bool whole = type == ScriptSettingType.Int;
                double? Bound(string field, out bool ok)
                {
                    var bound = setting.Get(field);
                    ok = bound.IsUndefined() || (bound is JsNumber && double.IsFinite(bound.AsNumber()) && (!whole || Whole(bound) is not null));
                    return ok && !bound.IsUndefined() ? bound.AsNumber() : null;
                }
                var min = Bound("min", out bool minOk);
                var max = Bound("max", out bool maxOk);
                var step = Bound("step", out bool stepOk);
                if (!minOk || !maxOk || !stepOk || min > max || step <= 0)
                {
                    fail(DomainStrings.FormatScript_InvalidSettingRange(key: key), ["settings", index, !minOk ? "min" : !maxOk || min > max ? "max" : "step"]);
                    return null;
                }
                double number = Math.Clamp(0, min ?? double.MinValue, max ?? double.MaxValue);
                if (!defaultValue.IsUndefined())
                {
                    bool fits = defaultValue is JsNumber && double.IsFinite(defaultValue.AsNumber()) && (!whole || Whole(defaultValue) is not null)
                        && defaultValue.AsNumber() >= (min ?? double.MinValue) && defaultValue.AsNumber() <= (max ?? double.MaxValue)
                        && (!whole || Math.Abs(defaultValue.AsNumber()) <= int.MaxValue);
                    if (!fits)
                    {
                        BadDefault();
                        return null;
                    }
                    number = defaultValue.AsNumber();
                }
                return Done(new(key, label, type.Value, min, max, step, whole ? (int)number : (object)number, []));
            }
            case ScriptSettingType.Bool:
                if (!defaultValue.IsUndefined() && !defaultValue.IsBoolean())
                {
                    BadDefault();
                    return null;
                }
                return Done(new(key, label, type.Value, null, null, null, defaultValue.IsBoolean() && defaultValue.AsBoolean(), []));
            case ScriptSettingType.Color:
            {
                var color = Color32.White;
                if (!defaultValue.IsUndefined() && !(defaultValue is JsString && Color32.TryParseHex(defaultValue.AsString(), out color)))
                {
                    BadDefault();
                    return null;
                }
                return Done(new(key, label, type.Value, null, null, null, alpha ? color : color.WithAlpha(255), []));
            }
            case ScriptSettingType.Layer:
            {
                // 레이어 고르기: key·label·default(레이어 이름, 비우면 고르지 않음)만. 범위·고르기 항목은 받지 않는다
                foreach (string field in (string[])["min", "max", "step", "choices"])
                    if (!setting.Get(field).IsUndefined())
                    {
                        fail(DomainStrings.FormatScript_InvalidLayerSettingField(key: key, field: field), ["settings", index, field]);
                        return null;
                    }
                string? layerName = defaultValue.IsUndefined() ? "" : defaultValue is JsString ? ScriptSetting.LayerName(defaultValue.AsString()) : null;
                if (layerName is null)
                {
                    BadDefault();
                    return null;
                }
                return Done(new(key, label, type.Value, null, null, null, layerName, []));
            }
            case ScriptSettingType.ColorList:
            case ScriptSettingType.ColorPairs:
            {
                // 항목 수 범위: 0~MaxListCount 정수, 기본 0~DefaultMaxListCount
                int? Count(string field, int fallback, out bool ok)
                {
                    var bound = setting.Get(field);
                    ok = bound.IsUndefined() || Whole(bound) is >= 0 and <= ScriptSetting.MaxListCount;
                    return !ok ? null : bound.IsUndefined() ? fallback : (int)bound.AsNumber();
                }
                var minCount = Count("min", 0, out bool minOk);
                var maxCount = Count("max", ScriptSetting.DefaultMaxListCount, out bool maxOk);
                if (!minOk || !maxOk || minCount > maxCount)
                {
                    fail(DomainStrings.FormatScript_InvalidSettingCount(key: key, max: ScriptSetting.MaxListCount), ["settings", index, !minOk ? "min" : "max"]);
                    return null;
                }
                object list;
                if (defaultValue.IsUndefined())
                {
                    list = type == ScriptSettingType.ColorList
                        ? Array.AsReadOnly(Enumerable.Repeat(Color32.White, minCount!.Value).ToArray())
                        : Array.AsReadOnly(Enumerable.Repeat(new ScriptColorPair(Color32.White, Color32.White), minCount!.Value).ToArray());
                }
                else if (ReadColorList(defaultValue, type.Value, alpha) is { } read && ListCount(read) >= minCount && ListCount(read) <= maxCount)
                {
                    list = read;
                }
                else
                {
                    BadDefault();
                    return null;
                }
                return Done(new(key, label, type.Value, minCount, maxCount, null, list, []));
            }
            default:
            {
                var choices = ReadChoices(setting.Get("choices"));
                if (choices is null)
                {
                    fail(DomainStrings.FormatScript_InvalidSettingChoices(key: key), ["settings", index, "choices"]);
                    return null;
                }
                string choice = choices[0].Value;
                if (!defaultValue.IsUndefined())
                {
                    if (defaultValue is not JsString || !choices.Any(c => c.Value == defaultValue.AsString()))
                    {
                        BadDefault();
                        return null;
                    }
                    choice = defaultValue.AsString();
                }
                return Done(new(key, label, type.Value, null, null, null, choice, choices));
            }
        }
    }

    private static int ListCount(object list) => list switch
    {
        IReadOnlyList<Color32> colors => colors.Count,
        IReadOnlyList<ScriptColorPair> pairs => pairs.Count,
        _ => -1,
    };

    // 목록 기본값: 색 목록은 ["#RRGGBB", …], 색 쌍 목록은 [{ from: "#…", to: "#…" }, …]. 틀리면 null
    private static object? ReadColorList(JsValue value, ScriptSettingType type, bool alpha)
    {
        if (value is not ArrayInstance array) return null;
        uint count = ScriptHostApi.Length(array);
        if (count > ScriptSetting.MaxListCount) return null;
        static Color32? Hex(JsValue v, bool alpha)
            => v is JsString && Color32.TryParseHex(v.AsString(), out var c) ? alpha ? c : c.WithAlpha(255) : null;
        if (type == ScriptSettingType.ColorList)
        {
            var colors = new Color32[count];
            for (uint i = 0; i < count; i++)
            {
                if (Hex(array[i], alpha) is not { } c) return null;
                colors[i] = c;
            }
            return Array.AsReadOnly(colors);
        }
        var pairs = new ScriptColorPair[count];
        for (uint i = 0; i < count; i++)
        {
            if (array[i] is not ObjectInstance item || item.IsCallable() || Hex(item.Get("from"), alpha) is not { } from || Hex(item.Get("to"), alpha) is not { } to)
                return null;
            pairs[i] = new(from, to);
        }
        return Array.AsReadOnly(pairs);
    }

    // 문자열이나 { value, label } 목록. 값은 비지 않고 겹치지 않아야 한다
    private static List<ScriptChoice>? ReadChoices(JsValue value)
    {
        if (value is not ArrayInstance list || ScriptHostApi.Length(list) == 0) return null;
        uint count = ScriptHostApi.Length(list);
        var choices = new List<ScriptChoice>((int)Math.Min(count, 256));
        for (uint i = 0; i < count; i++)
        {
            var item = list[i];
            ScriptChoice? choice = null;
            if (item is JsString) choice = new(item.AsString(), item.AsString());
            else if (item is ObjectInstance obj && !item.IsCallable() && obj.Get("value") is JsString choiceValue)
            {
                var labelValue = obj.Get("label");
                if (labelValue.IsUndefined()) choice = new(choiceValue.AsString(), choiceValue.AsString());
                else if (labelValue is JsString) choice = new(choiceValue.AsString(), labelValue.AsString());
            }
            if (choice is null || choice.Value.Length == 0 || choices.Any(c => c.Value == choice.Value)) return null;
            choices.Add(choice);
        }
        return choices;
    }

    private static double? Whole(JsValue value)
        => value is JsNumber && double.IsFinite(value.AsNumber()) && value.AsNumber() == Math.Floor(value.AsNumber()) ? value.AsNumber() : null;
}
