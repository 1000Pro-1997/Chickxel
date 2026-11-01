using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;
using Xunit.Sdk;

namespace Chickxel.Core.Tests;

/// <summary>
/// 기본(C#) 효과와 그것을 JS로 옮긴 스크립트 효과가 실제 효과 파이프라인(<see cref="LayerEffects.At"/> → <see cref="LayerEffects.Render(PixelImage, IReadOnlyList{LayerEffect})"/>)에서
/// 바이트까지 같은지 보는 시험 도구. 빠른 경로와 Jint(<see cref="ScriptEffectProgram.UseJintOnly"/>) 두 길을 모두 돌린다.
/// <para>쓰는 법(공식 효과 하나 옮길 때):</para>
/// <code>
/// var program = EffectParity.Official("shine");                       // Scripting/Official/shine.js를 등록·신뢰·해석(선언 오류면 실패)
/// EffectParity.AssertParity(new ShineEffect { Angle = 30, Width = 5 }, program);   // 기본 묶음: 아래 참고
/// EffectParity.AssertParity(new ShineEffect(), program, gradientSlot: "Color");    // 같은 묶음을 그라데이션마다(<see cref="Gradients"/>)
/// EffectParity.AssertSameLists([new OutlineEffect(), shine], [new OutlineEffect(), EffectParity.ToScript(shine, program)]);  // 직접 만든 목록
/// </code>
/// <list type="bullet">
/// <item><see cref="ToScript"/>: 기본 효과의 저장 설정 JSON(<see cref="EffectDefinition.WriteSettings"/>)을 그대로 스크립트 설정으로 넣는다 — 스크립트 설정 키는
/// 기본 효과의 저장 키와 같아야 하고(다르면 실패), 켜짐·프레임 범위·클리핑·빠르기 곡선·반복과 그라데이션(슬롯 이름은 <see cref="ScriptSlot"/>:
/// <c>Color</c> → <c>color</c>, <c>Colors[0]</c> → <c>colors[0]</c>, <c>Pairs[0].From</c> → <c>pairs[0].from</c>)도 옮긴다.</item>
/// <item><see cref="AssertParity"/>의 기본 묶음: 효과 하나, 여백을 넓히는 앞 효과(그림자·바깥 외곽선) 뒤, 외곽선에 클리핑, 기준이 되어 뒤 외곽선을 클리핑,
/// 프레임 범위(범위 밖 프레임 포함), 움직이는 효과면 반복·왕복·빠르기 곡선 바꾸기. 입력은 <see cref="Inputs"/>(시험 그림, 미리보기 병아리, 투명 구멍·반투명,
/// 빈 그림, 1×1, 프레임마다 다른 그림) × 모든 프레임.</item>
/// <item>다른 프레임을 보는 효과(잔상 등)는 입력의 <see cref="Input.Frames"/>를 프레임 원본으로 받는다(<see cref="LayerEffects.UsesOtherFrames"/>일 때만 — 프로젝트 렌더와 같음).</item>
/// <item><paramref name="expectFast"/>(<see cref="AssertParity"/>·<see cref="AssertSameLists"/>)가 true면 스크립트가 빠른 경로로 컴파일됐는지도 본다.</item>
/// </list>
/// </summary>
internal static class EffectParity
{
    /// <summary>입력 하나: 이름과 프레임별 그림(모두 같은 크기, 프레임 f는 <c>Frames[f % 길이]</c>).</summary>
    public sealed record Input(string Name, IReadOnlyList<PixelImage> Frames);

    /// <summary>
    /// 기본 입력: 시험 그림(<c>fixtures/player@2x.png</c>), 미리보기 병아리 셋, 투명 구멍·반투명이 섞인 그림, 빈(투명) 그림, 1×1 그림,
    /// 프레임마다 한 칸씩 밀린 그림(다른 프레임을 보는 효과용).
    /// </summary>
    public static IReadOnlyList<Input> Inputs { get; } = BuildInputs();

