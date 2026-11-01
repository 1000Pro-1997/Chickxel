using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 모양을 바꾸는 공식 스크립트 효과(외곽선·반사·회전)가 기본 효과와 효과 파이프라인에서 바이트까지 같은지(<see cref="EffectParity"/>, 빠른 경로·Jint),
/// 그리고 그 효과들이 쓰는 선언 <c>clipsBySource</c>·<c>erasesSource</c>를 파이프라인이 기본 효과의 정의처럼 다루는지.
/// </summary>
public class OfficialEffectParityGeometryTests
{
    // ───────────── clipsBySource · erasesSource 선언 ─────────────

    [Fact]
    public void Clip_declarations_default_to_off()
    {
        var program = Parse("effect({ name: 'x', render() {} });");
        Assert.False(program.ClipsBySource);
        Assert.False(program.ErasesSource);
        Assert.Null(program.ErasesSourceSetting);
        Assert.False(program.ErasesSourceWith(null));
    }

    [Fact]
    public void Clip_declarations_read_booleans_and_a_bool_setting_key()
    {
        var always = Parse("effect({ name: 'x', clipsBySource: true, erasesSource: true, render() {} });");
        Assert.True(always.ClipsBySource);
        Assert.True(always.ErasesSource);
        Assert.True(always.ErasesSourceWith(null));

        var off = Parse("effect({ name: 'x', clipsBySource: false, erasesSource: false, render() {} });");
        Assert.False(off.ClipsBySource);
        Assert.False(off.ErasesSourceWith(null));

        var keyed = Parse("effect({ name: 'x', erasesSource: 'hide', settings: [{ key: 'hide', label: 'h', type: 'bool', default: true }], render() {} });");
        Assert.False(keyed.ErasesSource);
        Assert.Equal("hide", keyed.ErasesSourceSetting);
        Assert.True(keyed.ErasesSourceWith(null)); // 빠진 값은 기본값
        Assert.False(keyed.ErasesSourceWith(new Dictionary<string, object> { ["hide"] = false }));
        Assert.True(keyed.ErasesSourceWith(new Dictionary<string, object> { ["hide"] = true }));
        Assert.True(keyed.ErasesSourceWith(new Dictionary<string, object> { ["hide"] = 3.0 })); // 틀린 모양은 기본값
    }

