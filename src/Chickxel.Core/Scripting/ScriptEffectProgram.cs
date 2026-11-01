using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using Acornima;
using Acornima.Ast;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting.Fast;
using Jint;

namespace Chickxel.Core.Scripting;

/// <summary>스크립트 효과의 계산 범위(12.4). 전체는 프레임마다 한 번, 주변은 조각마다 <see cref="ScriptEffectProgram.Radius"/>만큼 여유를 붙여 부른다.</summary>
public enum ScriptEffectArea
{
    Global,
    Local,
}

/// <summary>스크립트 해석 결과: 성공하면 <see cref="Program"/>, 실패하면 <see cref="Errors"/>(줄·칸 포함).</summary>
public sealed record ScriptParseResult(ScriptEffectProgram? Program, IReadOnlyList<ScriptError> Errors)
{
    [MemberNotNullWhen(true, nameof(Program))]
    public bool Success => Program is not null;
}

/// <summary>
/// 해석한 스크립트 효과 하나(13.2): <c>effect({ name, author, description, translations, animated, previewChick, previewFrames, area, radius, settings, padding, render })</c>를 선언한 JS 파일.
/// 불변이고 여러 스레드에서 같이 써도 된다. 실행은 부른 스레드의 <see cref="ScriptSandbox"/>에서 <see cref="Limits"/> 안에서 한다.
/// <para>
/// 스크립트 정체는 <see cref="ContentHash"/>(정리한 원문의 SHA-256)다(13.5). 효과 TypeId <c>chickxel.effect.script</c>와 이 해시, 설정값으로 효과 하나가 정해진다.
/// </para>
/// <para>
/// render는 처음 부를 때(또는 <see cref="ExecutionPath"/>를 처음 읽을 때) 앱이 원문을 .NET 코드로 바꿔 보고(빠른 경로, <see cref="FastCompiler"/>),
/// 바꿀 수 있으면 그 코드로, 아니면 Jint로 돈다. 두 길은 같은 입력에 같은 픽셀과 같은 문장 수 한도를 낸다. 선언 읽기와 <c>padding</c>은 늘 Jint다.
/// </para>
/// </summary>
public sealed class ScriptEffectProgram
{
    /// <summary>
    /// 여백 한 쪽과 주변 계산 반경의 최대 px = 이미지 최대 크기. 기본 효과처럼 따로 자르지 않고, 넓힌 축이 최대 크기를 넘으면
    /// <see cref="LayerEffects.Padding"/>이 그 축을 넓히지 않는다(회전·반사처럼 큰 그림에서 1024px보다 넓히는 효과도 기본 효과와 같게).
    /// </summary>
    public const int MaxPadding = PixelImage.MaxSize;

    private readonly Prepared<Script> _script;
    private readonly EffectPadding? _fixedPadding; // null이면 padding 함수
    private readonly Lazy<FastState> _fast;

    /// <summary>
    /// 설정 키로 쓸 수 없는 이름. 효과의 빠르기 곡선·반복이 같은 설정 JSON에 이 키로 저장된다(기본 움직이는 효과와 같은 모양).
    /// </summary>
    public static IReadOnlyList<string> ReservedSettingKeys { get; } = ["easing", "repeat", "repeatCount"];