    /// <summary>그라데이션 견본: 모양·색 공간·보간·디더·알파 정지점·반복·움직임(곡선·왕복)·예전 RGBA 직선 보간을 고루 섞음.</summary>
    public static IReadOnlyList<EffectColorGradient> Gradients { get; } =
    [
        new() { Stops = [new(0, new Color32(255, 40, 40)), new(0.5, new Color32(40, 255, 90)), new(1, new Color32(40, 60, 255))], Angle = 30 },
        new()
        {
            Shape = EffectGradientShape.Radial, Reverse = true, Cycles = 2, ColorSpace = GradientColorSpace.OkLab,
            Stops = [new(0, new Color32(250, 230, 60)), new(1, new Color32(120, 20, 160, 200))],
        },
        new()
        {
            Shape = EffectGradientShape.Square, Angle = 15, Dither = GradientDither.Bayer4, ColorSpace = GradientColorSpace.Rgb,
            Stops = [new(0, Color32.White), new(1, new Color32(10, 30, 90))], AlphaStops = [new(0, 255), new(0.6, 90), new(1, 0)],
        },
        new()
        {
            Shape = EffectGradientShape.Angular, Animated = true, PingPong = true, Easing = EffectEasing.EaseInOut, Cycles = 2,
            Interpolation = GradientInterpolation.Steps, Stops = [new(0, new Color32(255, 0, 128)), new(0.3, new Color32(0, 200, 255)), new(1, new Color32(255, 255, 0))],
        },
        new()
        {
            Shape = EffectGradientShape.Uniform, Cycles = 3, ColorSpace = GradientColorSpace.Hsl,
            Stops = [new(0, new Color32(255, 80, 0)), new(1, new Color32(0, 120, 255, 128))],
        },
        new()
        {
            Animated = true, Angle = 200, Dither = GradientDither.Bayer2, Easing = EffectEasing.EaseOut,
            Stops = [new(0, new Color32(20, 20, 20)), new(1, new Color32(240, 240, 200))],
        },
        new()
        {
            StraightRgba = true, ColorSpace = GradientColorSpace.Rgb, Angle = 270,
            Stops = [new(0, new Color32(255, 230, 120)), new(1, new Color32(200, 60, 40, 100))],
        },
    ];

    /// <summary>공식 효과 원문(<see cref="OfficialEffects.Source"/>)을 등록·신뢰하고 해석한 결과(선언 오류면 실패).</summary>
    public static ScriptEffectProgram Official(string id) => Trusted(OfficialEffects.Source(id));

    /// <summary>원문을 등록·신뢰하고 해석한 결과(선언 오류면 실패).</summary>
    public static ScriptEffectProgram Trusted(string source)
    {
        var hash = ScriptLibrary.Add(source);
        ScriptLibrary.SetTrust(hash, true);
        var parsed = ScriptLibrary.Parse(hash)!;
        if (!parsed.Success) throw new XunitException(string.Join("\n", parsed.Errors));
        return parsed.Program;
    }

    /// <summary>기본 효과의 그라데이션 슬롯 이름 → 스크립트 슬롯 이름(제품 변환 <see cref="LegacyEffects.ScriptSlot"/>).</summary>
    public static string ScriptSlot(string builtInSlot) => LegacyEffects.ScriptSlot(builtInSlot);

    /// <summary>
    /// 기본 효과 <paramref name="builtIn"/>와 같은 설정의 스크립트 효과(<paramref name="program"/>) — 제품 변환 <see cref="LegacyEffects.ToScript"/>.
    /// 설정 키가 기본 효과의 저장 키와 하나라도 다르면 실패한다(제품 변환은 다른 키를 조용히 버리므로 여기서 본다).
    /// </summary>
    public static ScriptEffect ToScript(LayerEffect builtIn, ScriptEffectProgram program)
    {
        var json = EffectRegistry.Of(builtIn)!.WriteSettings(builtIn);
        var own = json.Select(p => p.Key).Where(k => !ScriptEffectProgram.ReservedSettingKeys.Contains(k)).Order(StringComparer.Ordinal).ToArray();
        var declared = program.Settings.Select(s => s.Key).Order(StringComparer.Ordinal).ToArray();
        if (!own.SequenceEqual(declared))
            throw new XunitException($"setting keys differ: built-in [{string.Join(", ", own)}] / script [{string.Join(", ", declared)}]");
        return LegacyEffects.ToScript(builtIn, program);
    }