    [Theory]
    [InlineData("clipsBySource: 1", "clipsBySource")]
    [InlineData("clipsBySource: 'yes'", "clipsBySource")]
    [InlineData("erasesSource: 1", "erasesSource")]
    [InlineData("erasesSource: 'missing'", "erasesSource")]
    [InlineData("erasesSource: 'size'", "erasesSource")]
    [InlineData("erasesSource: 'mode'", "erasesSource")]
    public void Invalid_clip_declarations_are_reported(string declaration, string key)
    {
        var result = ScriptEffectProgram.Parse("effect({ name: 'x', " + declaration + ",\n settings: [{ key: 'size', label: 's', type: 'int', min: 0, max: 3, default: 1 },"
                                               + " { key: 'mode', label: 'm', type: 'choice', default: 'a', choices: [{ value: 'a', label: 'a' }] }], render() {} });");
        Assert.False(result.Success);
        var error = Assert.Single(result.Errors);
        Assert.Contains(key, error.Message);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void Script_definition_takes_clip_flags_from_the_declaration()
    {
        var definition = ScriptDefinition.Definition;
        var mover = EffectParity.Trusted(MoverSource(clips: true));
        var plain = EffectParity.Trusted(MoverSource(clips: false));
        var keyed = EffectParity.Trusted("effect({ name: 'eraser', erasesSource: 'hide', settings: [{ key: 'hide', label: 'h', type: 'bool', default: false }],"
                                         + " render(img) { if (img.width > 0) img.data[3] = 0; } });");
        Assert.True(definition.ClipsBySource(mover.CreateEffect()));
        Assert.False(definition.ClipsBySource(plain.CreateEffect()));
        Assert.False(definition.ErasesSource(mover.CreateEffect()));
        Assert.False(definition.ErasesSource(keyed.CreateEffect()));
        Assert.True(definition.ErasesSource(keyed.CreateEffect(new Dictionary<string, object> { ["hide"] = true })));
        // 실행할 수 없는(모르는) 원문은 기본 방식
        Assert.False(definition.ClipsBySource(new ScriptEffect { ScriptHash = new string('0', 64) }));
        Assert.False(definition.ErasesSource(new ScriptEffect { ScriptHash = new string('0', 64) }));
    }

    [Fact]
    public void Clipped_script_widens_padding_only_when_it_clips_by_source()
    {
        var mover = EffectParity.Trusted(MoverSource(clips: true)).CreateEffect() with { ClipToPrevious = true };
        var plain = EffectParity.Trusted(MoverSource(clips: false)).CreateEffect() with { ClipToPrevious = true };
        var outline = new OutlineEffect();
        Assert.Equal(new EffectPadding(1, 1, 1, 4), LayerEffects.Padding([outline, mover], 8, 8));
        Assert.Equal(new EffectPadding(1, 1, 1, 1), LayerEffects.Padding([outline, plain], 8, 8));
        // 기본 반사와 같은 규칙
        Assert.Equal(LayerEffects.Padding([outline, new ReflectionEffect { ClipToPrevious = true }], 8, 8),
            LayerEffects.Padding([outline, EffectParity.ToScript(new ReflectionEffect { ClipToPrevious = true }, EffectParity.Official("reflection"))], 8, 8));
    }

    [Fact]
    public void Clipped_script_that_clips_by_source_keeps_moved_pixels_behind_the_picture()
    {
        var source = new PixelImage(4, 4);
        for (int y = 1; y < 3; y++)
            for (int x = 1; x < 3; x++)
                source[x, y] = new Color32(200, 40, 40);
        var outline = new OutlineEffect { Color = new Color32(0, 0, 255) };
        var mover = EffectParity.Trusted(MoverSource(clips: true)).CreateEffect() with { ClipToPrevious = true };
        var plain = EffectParity.Trusted(MoverSource(clips: false)).CreateEffect() with { ClipToPrevious = true };

        var (moved, mx, my) = LayerEffects.Render(source, [outline, mover]);
        var (kept, kx, ky) = LayerEffects.Render(source, [outline, plain]);
        var blue = new Color32(0, 0, 255);
        // 바깥 외곽선(파랑 고리)만 기준 영역: 옮긴 고리는 기준 영역 밖(아래)에 그림 뒤로 남고, 기준 영역 안(고리 자리)은 그대로
        Assert.Equal((-1, -1, 6, 9), (mx, my, moved.Width, moved.Height));
        Assert.Equal(blue, moved[2, 7]);   // 고리 아래 칸 (2, 4) → (2, 7)
        Assert.Equal(blue, moved[1, 2]);   // 고리 왼쪽 칸은 그대로
        Assert.Equal(new Color32(200, 40, 40), moved[2, 2]); // 원본 자리는 그대로(옮긴 픽셀은 그림 뒤)
        // 기본 방식: 클리핑되면 넓히지 않고, 기준 영역 안만 옮긴 결과(고리 왼쪽 칸 위 3칸은 이미지 밖 = 투명)
        Assert.Equal((-1, -1, 6, 6), (kx, ky, kept.Width, kept.Height));
        Assert.Equal(Color32.Transparent, kept[1, 2]);
        Assert.Equal(new Color32(200, 40, 40), kept[2, 2]);
    }

    [Fact]
    public void Erasing_script_removes_erased_pixels_from_the_clip_area()
    {
        // 원본 지우기 외곽선 뒤 클리핑된 효과는 선에만 입혀진다(기본 효과와 같음)
        var program = EffectParity.Official("outline");
        var tint = new ColorOverlayEffect { Color = new Color32(0, 255, 0), ClipToPrevious = true };
        foreach (var inside in new[] { false, true })
        {
            var hide = new OutlineEffect { HideOriginal = true, Inside = inside, Thickness = 2 };
            EffectParity.AssertSameLists([hide, tint], [EffectParity.ToScript(hide, program), tint], frameCount: 1, label: $"inside {inside}");
        }
        var shown = EffectParity.ToScript(new OutlineEffect(), program);
        Assert.False(ScriptDefinition.Definition.ErasesSource(shown));
        Assert.True(ScriptDefinition.Definition.ErasesSource(EffectParity.ToScript(new OutlineEffect { HideOriginal = true }, program)));
    }

    /// <summary>그림을 아래로 3px 옮기는 스크립트(여백 아래 3).</summary>
    private static string MoverSource(bool clips) =>
        "effect({ name: 'mover " + clips + "', clipsBySource: " + (clips ? "true" : "false") + ", padding: [0, 0, 0, 3], render(img) {\n"
        + "  const w = img.width, h = img.height, d = img.data;\n"
        + "  for (let y = h - 1; y >= 0; y--) for (let x = 0; x < w; x++) for (let c = 0; c < 4; c++) {\n"
        + "    const i = (y * w + x) * 4 + c;\n"
        + "    d[i] = y >= 3 ? d[((y - 3) * w + x) * 4 + c] : 0;\n"
        + "  }\n"
        + "} });";

    private static ScriptEffectProgram Parse(string source)
    {
        var result = ScriptEffectProgram.Parse(source);
        Assert.True(result.Success, string.Join("\n", result.Errors));
        return result.Program;
    }

    /// <summary>
    /// 무거운 묶음(Scale2x로 8배 키우는 회전, 픽셀마다 그라데이션을 고르는 외곽선)에 쓰는 입력: 기본 입력에서 병아리 둘을 뺀 것 —
    /// 같은 모양 갈래를 지나면서 Jint 길로 한 시험이 몇 초 안에 끝나게.
    /// </summary>
    private static IEnumerable<EffectParity.Input> LightInputs => EffectParity.Inputs.Where(i => i.Name is not ("chick knight" or "chick wizard"));

    [Theory]
    [InlineData("outline", "Effect_Outline")]
    [InlineData("reflection", "Effect_Reflection")]
    [InlineData("rotate", "Effect_Rotate")]
    public void Official_names_match_the_built_in_effect_names(string id, string nameKey)
    {
        var program = EffectParity.Official(id);
        foreach (var culture in new[] { "ko", "en" })
        {
            var info = System.Globalization.CultureInfo.GetCultureInfo(culture);
            Assert.Equal(Localization.DomainStrings.ResourceManager.GetString(nameKey, info), program.NameFor(info));
        }
        Assert.True(program.ExecutionPath.Compiled, $"{id}: {program.ExecutionPath.Reason}");
    }

    // ───────────── 반사 ─────────────

    [Theory]
    [InlineData(ReflectionDirection.Down, 0, 50, 50, 0, false, 0x004682E6u, 0)]
    [InlineData(ReflectionDirection.Up, 3, 100, 100, 20, false, 0x80FF2020u, 1)]
    [InlineData(ReflectionDirection.Left, 1, 37, 80, 80, true, 0xFF00FF00u, 2)]
    [InlineData(ReflectionDirection.Right, 32, 1, 0, 100, false, 0x10203040u, 3)]
    [InlineData(ReflectionDirection.Down, 2, 100, 100, 100, true, 0x00000000u, 3)]
    [InlineData(ReflectionDirection.Right, 0, 64, 35, 0, true, 0xC0FFFFFFu, 1)]
    [InlineData(ReflectionDirection.Up, 0, 1, 0, 0, false, 0xFF000000u, 0)]
    public void Reflection_port_matches(ReflectionDirection direction, int gap, int height, int start, int end, bool dither, uint argb, int wave)
        => EffectParity.AssertParity(new ReflectionEffect
        {
            Direction = direction, Gap = gap, Height = height, OpacityStart = start, OpacityEnd = end, DitherFade = dither, Tint = new Color32(argb), Wave = wave,
        }, EffectParity.Official("reflection"), frameCount: 3);

    [Theory]
    [InlineData(ReflectionDirection.Down, false)]
    [InlineData(ReflectionDirection.Up, true)]
    [InlineData(ReflectionDirection.Left, false)]
    [InlineData(ReflectionDirection.Right, true)]
    public void Reflection_port_matches_with_gradients(ReflectionDirection direction, bool dither)
        => EffectParity.AssertParity(new ReflectionEffect { Direction = direction, Gap = 2, Height = 70, OpacityStart = 90, OpacityEnd = 30, DitherFade = dither, Wave = 1 },
            EffectParity.Official("reflection"), gradientSlot: nameof(ReflectionEffect.Tint), frameCount: 2);

    [Fact]
    public void Reflection_port_matches_in_chains()
    {
        var program = EffectParity.Official("reflection");
        var down = new ReflectionEffect { Gap = 1, Height = 80, OpacityStart = 70, Wave = 2 };
        var side = new ReflectionEffect { Direction = ReflectionDirection.Left, Height = 60, DitherFade = true, ClipToPrevious = true };
        var hide = new OutlineEffect { HideOriginal = true, Thickness = 2, Color = new Color32(250, 200, 30) };
        // 반사에 클리핑된 반사, 원본 지우기 외곽선에 클리핑된 반사, 반사 둘 뒤 그림자
        EffectParity.AssertSameLists([down, side], [EffectParity.ToScript(down, program), EffectParity.ToScript(side, program)], frameCount: 2);
        EffectParity.AssertSameLists([hide, side], [hide, EffectParity.ToScript(side, program)], frameCount: 2);
        EffectParity.AssertSameLists([down, down with { Direction = ReflectionDirection.Right }, new ShadowEffect { OffsetX = -2, OffsetY = 3 }],
            [EffectParity.ToScript(down, program), EffectParity.ToScript(down with { Direction = ReflectionDirection.Right }, program), new ShadowEffect { OffsetX = -2, OffsetY = 3 }],
            frameCount: 2);
    }

    // ───────────── 외곽선 ─────────────

    [Theory]
    [InlineData(1, false, false, false, 0xFF000000u)]
    [InlineData(2, true, false, false, 0xFFFF8020u)]
    [InlineData(3, false, false, true, 0x80FFFFFFu)]
    [InlineData(3, true, true, false, 0xC0103080u)]
    [InlineData(2, false, true, true, 0xFF20FF40u)]
    [InlineData(1, true, true, true, 0x00FFFFFFu)]
    [InlineData(1, false, false, false, 0x00000000u)]
    public void Outline_port_matches_fixed_color(int thickness, bool diagonal, bool inside, bool hide, uint argb)
        => EffectParity.AssertParity(new OutlineEffect { Thickness = thickness, Diagonal = diagonal, Inside = inside, HideOriginal = hide, Color = new Color32(argb) },
            EffectParity.Official("outline"), frameCount: 2);

    [Theory]
    [InlineData(-40, 1, false, false, false)]
    [InlineData(-100, 3, true, false, true)]
    [InlineData(100, 2, false, false, false)]
    [InlineData(37, 3, false, false, false)]
    [InlineData(-1, 1, true, false, false)]
    [InlineData(0, 2, true, false, false)]
    [InlineData(-65, 2, false, true, false)]
    [InlineData(80, 3, true, true, true)]
    [InlineData(0, 1, false, true, false)]
    public void Outline_port_matches_shade(int amount, int thickness, bool diagonal, bool inside, bool hide)
        => EffectParity.AssertParity(new OutlineEffect
        {
            ColorMode = OutlineColorMode.Shade, ShadeAmount = amount, Thickness = thickness, Diagonal = diagonal, Inside = inside, HideOriginal = hide,
        }, EffectParity.Official("outline"), frameCount: 2);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Outline_port_matches_with_gradients(bool inside)
        => EffectParity.AssertParity(new OutlineEffect { Thickness = 2, Diagonal = true, Inside = inside }, EffectParity.Official("outline"),
            gradientSlot: nameof(OutlineEffect.Color), frameCount: 2, inputs: LightInputs);

    [Fact]
    public void Outline_shade_matches_on_many_colors()
    {
        // 모든 색 영역을 고루 지나는 그림(무지개·회색·어두운·밝은 색, 반투명 포함)으로 OKLab 명암 계산을 넓게 본다
        var image = new PixelImage(48, 48);
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
            {
                if ((x * 5 + y * 3) % 7 == 0) continue;
                image[x, y] = new Color32((byte)(x * 5 + y * 37), (byte)(y * 11 + x * x), (byte)(255 - x * 3 - y * 2), (byte)((x + y) % 3 == 0 ? 255 : 40 + x * 4));
            }
        var inputs = new[] { new EffectParity.Input("colors", [image]) };
        var program = EffectParity.Official("outline");
        foreach (int amount in new[] { -100, -73, -40, -7, 1, 13, 50, 99, 100 })
            foreach (var inside in new[] { false, true })
            {
                var e = new OutlineEffect { ColorMode = OutlineColorMode.Shade, ShadeAmount = amount, Inside = inside, Diagonal = true };
                EffectParity.AssertSameLists([e], [EffectParity.ToScript(e, program)], frameCount: 1, inputs: inputs, label: $"{amount} inside {inside}");
            }
    }

    // ───────────── 회전 ─────────────

    [Theory]
    [InlineData(360, SquashAnchor.Pivot)]
    [InlineData(90, SquashAnchor.Middle)]
    [InlineData(-270, SquashAnchor.BottomCenter)]
    [InlineData(45, SquashAnchor.BottomCenter)]
    [InlineData(-1440, SquashAnchor.Middle)]
    [InlineData(1440, SquashAnchor.Pivot)]
    [InlineData(0, SquashAnchor.Pivot)]
    [InlineData(7, SquashAnchor.Middle)]
    [InlineData(-180, SquashAnchor.Pivot)]
    public void Rotate_port_matches(int angle, SquashAnchor anchor)
        => EffectParity.AssertParity(new RotateEffect { Angle = angle, Anchor = anchor }, EffectParity.Official("rotate"), frameCount: 4, inputs: LightInputs);

    [Theory]
    [InlineData(137, SquashAnchor.Middle)]
    [InlineData(-90, SquashAnchor.BottomCenter)]
    [InlineData(-33, SquashAnchor.BottomCenter)]
    [InlineData(-33, SquashAnchor.Pivot)]
    public void Rotate_port_matches_on_large_images(int angle, SquashAnchor anchor)
    {
        // 보이는 범위의 긴 변이 128·256·512px를 넘으면 Scale2x 단계가 2·1·0번으로 준다. 피벗 여백(대각선)은 커서 530px 그림은 빼고 본다 —
        // 그만큼 넓힌 그림(약 170만 픽셀)은 Jint 길에서 시간 한도(2초)를 넘는다(빠른 경로는 0.1초)
        var inputs = new List<EffectParity.Input>();
        foreach (var (w, h) in anchor == SquashAnchor.Pivot ? new[] { (140, 9), (13, 300) } : new[] { (140, 9), (13, 300), (530, 5) })
        {
            var image = new PixelImage(w, h);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if ((x / 3 + y) % 4 != 1)
                        image[x, y] = new Color32((byte)(x * 7), (byte)(y * 13), (byte)((x + y) % 2 * 200), (byte)(x % 5 == 0 ? 120 : 255));
            inputs.Add(new($"{w}x{h}", [image]));
        }
        var e = new RotateEffect { Angle = angle, Anchor = anchor };
        EffectParity.AssertSameLists([e], [EffectParity.ToScript(e, EffectParity.Official("rotate"))], frameCount: 3, inputs: inputs,
            fast: EffectParity.Official("rotate"));
    }
}