    internal ScriptEffectProgram(Prepared<Script> script, string source, string contentHash, ScriptLimits limits, string name, string? author, string? description,
        bool animated, ScriptEffectArea area, int radius, IReadOnlyList<ScriptSetting> settings, EffectPadding? fixedPadding, string previewChick, int previewFrames,
        IReadOnlyDictionary<string, ScriptTranslation> translations, bool deferred = false, EffectEasing easing = EffectEasing.Linear,
        bool clipsBySource = false, bool erasesSource = false, string? erasesSourceSetting = null)
    {
        _script = script;
        Source = source;
        ContentHash = contentHash;
        Limits = limits;
        Name = name;
        Author = author;
        Description = description;
        Animated = animated;
        Area = area;
        Radius = radius;
        Settings = settings;
        _fixedPadding = fixedPadding;
        PreviewChick = previewChick;
        PreviewFrames = previewFrames;
        Translations = translations;
        Deferred = deferred;
        Easing = easing;
        ClipsBySource = clipsBySource;
        ErasesSource = erasesSource;
        ErasesSourceSetting = erasesSourceSetting;
        _fast = new(CompileFast, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// 선언의 <c>translations</c>(언어 코드 → 그 언어의 제목·설명·설정 이름, 언어 코드는 대소문자를 가리지 않음, 없으면 빈 표).
    /// 화면에 보일 때는 <see cref="NameFor"/>·<see cref="DescriptionFor"/>·<see cref="LabelFor"/>·<see cref="ChoiceLabelFor"/>로 고른다.
    /// 정체(해시)·중복 판정·파일 이름에는 선언의 <see cref="Name"/>을 그대로 쓴다.
    /// </summary>
    public IReadOnlyDictionary<string, ScriptTranslation> Translations { get; }

    /// <summary><paramref name="culture"/>(보통 화면 언어)의 제목: 그 언어 → 중립 언어 → 선언의 <c>name</c>.</summary>
    public string NameFor(CultureInfo culture) => ScriptTranslations.NameFor(Translations, Name, culture);

    /// <summary><paramref name="culture"/>의 설명: 그 언어 → 중립 언어 → 선언의 <c>description</c>.</summary>
    public string? DescriptionFor(CultureInfo culture) => ScriptTranslations.DescriptionFor(Translations, Description, culture);

    /// <summary><paramref name="culture"/>의 설정 이름: 번역의 <c>labels[키]</c> → 선언의 <c>label</c>.</summary>
    public string LabelFor(ScriptSetting setting, CultureInfo culture) => ScriptTranslations.LabelFor(Translations, setting, culture);

    /// <summary><paramref name="culture"/>의 고르기 항목 이름: 번역의 <c>labels["키.값"]</c> → 선언의 항목 <c>label</c>.</summary>
    public string ChoiceLabelFor(ScriptSetting setting, ScriptChoice choice, CultureInfo culture)
        => ScriptTranslations.ChoiceLabelFor(Translations, setting, choice, culture);

    /// <summary>render가 도는 길: 빠른 경로인지, 아니면 Jint로 도는 까닭과 위치. 처음 읽을 때 한 번 바꿔 보고 그 결과를 계속 쓴다.</summary>
    public ScriptExecutionPath ExecutionPath => _fast.Value.Path;

    /// <summary>해석한 원문(시험에서 Jint 길을 직접 부를 때).</summary>
    internal Prepared<Script> PreparedScript => _script;

    /// <summary>빠른 경로 render(바꿀 수 없으면 null).</summary>
    internal FastRender? Fast => _fast.Value.Render;

    /// <summary>정리한 원문(<see cref="NormalizeSource"/>). 오류의 줄·칸은 이 글 기준이다.</summary>
    public string Source { get; }

    /// <summary>정리한 원문의 SHA-256(소문자 16진수 64자).</summary>
    public string ContentHash { get; }

    public ScriptLimits Limits { get; }

    /// <summary>선언의 <c>name</c>.</summary>
    public string Name { get; }

    /// <summary>선언의 <c>author</c>(선택, 없으면 null): 신뢰 확인 창에 보인다.</summary>
    public string? Author { get; }

    /// <summary>선언의 <c>description</c>(선택, 없으면 null, <see cref="MaxDescriptionLength"/>자까지): 효과 만들기의 설명 칸·올릴 때 짧은 설명의 처음 값.</summary>
    public string? Description { get; }

    /// <summary>선언 <c>description</c>의 최대 길이(올릴 때 짧은 설명 한도와 같음).</summary>
    public const int MaxDescriptionLength = 200;

    /// <summary>선언의 <c>animated</c>(기본 false): 프레임 진행에 따라 모습이 바뀌는지.</summary>
    public bool Animated { get; }

    /// <summary>
    /// 선언의 <c>deferred</c>(기본 false): 목록 자리와 관계없이 다른(미루지 않는) 효과를 모두 입힌 뒤에 그린다 — 기본 잔상 효과와 같은 차례
    /// (<see cref="LayerEffects.IsDeferred"/>). 이런 효과만 <c>ctx.renderedFrameSource</c>로 다른 프레임에 다른 효과를 입힌 그림을 받는다.
    /// </summary>
    public bool Deferred { get; }

    /// <summary>선언의 <c>easing</c>(기본 <see cref="EffectEasing.Linear"/>): 새로 넣는 효과의 빠르기 곡선 처음 값(<see cref="CreateEffect"/>).</summary>
    public EffectEasing Easing { get; }

    /// <summary>
    /// 선언의 <c>motion</c>(기본 true): false면 움직이는 효과라도 빠르기 곡선·반복을 쓰지 않으므로(프레임 수로 정하는 깜빡임 등) 효과 창·효과 만들기에서
    /// 그 편집기를 숨긴다. 저장되는 움직임 설정은 그대로다.
    /// </summary>
    public bool Motion { get; init; } = true;

    /// <summary>
    /// 선언의 <c>clipsBySource</c>(기본 false): 클리핑되면 기준 효과가 바꾼 픽셀만 원본(<c>img</c>)으로 받고, 기준 영역 밖에 생긴 결과는 그림 뒤에 놓는다 —
    /// 픽셀을 옮기는 효과(반사)용. 클리핑돼도 여백을 넓힌다(<see cref="Effects.EffectDefinition.ClipsBySource"/>). 아니면 결과 중 기준 영역 안만 남는다.
    /// </summary>
    public bool ClipsBySource { get; }

    /// <summary>
    /// 선언의 <c>erasesSource: true</c>: 원본을 지워(투명하게) 선 같은 결과만 남기는 효과 — 지워진 자리는 뒤따르는 클리핑 효과의 기준 영역에서 빠진다.
    /// 설정 키로 선언하면 <see cref="ErasesSourceSetting"/>(그 bool 설정이 참일 때만)이고 이 값은 false다. <see cref="ErasesSourceWith"/>로 효과마다 판단한다.
    /// </summary>
    public bool ErasesSource { get; }

    /// <summary>선언의 <c>erasesSource: "설정키"</c>: 그 bool 설정이 참일 때만 원본을 지우는 효과(없으면 null).</summary>
    public string? ErasesSourceSetting { get; }

    /// <summary>설정 <paramref name="values"/>(빠지거나 틀린 값은 기본값)로 그린 효과가 원본을 지우는지(<see cref="ErasesSource"/>·<see cref="ErasesSourceSetting"/>).</summary>
    public bool ErasesSourceWith(IReadOnlyDictionary<string, object>? values)
    {
        if (ErasesSource) return true;
        if (ErasesSourceSetting is not { } key || Settings.FirstOrDefault(s => s.Key == key) is not { } setting) return false;
        object? value = null;
        values?.TryGetValue(key, out value);
        return (Coerce(setting, value) ?? setting.Default) is true;
    }

    /// <summary>
    /// 이 스크립트를 쓰는 새 효과: 설정은 <paramref name="values"/>를 schema에 맞춘 값(null이면 기본값), 빠르기 곡선은 선언의 <c>easing</c>.
    /// 원문은 부르는 쪽이 프로젝트 스크립트 표에 넣는다.
    /// </summary>
    public ScriptEffect CreateEffect(IReadOnlyDictionary<string, object>? values = null)
        => new() { ScriptHash = ContentHash, Settings = ScriptSettingValues.Normalized(this, values), Easing = Easing };

    /// <summary>선언의 <c>previewChick</c>(기본 <see cref="Assets.PreviewChicks.Default"/>): 패키지 미리보기에서 효과를 입힐 병아리.</summary>
    public string PreviewChick { get; }

    /// <summary>
    /// 선언의 <c>previewFrames</c>(<see cref="MinPreviewFrames"/>~<see cref="MaxPreviewFrames"/>, 기본 <see cref="Assets.AssetPreview.PackageFrames"/>):
    /// 효과 만들기 미리보기·검증과 올릴 때 굽는 패키지 미리보기에서 효과를 돌리는 칸 수.
    /// </summary>
    public int PreviewFrames { get; }

    /// <summary>선언 <c>previewFrames</c>의 범위.</summary>
    public const int MinPreviewFrames = 2, MaxPreviewFrames = 20;

    /// <summary>선언의 <c>area</c>(기본 <see cref="ScriptEffectArea.Global"/>).</summary>
    public ScriptEffectArea Area { get; }

    /// <summary>선언의 <c>radius</c>: <see cref="ScriptEffectArea.Local"/>일 때 조각 둘레에 붙일 여유(px). 전체 효과는 0.</summary>
    public int Radius { get; }

    /// <summary>선언의 <c>settings</c>(선언 순서).</summary>
    public IReadOnlyList<ScriptSetting> Settings { get; }

    /// <summary>
    /// <paramref name="source"/>를 해석하고 맨 위를 한 번 실행해 선언을 검사한다. 문법 오류, 잘못된 선언, 맨 위 실행의 한도 초과와 오류는
    /// 줄·칸이 붙은 <see cref="ScriptParseResult.Errors"/>로 돌려준다(던지지 않는다).
    /// </summary>
    public static ScriptParseResult Parse(string source, ScriptLimits? limits = null)
    {
        limits ??= ScriptLimits.Default;
        var text = NormalizeSource(source);
        Prepared<Script> script;
        try
        {
            script = ScriptSandbox.Prepare(text);
        }
        catch (ScriptPreparationException ex) when (ex.InnerException is ParseErrorException parse)
        {
            return new(null, [new(ScriptErrorKind.Syntax, DomainStrings.FormatScript_SyntaxError(detail: parse.Description), parse.LineNumber, parse.Column + 1)]);
        }
        try
        {
            return ScriptSandbox.Run(limits, CancellationToken.None, sandbox => ScriptDeclarationReader.Read(sandbox, script, text, HashOf(text)));
        }
        catch (ScriptEffectException ex)
        {
            return new(null, [ex.Error]);
        }
    }

    /// <summary>해시와 해석에 쓰는 원문: 앞의 BOM을 빼고 줄바꿈을 LF로 통일한다(Windows와 macOS에서 같은 해시).</summary>
    public static string NormalizeSource(string source)
        => source.TrimStart('\uFEFF').ReplaceLineEndings("\n");

    /// <summary><paramref name="source"/>를 <see cref="NormalizeSource"/>로 정리한 글의 SHA-256(소문자 16진수).</summary>
    public static string HashOf(string source)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeSource(source))));

    /// <summary>
    /// 모든 설정 키의 값. 빠진 키, 종류가 틀린 값과 고르기 목록에 없는 값은 기본값으로, 숫자는 min~max로 자르고 int는 반올림한다.
    /// 값의 C# 형식은 <see cref="ScriptSettingType"/> 설명과 같다. 색은 <see cref="Color32"/>나 16진수 글로 받는다. 모르는 키는 버린다.
    /// </summary>
    public IReadOnlyDictionary<string, object> NormalizeSettings(IReadOnlyDictionary<string, object>? values)
    {
        var result = new Dictionary<string, object>(Settings.Count, StringComparer.Ordinal);
        foreach (var setting in Settings)
        {
            object? value = null;
            values?.TryGetValue(setting.Key, out value);
            result[setting.Key] = Coerce(setting, value) ?? setting.Default;
        }
        return result;
    }

    /// <summary>
    /// 효과가 <paramref name="width"/>×<paramref name="height"/> 그림 밖으로 넓히는 여백. 선언의 <c>padding</c>이 함수면
    /// <c>padding(s, width, height)</c>를 불러 <c>[왼, 위, 오른, 아래]</c>(0~<see cref="MaxPadding"/> 정수)를 받는다.
    /// </summary>
    public EffectPadding Padding(IReadOnlyDictionary<string, object>? settings, int width, int height, CancellationToken cancel = default)
    {
        if (_fixedPadding is { } fixedPadding) return fixedPadding;
        var values = NormalizeSettings(settings);
        return ScriptSandbox.Run(Limits, cancel, sandbox =>
        {
            var declaration = sandbox.Declare(_script);
            var s = ScriptHostApi.CreateSettings(sandbox.Engine, Settings, values);
            var result = sandbox.Engine.Invoke(declaration.Get("padding"), declaration, [s, width, height]);
            if (ScriptDeclarationReader.ReadPadding(result) is { } padding) return padding;
            var (line, column) = ScriptSourceLocator.Declaration(_script.Program!, "padding");
            throw new ScriptEffectException(new(ScriptErrorKind.Runtime, DomainStrings.FormatScript_InvalidPadding(max: MaxPadding), line, column));
        });
    }

    /// <summary>
    /// <c>render(img, ctx, s)</c>로 <paramref name="image"/>를 제자리에서 바꾼다(기본 효과 렌더러와 같은 계약).
    /// <paramref name="source"/>는 여백을 넣은 그림 안의 원본 자리(<c>ctx.source</c>). 실패하면 그림은 그대로 두고
    /// <see cref="ScriptEffectException"/>을, 취소되면 <see cref="OperationCanceledException"/>을 던진다.
    /// 빠른 경로가 JS와 결과가 갈릴 자리에 닿으면 그 호출만 Jint로 다시 그린다. 한도는 그림 크기에 비례한다(<see cref="ScriptLimits.ForPixels"/> — 두 길이 같은 값).
    /// </summary>
    public void Render(PixelImage image, Rectangle source, ScriptFrameContext context, IReadOnlyDictionary<string, object>? settings = null,
        CancellationToken cancel = default)
    {
        var values = NormalizeSettings(settings);
        // 빠른 경로는 그림 크기에 제한이 없다(메모리가 허락하는 만큼 — 기본 효과와 같음). Jint는 JS 배열 한도(MaxArrayLength) 안의 그림만
        if (!t_jintOnly && Fast is { } fast && fast.WithLimits(CallLimits(image)).TryRender(image, source, context, values, cancel)) return;
        RenderOnJint(image, source, context, values, cancel);
    }

    [ThreadStatic] private static TimeSpan? t_timeout;

    // render 한 번의 한도: 그림 크기 비례(ScriptLimits.ForPixels), 시험이 시간 한도를 바꿨으면 그 값
    private ScriptLimits CallLimits(PixelImage image)
    {
        var limits = Limits.ForPixels((long)image.Width * image.Height);
        return t_timeout is { } timeout ? limits with { Timeout = timeout } : limits;
    }

    /// <summary>
    /// 시험용: 이 스레드의 render 시간 한도를 <paramref name="timeout"/>으로(바뀌는 기계 부하에 걸리지 않게 — 결정론적인 문장 한도는 그대로).
    /// 돌려받은 것을 닫으면 앞의 값으로 돌아간다.
    /// </summary>
    internal static IDisposable UseTimeout(TimeSpan timeout)
    {
        var previous = t_timeout;
        t_timeout = timeout;
        return new Restore(() => t_timeout = previous);
    }

    [ThreadStatic] private static bool t_jintOnly;

    /// <summary>
    /// 시험용: 이 스레드의 <see cref="Render"/>가 빠른 경로를 건너뛰고 Jint로만 돌게 한다(효과 목록 렌더링을 통째로 Jint로 비교할 때).
    /// 돌려받은 것을 닫으면 앞의 방식으로 돌아간다. 스크립트 효과는 부른 스레드에서 돌므로 렌더링 전체에 걸린다.
    /// </summary>
    internal static IDisposable UseJintOnly()
    {
        bool previous = t_jintOnly;
        t_jintOnly = true;
        return new Restore(() => t_jintOnly = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    /// <summary>Jint로 render(<paramref name="values"/>는 <see cref="NormalizeSettings"/>를 거친 값).</summary>
    internal void RenderOnJint(PixelImage image, Rectangle source, ScriptFrameContext context, IReadOnlyDictionary<string, object> values,
        CancellationToken cancel)
    {
        if ((long)image.Width * image.Height * 4 > Limits.MaxArrayLength)
            throw new ScriptEffectException(new(ScriptErrorKind.Memory,
                DomainStrings.FormatScript_ImageTooLarge(width: image.Width, height: image.Height), 0, 0));
        ScriptSandbox.Run(Limits, CallLimits(image), cancel, sandbox =>
        {
            var declaration = sandbox.Declare(_script);
            var s = ScriptHostApi.CreateSettings(sandbox.Engine, Settings, values);
            var colors = new ScriptColors(Settings, values, context, image, source);
            var ctx = ScriptHostApi.CreateContext(sandbox, Settings, s, source, context, new Size(image.Width, image.Height), colors, Deferred);
            var (img, data) = ScriptHostApi.CreateImage(sandbox, image);
            sandbox.Engine.Invoke(declaration.Get("render"), declaration, [img, ctx, s]);
            ScriptHostApi.CopyBack(data, image);
            return true;
        });
    }

    private sealed record FastState(FastRender? Render, ScriptExecutionPath Path);

    private FastState CompileFast()
    {
        try
        {
            return new(FastCompiler.Compile(_script.Program!, Settings, Limits, Deferred), ScriptExecutionPath.Fast);
        }
        catch (FastUnsupportedException ex)
        {
            return new(null, new(false, ex.Message, ex.Line, ex.Column));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            // 변환기 자체의 결함: 효과는 Jint로 그대로 돌게 하고 까닭만 알린다
            return new(null, new(false, DomainStrings.FormatScript_FastInternal(detail: ex.Message), 0, 0));
        }
    }

    private static object? Coerce(ScriptSetting setting, object? value)
    {
        switch (setting.Type)
        {
            case ScriptSettingType.Int:
            case ScriptSettingType.Number:
                if (AsDouble(value) is not { } number || !double.IsFinite(number)) return null;
                if (setting.Type == ScriptSettingType.Int) number = Math.Round(number);
                number = Math.Clamp(number, setting.Min ?? double.MinValue, setting.Max ?? double.MaxValue);
                return setting.Type == ScriptSettingType.Int ? (int)Math.Clamp(number, int.MinValue, int.MaxValue) : (object)number;
            case ScriptSettingType.Bool:
                return value as bool?;
            case ScriptSettingType.Color:
                return value switch
                {
                    Color32 color => Opaque(setting, color),
                    string hex when Color32.TryParseHex(hex, out var parsed) => Opaque(setting, parsed),
                    _ => null,
                };
            case ScriptSettingType.ColorList:
                return CoerceList(setting, ReadColors(value));
            case ScriptSettingType.ColorPairs:
                return CoerceList(setting, ReadPairs(value)?.Select(p => new ScriptColorPair(Opaque(setting, p.From), Opaque(setting, p.To))).ToArray());
            default:
                return value is string choice && setting.Choices.Any(c => c.Value == choice) ? choice : null;
        }
    }

    // 알파를 고르지 않는 설정(alpha: false)의 색은 불투명으로
    private static Color32 Opaque(ScriptSetting setting, Color32 color) => setting.Alpha ? color : color.WithAlpha(255);

    // 목록: 잘못된 항목이 있거나 min보다 적으면 기본값, max보다 많으면 앞에서부터 max개
    private static object? CoerceList<T>(ScriptSetting setting, T[]? items)
    {
        if (items is null || items.Length < (setting.Min ?? 0)) return null;
        int max = (int)(setting.Max ?? ScriptSetting.MaxListCount);
        if (items.Length > max) items = items[..max];
        return setting.Type == ScriptSettingType.ColorList
            ? Array.AsReadOnly(items.Cast<Color32>().Select(c => Opaque(setting, c)).ToArray())
            : Array.AsReadOnly(items);
    }

    // 색 목록 값: Color32 목록, 16진수 글 목록, 저장 JSON 배열
    private static Color32[]? ReadColors(object? value)
    {
        switch (value)
        {
            case IEnumerable<Color32> colors:
                return [.. colors];
            case IEnumerable<string> texts:
            {
                var list = new List<Color32>();
                foreach (var text in texts)
                {
                    if (!Color32.TryParseHex(text, out var c)) return null;
                    list.Add(c);
                }
                return [.. list];
            }
            case ScriptRawValue raw:
                return ReadColors(ParseJson(raw.Json));
            case System.Text.Json.Nodes.JsonArray array:
            {
                var list = new List<Color32>(array.Count);
                foreach (var node in array)
                {
                    if (node is not System.Text.Json.Nodes.JsonValue v || !v.TryGetValue(out string? text) || !Color32.TryParseHex(text, out var c)) return null;
                    list.Add(c);
                }
                return [.. list];
            }
            default:
                return null;
        }
    }

    // 색 쌍 목록 값: ScriptColorPair 목록, 저장 JSON 배열([["#a", "#b"], …] 또는 [{ "from": "#a", "to": "#b" }, …])
    private static ScriptColorPair[]? ReadPairs(object? value)
    {
        switch (value)
        {
            case IEnumerable<ScriptColorPair> pairs:
                return [.. pairs];
            case ScriptRawValue raw:
                return ReadPairs(ParseJson(raw.Json));
            case System.Text.Json.Nodes.JsonArray array:
            {
                var list = new List<ScriptColorPair>(array.Count);
                foreach (var node in array)
                {
                    var (from, to) = node switch
                    {
                        System.Text.Json.Nodes.JsonArray { Count: 2 } pair => (pair[0], pair[1]),
                        System.Text.Json.Nodes.JsonObject obj => (obj["from"], obj["to"]),
                        _ => (null, null),
                    };
                    if (Hex(from) is not { } a || Hex(to) is not { } b) return null;
                    list.Add(new(a, b));
                }
                return [.. list];
            }
            default:
                return null;
        }

        static Color32? Hex(System.Text.Json.Nodes.JsonNode? node)
            => node is System.Text.Json.Nodes.JsonValue v && v.TryGetValue(out string? text) && Color32.TryParseHex(text, out var c) ? c : null;
    }

    private static System.Text.Json.Nodes.JsonNode? ParseJson(string json)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static double? AsDouble(object? value) => value switch
    {
        int i => i,
        long l => l,
        double d => d,
        float f => f,
        decimal m => (double)m,
        short s => s,
        byte b => b,
        _ => null,
    };
}