    /// <summary>
    /// <paramref name="builtIn"/>의 기본 묶음(효과 하나, 여백을 넓히는 앞 효과 뒤, 클리핑 사슬, 프레임 범위, 움직임 바꾸기)이 스크립트 효과와 같은지.
    /// <paramref name="gradientSlot"/>(기본 효과의 슬롯 이름, 예: <c>Color</c>)을 주면 <see cref="Gradients"/>마다 그 슬롯에 그라데이션을 넣고 다시 본다.
    /// </summary>
    public static void AssertParity(LayerEffect builtIn, ScriptEffectProgram program, string? gradientSlot = null, int frameCount = 6, bool expectFast = true,
        IEnumerable<Input>? inputs = null)
    {
        var bases = gradientSlot is null
            ? [builtIn]
            : Gradients.Select(g => builtIn with { ColorGradients = new Dictionary<string, EffectColorGradient> { [gradientSlot] = g } }).ToList();
        foreach (var e in bases)
            foreach (var (label, list) in Variants(e, frameCount))
                AssertSameLists(list, [.. list.Select(x => ReferenceEquals(x.GetType(), builtIn.GetType()) ? ToScript(x, program) : x)], frameCount,
                    inputs, label + (gradientSlot is null ? "" : $" gradient {Gradients.ToList().IndexOf(e.ColorGradients[gradientSlot])}"),
                    expectFast ? program : null);
    }

    /// <summary>기본 묶음의 목록들(<see cref="AssertParity"/>).</summary>
    public static IEnumerable<(string Label, IReadOnlyList<LayerEffect> List)> Variants(LayerEffect e, int frameCount)
    {
        yield return ("alone", [e]);
        yield return ("after shadow", [new ShadowEffect { OffsetX = 3, OffsetY = -2 }, e]);
        yield return ("after outline", [new OutlineEffect { Thickness = 2, Color = new Color32(30, 10, 60) }, e]);
        yield return ("clipped to outline", [new OutlineEffect { Color = new Color32(250, 250, 250) }, e with { ClipToPrevious = true }]);
        yield return ("anchor of clipped outline", [e, new OutlineEffect { Color = new Color32(0, 90, 40), ClipToPrevious = true }]);
        yield return ("frame range", [e with { Frames = new FrameRange(1, Math.Max(1, frameCount - 2)) }]);
        if (e is AnimatedEffect animated)
        {
            yield return ("loop", [animated with { Repeat = EffectRepeat.Loop, RepeatCount = 2, Easing = EffectEasing.EaseIn }]);
            yield return ("ping-pong", [animated with { Repeat = EffectRepeat.PingPong, RepeatCount = 3, Easing = EffectEasing.EaseInOut }]);
            yield return ("ease out range", [animated with { Easing = EffectEasing.EaseOut, Frames = new FrameRange(0, frameCount - 1) }]);
        }
    }

    /// <summary>
    /// 두 효과 목록(<paramref name="expected"/> = 기본 효과, <paramref name="actual"/> = 스크립트)이 모든 입력·프레임에서 같은 그림·같은 위치를 내는지:
    /// 빠른 경로와 Jint 두 길 모두. <paramref name="fast"/>를 주면 그 스크립트가 빠른 경로로 컴파일됐는지도 본다.
    /// </summary>
    public static void AssertSameLists(IReadOnlyList<LayerEffect> expected, IReadOnlyList<LayerEffect> actual, int frameCount = 6, IEnumerable<Input>? inputs = null,
        string label = "", ScriptEffectProgram? fast = null)
    {
        if (fast is not null && !fast.ExecutionPath.Compiled)
            throw new XunitException($"{fast.Name}: not on the fast path ({fast.ExecutionPath.Reason} at {fast.ExecutionPath.Line}:{fast.ExecutionPath.Column})");
        // 시간 한도는 기계 부하에 따라 걸리므로(결정론적인 문장 한도는 그대로) 비교 동안 넉넉히
        using var generous = ScriptEffectProgram.UseTimeout(TimeSpan.FromMinutes(10));
        foreach (var input in inputs ?? Inputs)
            for (int f = 0; f < frameCount; f++)
            {
                var want = RenderFrame(input, expected, f, frameCount);
                var got = RenderFrame(input, actual, f, frameCount);
                Compare(want, got, $"{label} / {input.Name} / frame {f} / fast");
                using (ScriptEffectProgram.UseJintOnly())
                    got = RenderFrame(input, actual, f, frameCount);
                Compare(want, got, $"{label} / {input.Name} / frame {f} / Jint");
            }
    }

