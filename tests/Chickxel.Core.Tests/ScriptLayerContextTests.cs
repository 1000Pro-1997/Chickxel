using System.Drawing;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 스크립트가 레이어 고르기 설정(<c>type: "layer"</c>)으로 고른 레이어와 효과 원본 배치를 읽는 API(<c>ctx.layer</c>·<c>ctx.layerWithEffects</c>·<c>ctx.layerEffects</c>,
/// <c>ctx.center</c>·<c>ctx.slice</c>): 프로젝트 렌더에서의 픽셀·자리, 이름 찾기, 겹침 규칙, 효과 이름, 설정 값 저장, 캐시 무효, 읽기 판정, 빠른 경로.
/// 신뢰 결정은 프로세스 전체 상태라 시험마다 원문 끝에 주석을 붙여 해시를 따로 쓴다.
/// </summary>
public class ScriptLayerContextTests
{
    private const int W = 8, H = 6, Pad = 2;

    private static readonly Color32 Red = new(255, 0, 0), Blue = new(0, 0, 255), Green = new(0, 255, 0), Mark = new(255, 0, 255), Missing = new(7, 7, 7, 7);

    private static string Trusted(string source, [CallerMemberName] string test = "")
    {
        var hash = ScriptLibrary.Add(source + "\n// " + test + "\n");
        ScriptLibrary.SetTrust(hash, true);
        return hash;
    }

    /// <summary>레이어 고르기 설정 <c>target</c>에 <paramref name="name"/>을 고른 값.</summary>
    private static ScriptSettingValues Target(string name) => ScriptSettingValues.From([KeyValuePair.Create("target", (object)name)]);

    /// <summary>
    /// 설정 <c>target</c>(레이어 고르기)으로 고른 레이어의 <c>ctx.CALL(KEY)</c> 그림을 img에 그대로 옮기는 효과(여백 2px). 없으면(null) 모든 값을 7로.
    /// <c>n</c>은 레이어가 아닌 설정(그 키로 부르면 null).
    /// </summary>
    private static ScriptEffect Copier(string call, string target, string key = "\"target\"", [CallerMemberName] string test = "") => new()
    {
        ScriptHash = Trusted($$"""
            effect({ name: "copy {{call}}", padding: [{{Pad}}, {{Pad}}, {{Pad}}, {{Pad}}],
              settings: [{ key: "target", label: "Target", type: "layer" }, { key: "n", type: "int", default: 1 }],
              render(img, ctx, s) {
                const o = ctx.{{call}}({{key}});
                for (let i = 0; i < img.data.length; i++) img.data[i] = o ? o.data[i] : 7;
              } });
            """, test),
        Settings = Target(target),
    };

    /// <summary>설정 <c>target</c>으로 고른 레이어의 <c>ctx.CALL("target")</c> 그림이 불투명한 자리를 표시 색으로 칠하는 효과(여백 없음).</summary>
    private static ScriptEffect Marker(string call, string target, [CallerMemberName] string test = "") => new()
    {
        ScriptHash = Trusted($$"""
            effect({ name: "mark {{call}}", settings: [{ key: "target", label: "Target", type: "layer" }], render(img, ctx, s) {
              const o = ctx.{{call}}("target");
              if (!o) return;
              for (let i = 0; i < img.data.length; i += 4)
                if (o.data[i + 3] > 0) { img.data[i] = 255; img.data[i + 1] = 0; img.data[i + 2] = 255; img.data[i + 3] = 255; }
            } });
            """, test),
        Settings = Target(target),
    };

    private static PixelImage Dot(int width, int height, int x, int y, Color32 color)
    {
        var image = new PixelImage(width, height);
        image[x, y] = color;
        return image;
    }

    /// <summary>레이어 효과를 프로젝트의 이 프레임 값으로 입힌 그림(<see cref="SpriteProject.EffectsAt"/> — 내보내기·AI 도구와 같은 길).</summary>
    private static PixelImage Rendered(SpriteProject p, Layer layer, int frame = 0)
    {
        var cel = p.GetCel(layer, p.Frames[frame])!;
        return LayerEffects.Render(cel, p.EffectsAt(layer, frame)).Image;
    }

    /// <summary>캔버스 그림 <paramref name="canvas"/>(캔버스 (0,0)이 <paramref name="origin"/>)를 여백 넣은 효과 그림 크기로 맞춘 기댓값.</summary>
    private static PixelImage Aligned(PixelImage canvas, int width, int height, Point origin)
    {
        var image = new PixelImage(width, height);
        image.Blit(canvas, origin.X, origin.Y);
        return image;
    }

    private static void AssertMissing(PixelImage image) => Assert.All(image.Pixels, c => Assert.Equal(Missing, c));

    /// <summary>bottom(빨간 점)·middle(읽는 레이어)·top(파란 점) 세 레이어.</summary>
    private static (SpriteProject P, Layer Bottom, Layer Middle, Layer Top) ThreeLayers()
    {
        var p = new SpriteProject(W, H);
        var bottom = p.Layers[0];
        p.SetLayerProperties(0, name: "bottom");
        var middle = p.AddLayer(name: "middle");
        var top = p.AddLayer(name: "top");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, Dot(W, H, 4, 4, Green));
        p.SetCel(2, 0, Dot(W, H, 5, 2, Blue));
        return (p, bottom, middle, top);
    }

    [Fact]
    public void Layer_is_the_raw_cel_of_the_chosen_layer_aligned_to_the_padded_image()
    {
        var (p, _, middle, _) = ThreeLayers();
        // 고른 레이어 효과는 원본 읽기에 들어가지 않는다
        p.SetLayerEffects(0, [new ColorOverlayEffect { Color = Blue, Amount = 100 }]);

        p.SetLayerEffects(1, [Copier("layer", "bottom")]);
        var bottom = Rendered(p, middle);
        Assert.Equal((W + 2 * Pad, H + 2 * Pad), (bottom.Width, bottom.Height));
        Assert.True(Aligned(Dot(W, H, 1, 1, Red), bottom.Width, bottom.Height, new Point(Pad, Pad)).ContentEquals(bottom));

        p.SetLayerEffects(1, [Copier("layer", "top")]);
        var top = Rendered(p, middle);
        Assert.True(Aligned(Dot(W, H, 5, 2, Blue), top.Width, top.Height, new Point(Pad, Pad)).ContentEquals(top));
    }

    [Fact]
    public void Layer_with_effects_renders_the_chosen_layer_effects_for_this_frame_including_spill()
    {
        var (p, bottom, middle, top) = ThreeLayers();
        var outline = new OutlineEffect { Color = new Color32(10, 20, 30) };
        p.SetLayerEffects(0, [outline]);
        p.SetLayerEffects(2, [new ColorOverlayEffect { Color = Red, Amount = 100 }]);

        PixelImage Expected(Layer chosen, int width, int height)
        {
            var (fx, dx, dy) = LayerEffects.Render(p.GetCel(chosen, p.Frames[0])!, p.EffectsAt(chosen, 0));
            var expected = new PixelImage(width, height);
            expected.Composite(fx, Pad + dx, Pad + dy);
            return expected;
        }

        p.SetLayerEffects(1, [Copier("layerWithEffects", "bottom")]);
        var below = Rendered(p, middle);
        Assert.True(Expected(bottom, below.Width, below.Height).ContentEquals(below));
        Assert.Equal(outline.Color, below[Pad + 0, Pad + 1]); // 셀 밖으로 번진 외곽선도 같은 자리에

        p.SetLayerEffects(1, [Copier("layerWithEffects", "top")]);
        var above = Rendered(p, middle);
        Assert.True(Expected(top, above.Width, above.Height).ContentEquals(above));
        Assert.NotEqual(Blue, above[Pad + 5, Pad + 2]);
    }

    [Fact]
    public void Project_render_follows_the_chosen_name_and_bad_names_or_keys_give_null()
    {
        var p = new SpriteProject(W, H);
        p.SetLayerProperties(0, name: "ground");
        var reader = p.AddLayer(name: "reader");
        p.AddLayer(name: "sky");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, new PixelImage(W, H));
        p.SetCel(2, 0, Dot(W, H, 5, 2, Blue));
        var ground = Aligned(Dot(W, H, 1, 1, Red), W + 2 * Pad, H + 2 * Pad, new Point(Pad, Pad));
        var sky = Aligned(Dot(W, H, 5, 2, Blue), W + 2 * Pad, H + 2 * Pad, new Point(Pad, Pad));

        var copier = Copier("layer", "ground");
        p.SetLayerEffects(1, [copier]);
        Assert.True(ground.ContentEquals(Rendered(p, reader)));

        // 설정 값만 바꾸면 다른 레이어를 본다(앞뒤 공백은 지운 이름)
        p.SetLayerEffects(1, [copier with { Settings = Target("sky") }]);
        Assert.True(sky.ContentEquals(Rendered(p, reader)));
        p.SetLayerEffects(1, [copier with { Settings = Target("  ground  ") }]);
        Assert.True(ground.ContentEquals(Rendered(p, reader)));

        // 비운 값·없는 이름은 오류 없이 null
        p.SetLayerEffects(1, [copier with { Settings = Target("") }]);
        AssertMissing(Rendered(p, reader));
        p.SetLayerEffects(1, [copier with { Settings = Target("nobody") }]);
        AssertMissing(Rendered(p, reader));

        // 모르는 키·레이어가 아닌 설정 키·문자열이 아닌 키·키 없음도 null
        foreach (string key in new[] { "\"zzz\"", "\"n\"", "1", "" })
        {
            p.SetLayerEffects(1, [Copier("layer", "ground", key)]);
            AssertMissing(Rendered(p, reader));
            p.SetLayerEffects(1, [Copier("layerWithEffects", "ground", key)]);
            AssertMissing(Rendered(p, reader));
        }

        // 그리는 중 s.키는 고른 이름 그대로(글)
        var probe = new ScriptEffect
        {
            ScriptHash = Trusted("""
                effect({ name: "name probe", settings: [{ key: "target", type: "layer" }, { key: "n", type: "int", default: 1 }], render(img, ctx, s) {
                  img.data[0] = typeof s.target === "string" ? s.target.length : 99;
                  img.data[1] = s.target === "sky" ? 1 : 2;
                  img.data[2] = 0; img.data[3] = 255;
                } });
                """),
            Settings = Target(" sky "),
        };
        p.SetLayerEffects(1, [probe]);
        Assert.Equal(new Color32(3, 1, 0, 255), Rendered(p, reader)[0, 0]);
        p.SetLayerEffects(1, [probe with { Settings = ScriptSettingValues.Empty }]);
        Assert.Equal(new Color32(0, 2, 0, 255), Rendered(p, reader)[0, 0]);
    }

    [Fact]
    public void Name_finds_the_nearest_other_layer_ties_go_lower_and_own_layer_is_skipped()
    {
        var p = new SpriteProject(W, H);
        var reader = p.AddLayer(name: "reader");
        p.AddLayer(name: "x2");
        p.AddLayer(name: "x3");
        p.SetLayerProperties(0, name: "ground");
        p.SetLayerProperties(2, name: "ground"); // 읽는 레이어(1)에서 0과 2가 같은 거리 — 아래쪽(0)
        p.SetLayerProperties(3, name: "ground");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, new PixelImage(W, H));
        p.SetCel(2, 0, Dot(W, H, 5, 2, Blue));
        p.SetCel(3, 0, Dot(W, H, 6, 4, Green));

        p.SetLayerEffects(1, [Copier("layer", "ground")]);
        var image = Rendered(p, reader);
        Assert.True(Aligned(Dot(W, H, 1, 1, Red), image.Width, image.Height, new Point(Pad, Pad)).ContentEquals(image));

        // 가장 가까운 것이 이름을 바꾸면 다음으로 가까운 것, 숨겨도 센다
        p.SetLayerProperties(0, name: "base");
        p.SetLayerProperties(2, visible: false);
        Assert.True(Aligned(Dot(W, H, 5, 2, Blue), image.Width, image.Height, new Point(Pad, Pad)).ContentEquals(Rendered(p, reader)));

        // 자기 레이어는 찾지 않는다: 같은 이름의 다른 레이어가 생겨야 그것을 준다
        p.SetLayerEffects(1, [Copier("layer", "reader")]);
        AssertMissing(Rendered(p, reader));
        p.SetLayerProperties(3, name: "reader");
        var same = Rendered(p, reader);
        Assert.Equal(Green, same[Pad + 6, Pad + 4]);
        Assert.Equal(1, same.Pixels.Count(c => c.A != 0));
    }

    [Fact]
    public void Missing_cels_give_null_and_hidden_layers_still_count()
    {
        var p = new SpriteProject(W, H);
        p.SetCel(0, 0, Dot(W, H, 0, 0, Red));
        p.SetLayerProperties(0, name: "only");
        var only = p.Layers[0];
        p.SetLayerEffects(0, [Copier("layer", "other")]);
        AssertMissing(Rendered(p, only));

        // 고른 레이어가 있어도 이 프레임 셀이 없으면 null(효과까지도)
        var hidden = p.AddLayer(name: "other");
        AssertMissing(Rendered(p, only));
        p.SetLayerEffects(0, [Copier("layerWithEffects", "other")]);
        AssertMissing(Rendered(p, only));

        // 숨긴 레이어도 건너뛰지 않고 그 그림을 준다
        p.SetCel(1, 0, Dot(W, H, 3, 3, Blue));
        p.SetLayerProperties(1, visible: false);
        Assert.Equal(Blue, Rendered(p, only)[Pad + 3, Pad + 3]);
        p.SetLayerEffects(0, [Copier("layer", "other")]);
        Assert.Equal(Blue, Rendered(p, only)[Pad + 3, Pad + 3]);
        Assert.False(hidden.Visible);
    }

    [Fact]
    public void Parts_and_groups_are_aligned_through_part_offsets_and_the_group_holding_the_reader_is_not_found()
    {
        var p = new SpriteProject(W, H);
        var group = p.AddGroup("g");
        var lower = p.AddPart(group, "lower", 3, 3, new Point(1, 1));
        var upper = p.AddPart(group, "upper", 3, 3, new Point(4, 2));
        p.SetCel(p.IndexOf(lower), 0, Dot(3, 3, 2, 0, Red)); // 캔버스 (3, 1)
        p.SetCel(p.IndexOf(upper), 0, Dot(3, 3, 0, 0, Blue)); // 캔버스 (4, 2)
        var reader = p.AddLayer(name: "reader");
        p.SetCel(p.IndexOf(reader), 0, new PixelImage(W, H));

        // 부위가 다른 부위를: 부위 자리를 거쳐 맞춘다(upper 원본 왼쪽 위 = 캔버스 (4, 2) = 그림 (2, 2) → 캔버스 (3, 1)은 (1, 1))
        p.SetLayerEffects(p.IndexOf(upper), [Copier("layer", "lower")]);
        var part = Rendered(p, upper);
        Assert.Equal((3 + 2 * Pad, 3 + 2 * Pad), (part.Width, part.Height));
        Assert.Equal(Red, part[1, 1]);
        Assert.Equal(1, part.Pixels.Count(c => c.A != 0));

        // 자기를 담은 그룹은 찾지 않는다(그 그림에 자기가 들어 있음)
        p.SetLayerEffects(p.IndexOf(upper), [Copier("layer", "g")]);
        AssertMissing(Rendered(p, upper));

        // 최상위 레이어가 그룹을: 보이는 부위를 합친 효과 없는 그림(부위 효과는 들어가므로 7로 채우는 upper 효과는 뺀다)
        p.SetLayerEffects(p.IndexOf(upper), []);
        p.SetLayerEffects(p.IndexOf(reader), [Copier("layer", "g")]);
        var image = Rendered(p, reader);
        Assert.Equal(Red, image[Pad + 3, Pad + 1]);
        Assert.Equal(Blue, image[Pad + 4, Pad + 2]);
        Assert.Equal(2, image.Pixels.Count(c => c.A != 0));

        // 최상위 레이어가 부위를: 부위 자리 그대로 캔버스에
        p.SetLayerEffects(p.IndexOf(reader), [Copier("layer", "lower")]);
        var lowerImage = Rendered(p, reader);
        Assert.Equal(Red, lowerImage[Pad + 3, Pad + 1]);
        Assert.Equal(1, lowerImage.Pixels.Count(c => c.A != 0));
        Assert.NotNull(p.Flatten(0));
    }

    [Fact]
    public void Group_chosen_inside_another_layer_render_gives_null_image()
    {
        var p = new SpriteProject(W, H);
        var group = p.AddGroup("g");
        var lower = p.AddPart(group, "lower", 3, 3, new Point(1, 1));
        p.SetCel(p.IndexOf(lower), 0, Dot(3, 3, 2, 0, Red)); // 캔버스 (3, 1)
        var x = p.AddLayer(name: "x");
        p.SetCel(p.IndexOf(x), 0, Dot(W, H, 6, 4, Blue));
        p.SetLayerEffects(p.IndexOf(x), [Marker("layer", "g")]);
        var y = p.AddLayer(name: "y");
        p.SetCel(p.IndexOf(y), 0, new PixelImage(W, H));
        p.SetLayerEffects(p.IndexOf(y), [Copier("layerWithEffects", "x")]);

        // x를 따로 그리면 그룹 그림이 있어 표시가 생긴다
        Assert.Equal(Mark, Rendered(p, x)[3, 1]);
        // y가 효과까지 읽는 x 안에서는 고른 그룹이 null → x 셀만
        var seen = Rendered(p, y);
        Assert.Equal(Blue, seen[Pad + 6, Pad + 4]);
        Assert.Equal(1, seen.Pixels.Count(c => c.A != 0));
    }

    [Fact]
    public void Two_layers_reading_each_other_with_effects_render_deterministically_without_recursion()
    {
        var p = new SpriteProject(W, H);
        var a = p.Layers[0];
        p.SetLayerProperties(0, name: "a");
        p.AddLayer(name: "between");
        var b = p.AddLayer(name: "b");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(2, 0, Dot(W, H, 6, 4, Blue));
        var overlay = new ColorOverlayEffect { Color = new Color32(0, 200, 0), Amount = 100 };
        p.SetLayerEffects(0, [overlay, Marker("layerWithEffects", "b")]);
        p.SetLayerEffects(2, [Marker("layerWithEffects", "a")]);

        // b가 보는 a: 겹침 규칙으로 a의 읽는 효과는 건너뛰고 색 덧칠은 입힘 → a의 (1, 1)만 불투명
        var seenByB = Rendered(p, b);
        Assert.Equal(Mark, seenByB[1, 1]);
        Assert.Equal(Blue, seenByB[6, 4]);
        Assert.Equal(2, seenByB.Pixels.Count(c => c.A != 0));
        // a가 보는 b: b의 읽는 효과를 건너뛴 b 셀 → (6, 4)만
        var seenByA = Rendered(p, a);
        Assert.Equal(Mark, seenByA[6, 4]);
        Assert.NotEqual(Mark, seenByA[1, 1]); // a 자신(덧칠한 셀)은 표시 없음
        Assert.Equal(2, seenByA.Pixels.Count(c => c.A != 0));
        var first = p.Flatten(0);
        Assert.True(first.ContentEquals(p.Flatten(0)));
        Assert.Equal(Mark, first[1, 1]);
        Assert.Equal(Blue, first[6, 4]);
    }

    [Fact]
    public void Effect_names_list_only_effects_of_the_chosen_layer_enabled_on_the_frame()
    {
        var p = new SpriteProject(W, H);
        p.DuplicateFrame(0);
        var reader = p.AddLayer(name: "reader");
        var third = p.AddLayer(name: "other");
        var fourth = p.AddLayer(name: "far");
        p.SetLayerProperties(0, name: "base");
        p.SetLayerProperties(2, name: "base"); // 읽는 레이어(1)에서 0과 2가 같은 거리 — 아래쪽(0)
        p.SetLayerProperties(3, name: "base");
        var outline = new OutlineEffect();
        var shadow = new ShadowEffect { Enabled = false };
        var overlay = new ColorOverlayEffect { Frames = new FrameRange(1, 1) };
        p.SetLayerEffects(0, [outline, shadow, overlay]);
        p.SetLayerEffects(2, [new ColorOverlayEffect()]);
        p.SetLayerEffects(3, [new OutlineEffect(), new OutlineEffect()]);
        string expected0 = outline.DisplayName, expected1 = outline.DisplayName + "|" + overlay.DisplayName;
        var script = new ScriptEffect
        {
            ScriptHash = Trusted($$"""
                effect({ name: "names",
                  settings: [{ key: "target", type: "layer", default: "base" }, { key: "empty", type: "layer" },
                             { key: "missing", type: "layer", default: "nobody" }, { key: "n", type: "int", default: 3 }],
                  render(img, ctx, s) {
                    const want = ctx.frame === 0 ? "{{expected0}}" : "{{expected1}}";
                    img.data[0] = ctx.layerEffects("target").join("|") === want ? 1 : 2;
                    img.data[1] = ctx.layerEffects("empty").length === 0 && ctx.layerEffects("missing").length === 0 ? 1 : 2;
                    img.data[2] = ctx.layerEffects("n").length === 0 && ctx.layerEffects("zzz").length === 0 && ctx.layerEffects(5).length === 0 ? 1 : 2;
                    img.data[3] = 255;
                    img.data[4] = Array.isArray(ctx.layerEffects("target")) && Array.isArray(ctx.layerEffects()) ? 1 : 2;
                    img.data[7] = 255;
                  } });
                """),
        };
        p.SetLayerEffects(1, [script]);

        for (int f = 0; f < 2; f++)
        {
            p.SetCel(1, f, new PixelImage(W, H));
            var image = Rendered(p, reader, f);
            Assert.Equal(new Color32(1, 1, 1, 255), image[0, 0]);
            Assert.Equal(1, image[1, 0].R);
        }

        // 고른 이름만 담는다
        var layers = p.ScriptLayersAt(reader, 0)!;
        Assert.Equal([outline.DisplayName], layers.EffectsByName("base"));
        Assert.Empty(layers.EffectsByName("nobody"));
        Assert.Empty(layers.EffectsByName("other"));
        // 효과 목록을 따로 주면 그 목록 기준(효과 창 미리보기): 자기 레이어는 빼고 같은 이름 중 가장 가까운 것
        Assert.Equal([new ColorOverlayEffect().DisplayName], p.ScriptLayersAt(fourth, 0, [script])!.EffectsByName("base"));
        Assert.Null(p.ScriptLayersAt(fourth, 0));
        Assert.Equal("base", third.Name);
    }

    private const string LayoutProbe = """
        effect({ name: "layout", padding: [2, 2, 2, 2], render(img, ctx, s) {
          img.data[0] = ctx.center.x * 10; img.data[1] = ctx.center.y * 10; img.data[2] = 0; img.data[3] = 255;
          const sl = ctx.slice;
          img.data[4] = sl ? sl.left : 200; img.data[5] = sl ? sl.top : 200; img.data[6] = sl ? sl.right : 200; img.data[7] = sl ? sl.bottom : 200;
        } });
        """;

    [Fact]
    public void Center_and_slice_describe_the_canvas_or_the_part()
    {
        var hash = Trusted(LayoutProbe);
        var p = new SpriteProject(W, H);
        p.SetCel(0, 0, new PixelImage(W, H));
        p.SetLayerEffects(0, [new ScriptEffect { ScriptHash = hash }]);

        // 테두리 없음 → null, 가운데 = 원본(2, 2, 8, 6) 가운데 (6, 5)
        var none = Rendered(p, p.Layers[0]);
        Assert.Equal(new Color32(60, 50, 0, 255), none[0, 0]);
        Assert.Equal(new Color32(200, 200, 200, 200), none[1, 0]);

        p.Sprite.Border = new SpriteBorder(1, 2, 3, 1);
        Assert.Equal(new Color32(1, 2, 3, 1), Rendered(p, p.Layers[0])[1, 0]);
        Assert.Equal(new SpriteBorder(1, 2, 3, 1), p.ScriptLayersAt(p.Layers[0], 0)!.Slice);
        p.Sprite.Border = new SpriteBorder(5, 0, 5, 0); // 원본보다 넓음 → 맞지 않음
        Assert.Equal(new Color32(200, 200, 200, 200), Rendered(p, p.Layers[0])[1, 0]);

        var group = p.AddGroup("g");
        var part = p.AddPart(group, "part", 4, 4, new Point(3, 1));
        p.SetCel(p.IndexOf(part), 0, new PixelImage(4, 4));
        p.SetPartBorder(p.IndexOf(part), new SpriteBorder(1, 1, 1, 1));
        p.SetLayerEffects(p.IndexOf(part), [new ScriptEffect { ScriptHash = hash }]);
        var partImage = Rendered(p, part);
        Assert.Equal(new Color32(40, 40, 0, 255), partImage[0, 0]); // 원본 (2, 2, 4, 4) 가운데 (4, 4)
        Assert.Equal(new Color32(1, 1, 1, 1), partImage[1, 0]);
    }

    [Fact]
    public void Without_a_project_layers_are_null_names_empty_center_is_the_source_center_and_slice_is_null()
    {
        var program = ScriptLibrary.Runnable(Trusted("""
            effect({ name: "alone", settings: [{ key: "target", type: "layer", default: "a" }], render(img, ctx, s) {
              img.data[0] = ctx.layer("target") === null && ctx.layerWithEffects("target") === null ? 1 : 2;
              img.data[1] = s.target === "a" ? 1 : 2;
              img.data[2] = ctx.layerEffects("target").length;
              img.data[3] = ctx.slice === null ? 1 : 2;
              img.data[4] = ctx.center.x * 10; img.data[5] = ctx.center.y * 10; img.data[6] = 0; img.data[7] = 0;
            } });
            """))!;
        var image = new PixelImage(4, 3);
        program.Render(image, new Rectangle(1, 1, 2, 1), ScriptFrameContext.Still);
        Assert.Equal(new Color32(1, 1, 0, 1), image[0, 0]);
        Assert.Equal(new Color32(20, 15, 0, 0), image[1, 0]);
    }

    [Fact]
    public void Fast_path_reads_center_and_falls_back_to_jint_for_slice_layer_functions_and_layer_setting_values()
    {
        var center = ScriptLibrary.Runnable(Trusted("""
            effect({ name: "center", render(img, ctx, s) {
              for (let i = 0; i < img.data.length; i += 4) { img.data[i] = ctx.center.x * 10; img.data[i + 1] = ctx.center.y * 10; img.data[i + 3] = 255; }
            } });
            """))!;
        Assert.True(center.ExecutionPath.Compiled);
        var fast = new PixelImage(5, 4);
        var jint = new PixelImage(5, 4);
        var source = new Rectangle(1, 1, 3, 2);
        center.Render(fast, source, ScriptFrameContext.Still);
        center.RenderOnJint(jint, source, ScriptFrameContext.Still, center.NormalizeSettings(null), CancellationToken.None);
        Assert.True(jint.ContentEquals(fast));
        Assert.Equal(new Color32(25, 20, 0, 255), fast[0, 0]);

        // 레이어 설정을 두기만 하면 빠른 경로 그대로(칸이 없어 뒤 설정 값도 제자리)
        var unread = ScriptLibrary.Runnable(Trusted("""
            effect({ name: "unread", settings: [{ key: "path", type: "layer", default: "ground" }, { key: "n", type: "int", default: 7 }, { key: "c", type: "color", default: "#102030" }],
              render(img, ctx, s) { for (let i = 0; i < img.data.length; i += 4) { img.data[i] = s.n; img.data[i + 1] = s.c.g; img.data[i + 3] = 255; } } });
            """))!;
        Assert.True(unread.ExecutionPath.Compiled, unread.ExecutionPath.Reason);
        fast = new PixelImage(5, 4);
        jint = new PixelImage(5, 4);
        unread.Render(fast, source, ScriptFrameContext.Still);
        unread.RenderOnJint(jint, source, ScriptFrameContext.Still, unread.NormalizeSettings(null), CancellationToken.None);
        Assert.True(jint.ContentEquals(fast));
        Assert.Equal(new Color32(7, 0x20, 0, 255), fast[0, 0]);

        const string Settings = "settings: [{ key: \"path\", type: \"layer\", default: \"ground\" }]";
        foreach (var (name, body) in new[]
                 {
                     ("ctx.slice", "img.data[0] = ctx.slice ? 1 : 0;"),
                     ("ctx.layer", "img.data[0] = ctx.layer(\"path\") ? 1 : 0;"),
                     ("ctx.layerWithEffects", "img.data[0] = ctx.layerWithEffects(\"path\") ? 1 : 0;"),
                     ("ctx.layerEffects", "img.data[0] = ctx.layerEffects(\"path\").length;"),
                     ("s.path", "img.data[0] = s.path ? 1 : 0;"),
                 })
        {
            var program = ScriptLibrary.Runnable(Trusted($$"""effect({ name: "x", {{Settings}}, render(img, ctx, s) { {{body}} } });""" + "\n// " + name))!;
            Assert.False(program.ExecutionPath.Compiled, name);
            Assert.Contains(name, program.ExecutionPath.Reason);
        }
    }

    [Fact]
    public void Render_cache_follows_the_chosen_layer_cel_and_effects_and_ignores_unrelated_layers()
    {
        var p = new SpriteProject(W, H);
        var ground = p.Layers[0];
        p.SetLayerProperties(0, name: "ground");
        p.AddLayer(name: "middle"); // 바로 아래가 아닌 레이어를 고른다
        var reader = p.AddLayer(name: "reader");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(2, 0, Dot(W, H, 6, 4, Blue));
        // 읽는 효과 뒤에 효과가 하나 더 있어야 중간 결과(효과 1까지)가 캐시에 담긴다
        p.SetLayerEffects(2, [Marker("layerWithEffects", "ground"), new OutlineEffect { Color = new Color32(1, 2, 3) }]);
        var cache = new EffectRenderCache(64L << 20);
        PixelImage Cached() => p.PlanFrame(0).Render(new RenderPlanOptions { Cache = cache, CheckpointLayer = reader });

        var before = Cached();
        Assert.True(before.ContentEquals(Cached()));
        Assert.True(cache.Bytes > 0);
        Assert.Equal(Mark, before[1, 1]);

        // 고르지 않은 레이어가 바뀌어도 열쇠는 그대로
        var key = p.ScriptLayersAt(reader, 0)!.Key;
        p.SetCel(1, 0, Dot(W, H, 2, 2, Green));
        p.SetLayerEffects(1, [new OutlineEffect()]);
        Assert.Equal(key, p.ScriptLayersAt(reader, 0)!.Key);

        // 고른 레이어의 효과만 바뀜(앱은 이때 효과 결과 캐시를 비우지 않는다)
        p.SetLayerEffects(0, [new OutlineEffect { Color = new Color32(9, 9, 9) }]);
        Assert.NotEqual(key, p.ScriptLayersAt(reader, 0)!.Key);
        var afterEffects = Cached();
        Assert.False(before.ContentEquals(afterEffects));
        Assert.True(p.Flatten(0).ContentEquals(afterEffects));
        Assert.Equal(Mark, afterEffects[0, 1]); // 고른 레이어 외곽선 자리도 표시됨

        // 고른 레이어의 셀이 새 그림으로 바뀜
        p.SetCel(0, 0, Dot(W, H, 3, 3, Red));
        var afterCel = Cached();
        Assert.False(afterEffects.ContentEquals(afterCel));
        Assert.True(p.Flatten(0).ContentEquals(afterCel));
        Assert.Equal(Mark, afterCel[3, 3]);
        Assert.Same(ground, p.Layers[0]);
    }

    [Fact]
    public void Top_layer_reading_the_bottom_layer_changes_when_the_bottom_layer_is_edited()
    {
        var p = new SpriteProject(W, H);
        p.SetLayerProperties(0, name: "bottom");
        p.AddLayer();
        p.SetCel(0, 0, Dot(W, H, 2, 2, Red));
        p.SetCel(1, 0, new PixelImage(W, H)); // 빈 셀이 있어야 위 레이어(와 그 효과)가 그려진다
        p.SetLayerEffects(1, [Marker("layer", "bottom")]);
        var before = p.Flatten(0);
        Assert.Equal(Mark, before[2, 2]);

        p.SetCel(0, 0, Dot(W, H, 5, 3, Red));
        var after = p.Flatten(0);
        Assert.Equal(Mark, after[5, 3]);
        Assert.Equal(0, after[2, 2].A);
    }

    [Fact]
    public void Layer_setting_values_round_trip_through_project_save_and_open()
    {
        var p = new SpriteProject(W, H);
        p.SetLayerProperties(0, name: "ground");
        var reader = p.AddLayer(name: "reader");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, new PixelImage(W, H));
        var hash = p.AddScript("""
            effect({ name: "saved layers", settings: [{ key: "target", type: "layer", default: "ground" }, { key: "other", type: "layer" }],
              render(img, ctx, s) { const o = ctx.layer("target"); if (o) for (let i = 0; i < img.data.length; i++) img.data[i] = o.data[i]; } });
            // Layer_setting_values_round_trip_through_project_save_and_open
            """);
        ScriptLibrary.SetTrust(hash, true);
        var program = ScriptLibrary.Runnable(hash)!;
        // 문서에 없는 이름도 그대로 둔다
        var effect = new ScriptEffect
        {
            ScriptHash = hash,
            Settings = ScriptSettingValues.Normalized(program, new Dictionary<string, object> { ["target"] = "ground", ["other"] = "nowhere" }),
        };
        p.SetLayerEffects(1, [effect]);
        Assert.Equal(Red, p.Flatten(0)[1, 1]);

        var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        JsonObject json;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
        using (var text = new StreamReader(zip.GetEntry(ProjectIO.ProjectEntryName)!.Open()))
            json = JsonNode.Parse(text.ReadToEnd())!.AsObject();
        var settings = json["layers"]![1]!["effects"]![0]!["settings"]!;
        Assert.Equal(("ground", "nowhere"), (settings["target"]!.GetValue<string>(), settings["other"]!.GetValue<string>()));

        stream.Position = 0;
        var warnings = new List<string>();
        var loaded = ProjectIO.Load(stream, warnings);
        Assert.Empty(warnings);
        var read = Assert.IsType<ScriptEffect>(loaded.Layers[1].Effects[0]);
        Assert.Equal(effect, read);
        Assert.Equal("nowhere", read.Settings["other"]);
        Assert.True(p.Flatten(0).ContentEquals(loaded.Flatten(0)));
        Assert.Equal("reader", reader.Name);
    }

    [Theory]
    [InlineData("settings: [{ key: 'k', type: 'layer', default: 'ground' }], render(img, ctx, s) {}", false, false)]
    [InlineData("settings: [{ key: 'k', type: 'layer' }], render(img, ctx, s) { const layer = 1; img.data[0] = layer; }", false, false)]
    [InlineData("render(img, ctx, s) { const o = ctx.layer('k'); }", true, false)]
    [InlineData("render(img, ctx, s) { const o = ctx['layer']('k'); }", true, false)]
    [InlineData("render(img, ctx, s) { const { layer } = ctx; layer('k'); }", true, false)]
    [InlineData("render(img, ctx, s) { ctx.layerEffects('k'); }", true, false)]
    [InlineData("render(img, ctx, s) { ctx.layerWithEffects('k'); }", true, true)]
    public void Reading_other_layers_is_detected_from_the_layer_functions_only(string body, bool reads, bool rendered)
    {
        var hash = ScriptLibrary.Add("effect({ name: 'x', " + body + " });\n// " + nameof(Reading_other_layers_is_detected_from_the_layer_functions_only) + body + "\n");
        var info = ScriptLibrary.InfoOf(hash)!;
        Assert.Equal((reads, rendered), (info.ReadsOtherLayers, info.ReadsRenderedLayers));
    }
}
