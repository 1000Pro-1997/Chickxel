using System.Drawing;
using System.Globalization;
using Acornima.Ast;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.Scripting;

/// <summary>그라데이션 안내 하나: 그라데이션을 골라도 한 색으로만 그려지는 색 설정과 그 슬롯(<see cref="ScriptColorSlots"/>), 보일 글.</summary>
public sealed record ScriptGradientHint(ScriptSetting Setting, string Slot, string Message);

/// <summary>
/// 색 설정을 그라데이션으로 바꿔도 픽셀마다 색이 달라지는지 보는 안내(권장 — 검증 실패가 아니다). 효과 창에서는 색 설정마다 단색·그라데이션을 고를 수 있는데,
/// 스크립트가 <c>s.키</c>를 한 번 읽어 쓰면 그라데이션을 골라도 한 색이고, <c>ctx.color("키", x, y)</c>(목록은 <c>ctx.colorAt</c>·<c>ctx.pairColor</c>)로
/// 그리는 픽셀마다 골라야 그라데이션이 보인다.
/// <para>
/// <see cref="Render"/>는 검증과 같은 견본(병아리마다 미리보기 칸마다, 다른 설정은 기본값)으로 슬롯마다 그라데이션의 첫 색 단색과 <see cref="SampleGradient"/>를 그려 견준다.
/// 둘이 모든 견본에서 같고, 단색을 그라데이션의 끝 색으로 바꾸면 그림이 달라지면(그 설정을 쓰는데 그라데이션만 안 보임) 안내한다 — 쓰지 않는 설정은 안내하지 않는다.
/// 색 목록은 첫 항목, 색 쌍 목록은 첫 쌍의 바뀐 색(<c>to</c> — 그리는 쪽)을 본다. 다른 견본에서 달라지는 순간 그 슬롯은 멈추므로 보통 슬롯마다 두 번만 그린다.
/// </para>
/// <para><see cref="Static"/>은 그리지 않고 원문만 보는 싼 안내: <c>s.키</c>로 읽으면서 <c>ctx.color</c> 등에 그 키를 한 번도 넘기지 않는 색 설정.</para>
/// </summary>
public static class ScriptGradientCheck
{
    private static readonly Color32 Start = new(255, 0, 0), Middle = new(0, 255, 0), End = new(0, 0, 255);

    /// <summary>견줄 때 슬롯에 넣는 그라데이션: 빨강 → 초록 → 파랑, 가로(왼쪽 → 오른쪽), 움직이지 않음.</summary>
    public static EffectColorGradient SampleGradient { get; } = new()
    {
        Stops = [new GradientStop(0, Start), new GradientStop(0.5, Middle), new GradientStop(1, End)],
        Shape = EffectGradientShape.Linear,
    };

    /// <summary>
    /// 그려 보는 안내(설정 차례). <paramref name="chicks"/>가 없으면 모든 견본 병아리(<see cref="PreviewChicks"/>). 칸은 선언의 <c>previewFrames</c>
    /// (<see cref="ScriptFrameContext.PreviewStep"/>). 실행 오류가 난 견본은 건너뛴다(오류는 검증이 따로 알린다). 색 설정이 없으면 그리지 않는다.
    /// </summary>
    public static IReadOnlyList<ScriptGradientHint> Render(ScriptEffectProgram program, IReadOnlyList<PixelImage>? chicks = null, CancellationToken cancel = default)
    {
        var slots = Slots(program);
        if (slots.Count == 0) return [];
        chicks ??= [.. PreviewChicks.Ids.Select(PreviewChicks.Load)];
        var defaults = program.NormalizeSettings(null);
        int steps = program.PreviewFrames;
        var hints = new List<ScriptGradientHint>();
        foreach (var (setting, slot) in slots)
        {
            var solid = WithSlotColor(defaults, setting, Start);
            var gradients = new Dictionary<string, EffectColorGradient>(StringComparer.Ordinal) { [slot] = SampleGradient };
            // 그라데이션이 한 견본에서라도 그림을 바꾸면 그 슬롯은 됨. 모두 같으면 단색 결과를 남겨 쓰는지 본다
            var flat = new List<(PixelImage Chick, int Step, PixelImage Image)>();
            bool varies = false;
            foreach (var chick in chicks)
            {
                for (int step = 0; step < steps && !varies; step++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var frame = ScriptFrameContext.PreviewStep(step, steps);
                    if (Draw(program, chick, frame, solid, cancel) is not { } a
                        || Draw(program, chick, frame with { Gradients = gradients }, solid, cancel) is not { } b) continue;
                    if (!a.ContentEquals(b)) varies = true;
                    else flat.Add((chick, step, a));
                }
                if (varies) break;
            }
            if (varies || flat.Count == 0) continue;
            var other = WithSlotColor(defaults, setting, End);
            foreach (var (chick, step, image) in flat)
            {
                cancel.ThrowIfCancellationRequested();
                if (Draw(program, chick, ScriptFrameContext.PreviewStep(step, steps), other, cancel) is { } c && !c.ContentEquals(image))
                {
                    hints.Add(Hint(program, setting, slot));
                    break;
                }
            }
        }
        return hints;
    }