    /// <summary>프로젝트 렌더처럼 프레임 <paramref name="frame"/>의 효과를 골라(<see cref="LayerEffects.At"/>) 그린 그림과 원본 대비 위치.</summary>
    public static (PixelImage Image, int OffsetX, int OffsetY) RenderFrame(Input input, IReadOnlyList<LayerEffect> effects, int frame, int frameCount)
    {
        var source = input.Frames[frame % input.Frames.Count];
        Func<int, PixelImage?>? frameSource = LayerEffects.UsesOtherFrames(effects)
            ? f => (uint)f < (uint)frameCount ? input.Frames[f % input.Frames.Count] : null
            : null;
        var at = LayerEffects.At(effects, frame, frameCount, null, frameSource, frameSource is null ? null : _ => null);
        return LayerEffects.Render(source, at);
    }

    private static void Compare((PixelImage Image, int OffsetX, int OffsetY) want, (PixelImage Image, int OffsetX, int OffsetY) got, string where)
    {
        if ((want.OffsetX, want.OffsetY, want.Image.Width, want.Image.Height) != (got.OffsetX, got.OffsetY, got.Image.Width, got.Image.Height))
            throw new XunitException($"{where}: size/offset {want.Image.Width}x{want.Image.Height}@{want.OffsetX},{want.OffsetY} != "
                                     + $"{got.Image.Width}x{got.Image.Height}@{got.OffsetX},{got.OffsetY}");
        if (want.Image.ContentEquals(got.Image)) return;
        for (int i = 0; i < want.Image.Pixels.Length; i++)
            if (want.Image.Pixels[i] != got.Image.Pixels[i])
                throw new XunitException($"{where}: pixel ({i % want.Image.Width}, {i / want.Image.Width}) {want.Image.Pixels[i].ToHex()} != {got.Image.Pixels[i].ToHex()}");
    }

    private static List<Input> BuildInputs()
    {
        var holes = new PixelImage(13, 11);
        for (int y = 0; y < holes.Height; y++)
            for (int x = 0; x < holes.Width; x++)
            {
                if ((x * 7 + y * 3) % 11 == 0 || x == 0 && y < 4 || y == holes.Height - 1 && x > 8) continue; // 투명 구멍·가장자리 빈 칸
                byte alpha = (x + y) % 5 == 0 ? (byte)(40 + x * 9) : (x * y) % 7 == 1 ? (byte)1 : (byte)255;
                holes[x, y] = new Color32((byte)(x * 19 + 7), (byte)(y * 23 + 11), (byte)(255 - x * y * 3), alpha);
            }
        var empty = new PixelImage(5, 4);
        var single = new PixelImage(1, 1);
        single[0, 0] = new Color32(200, 100, 50, 180);
        // 프레임마다 가로로 한 칸씩 밀린 그림(크기 같음 — 다른 프레임을 보는 효과)
        var shifted = new List<PixelImage>();
        for (int k = 0; k < 4; k++)
        {
            var frame = new PixelImage(holes.Width, holes.Height);
            for (int y = 0; y < holes.Height; y++)
                for (int x = 0; x < holes.Width; x++)
                    frame[(x + k) % holes.Width, y] = holes[x, y];
            shifted.Add(frame);
        }
        return
        [
            new("player", [PngIO.Load(TestUtil.Fixture("player@2x.png"))]),
            new("chick original", [PreviewChicks.Load(PreviewChicks.Default)]),
            new("chick knight", [PreviewChicks.Load("knight")]),
            new("chick wizard", [PreviewChicks.Load("wizard")]),
            new("holes and partial alpha", [holes]),
            new("empty", [empty]),
            new("single pixel", [single]),
            new("moving frames", shifted),
        ];
    }
}