    /// <summary>
    /// 원문만 보는 안내(설정 차례): render의 세 번째 인자(없으면 <c>s</c>)에서 <c>.키</c>·<c>["키"]</c>·구조 분해로 읽는 색 설정 중
    /// <c>ctx.color</c>·<c>ctx.colorAt</c>·<c>ctx.pairColor</c>에 글자 그대로의 키로 한 번도 넘기지 않는 것. 키를 글자가 아닌 값으로 넘기는 호출이 있으면 그 종류는 모른다고 보고 넘어간다.
    /// </summary>
    public static IReadOnlyList<ScriptGradientHint> Static(ScriptEffectProgram program)
    {
        if (!program.Settings.Any(s => s.IsColor)) return [];
        var script = program.PreparedScript.Program!;
        string settingsName = SettingsParameter(script, out var destructured);
        var read = new HashSet<string>(destructured, StringComparer.Ordinal);
        var sampled = new HashSet<string>(StringComparer.Ordinal);
        var dynamic = new HashSet<ScriptSettingType>();
        var pending = new Stack<Node>();
        pending.Push(script);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            switch (node)
            {
                case MemberExpression { Object: Identifier owner } member when owner.Name == settingsName && PropertyName(member) is { } key:
                    read.Add(key);
                    break;
                case VariableDeclarator { Id: ObjectPattern pattern, Init: Identifier init } when init.Name == settingsName:
                    foreach (var key in PatternKeys(pattern)) read.Add(key);
                    break;
                case CallExpression { Callee: MemberExpression { Computed: false, Property: Identifier method } } call
                    when SampleType(method.Name) is { } type:
                    if (call.Arguments.Count > 0 && call.Arguments[0] is StringLiteral literal) sampled.Add(literal.Value);
                    else dynamic.Add(type);
                    break;
            }
            foreach (var child in node.ChildNodes) pending.Push(child);
        }
        var hints = new List<ScriptGradientHint>();
        foreach (var setting in program.Settings)
            if (setting.IsColor && read.Contains(setting.Key) && !sampled.Contains(setting.Key) && !dynamic.Contains(setting.Type))
                hints.Add(Hint(program, setting, setting.Type switch
                {
                    ScriptSettingType.ColorList => ScriptColorSlots.Item(setting.Key, 0),
                    ScriptSettingType.ColorPairs => ScriptColorSlots.PairItem(setting.Key, 0, to: true),
                    _ => setting.Key,
                }));
        return hints;
    }

    /// <summary>견줄 슬롯: 색은 그 키, 색 목록은 첫 항목, 색 쌍 목록은 첫 쌍의 바뀐 색(기본값에 항목이 없는 목록은 뺀다).</summary>
    private static List<(ScriptSetting Setting, string Slot)> Slots(ScriptEffectProgram program)
    {
        var slots = new List<(ScriptSetting, string)>();
        foreach (var setting in program.Settings)
        {
            switch (setting.Type)
            {
                case ScriptSettingType.Color:
                    slots.Add((setting, setting.Key));
                    break;
                case ScriptSettingType.ColorList when setting.Default is IReadOnlyList<Color32> { Count: > 0 }:
                    slots.Add((setting, ScriptColorSlots.Item(setting.Key, 0)));
                    break;
                case ScriptSettingType.ColorPairs when setting.Default is IReadOnlyList<ScriptColorPair> { Count: > 0 }:
                    slots.Add((setting, ScriptColorSlots.PairItem(setting.Key, 0, to: true)));
                    break;
            }
        }
        return slots;
    }

    /// <summary>기본 설정에서 그 슬롯의 단색만 <paramref name="color"/>로 바꾼 값.</summary>
    private static Dictionary<string, object> WithSlotColor(IReadOnlyDictionary<string, object> defaults, ScriptSetting setting, Color32 color)
    {
        var values = new Dictionary<string, object>(defaults, StringComparer.Ordinal);
        values[setting.Key] = values[setting.Key] switch
        {
            IReadOnlyList<Color32> list => (object)list.Select((c, i) => i == 0 ? color : c).ToArray(),
            IReadOnlyList<ScriptColorPair> pairs => pairs.Select((p, i) => i == 0 ? p with { To = color } : p).ToArray(),
            _ => color,
        };
        return values;
    }

    private static PixelImage? Draw(ScriptEffectProgram program, PixelImage chick, ScriptFrameContext frame, IReadOnlyDictionary<string, object> values,
        CancellationToken cancel)
    {
        var image = chick.Clone();
        try
        {
            program.Render(image, new Rectangle(0, 0, image.Width, image.Height), frame, values, cancel);
            return image;
        }
        catch (ScriptEffectException)
        {
            return null;
        }
    }

    private static ScriptGradientHint Hint(ScriptEffectProgram program, ScriptSetting setting, string slot)
    {
        var culture = DomainStrings.Culture ?? CultureInfo.CurrentUICulture;
        string call = setting.Type switch
        {
            ScriptSettingType.ColorList => $"ctx.colorAt(\"{setting.Key}\", i, x, y)",
            ScriptSettingType.ColorPairs => $"ctx.pairColor(\"{setting.Key}\", i, \"to\", x, y)",
            _ => $"ctx.color(\"{setting.Key}\", x, y)",
        };
        return new ScriptGradientHint(setting, slot, DomainStrings.FormatScript_GradientHint(label: program.LabelFor(setting, culture), call: call));
    }

    private static ScriptSettingType? SampleType(string method) => method switch
    {
        "color" => ScriptSettingType.Color,
        "colorAt" => ScriptSettingType.ColorList,
        "pairColor" => ScriptSettingType.ColorPairs,
        _ => null,
    };

    /// <summary>render의 세 번째 인자 이름(구조 분해면 그 키들을 <paramref name="destructured"/>에). 찾지 못하면 <c>s</c>.</summary>
    private static string SettingsParameter(Script script, out IReadOnlyList<string> destructured)
    {
        destructured = [];
        var pending = new Stack<Node>();
        pending.Push(script);
        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (node is Property { Value: IFunction function } property && !property.Computed
                && property.Key is Identifier { Name: "render" } or StringLiteral { Value: "render" } && function.Params.Count >= 3)
            {
                switch (function.Params[2])
                {
                    case Identifier id:
                        return id.Name;
                    case ObjectPattern pattern:
                        destructured = [.. PatternKeys(pattern)];
                        return "";
                }
            }
            foreach (var child in node.ChildNodes) pending.Push(child);
        }
        return "s";
    }

    private static IEnumerable<string> PatternKeys(ObjectPattern pattern)
    {
        foreach (var node in pattern.Properties)
            if (node is Property { Computed: false, Key: Identifier id }) yield return id.Name;
            else if (node is Property { Key: StringLiteral literal }) yield return literal.Value;
    }

    private static string? PropertyName(MemberExpression member) => member switch
    {
        { Computed: false, Property: Identifier id } => id.Name,
        { Computed: true, Property: StringLiteral literal } => literal.Value,
        _ => null,
    };
}
