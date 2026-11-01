using System.Drawing;
using System.Runtime.CompilerServices;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Scripting;

namespace Chickxel.Core.Tests;

/// <summary>
/// 스크립트가 다른 레이어와 효과 원본 배치를 읽는 API(<c>ctx.layerBelow</c>·<c>ctx.layerAbove</c>·<c>…WithEffects</c>, <c>ctx.layerByName</c>·<c>…WithEffects</c>,
/// <c>ctx.layerEffectsByName</c>·<c>ctx.layerEffectsAt</c>, <c>ctx.center</c>·<c>ctx.slice</c>): 프로젝트 렌더에서의 픽셀·자리, 겹침 규칙, 이름, 캐시 무효, 빠른 경로.
/// 신뢰 결정은 프로세스 전체 상태라 시험마다 원문 끝에 주석을 붙여 해시를 따로 쓴다.
/// </summary>
public class ScriptLayerContextTests
{
    private const int W = 8, H = 6, Pad = 2;

    private static readonly Color32 Red = new(255, 0, 0), Blue = new(0, 0, 255), Mark = new(255, 0, 255);

    private static string Trusted(string source, [CallerMemberName] string test = "")
    {
        var hash = ScriptLibrary.Add(source + "\n// " + test + "\n");
        ScriptLibrary.SetTrust(hash, true);
        return hash;
    }

    /// <summary><c>ctx.CALL(ARGS)</c>의 그림을 img에 그대로 옮기는 효과(여백 2px). 없으면(null) 모든 값을 7로.</summary>
    private static ScriptEffect Copier(string call, string args = "", [CallerMemberName] string test = "") => new()
    {
        ScriptHash = Trusted($$"""
            effect({ name: "copy {{call}}", padding: [{{Pad}}, {{Pad}}, {{Pad}}, {{Pad}}], render(img, ctx, s) {
              const o = ctx.{{call}}({{args}});
              for (let i = 0; i < img.data.length; i++) img.data[i] = o ? o.data[i] : 7;
            } });
            """, test),
    };

    /// <summary><c>ctx.CALL(ARGS)</c> 그림이 불투명한 자리를 표시 색으로 칠하는 효과(여백 없음).</summary>
    private static ScriptEffect Marker(string call, string args = "", [CallerMemberName] string test = "") => new()
    {
        ScriptHash = Trusted($$"""
            effect({ name: "mark {{call}}", render(img, ctx, s) {
              const o = ctx.{{call}}({{args}});
              if (!o) return;
              for (let i = 0; i < img.data.length; i += 4)
                if (o.data[i + 3] > 0) { img.data[i] = 255; img.data[i + 1] = 0; img.data[i + 2] = 255; img.data[i + 3] = 255; }
            } });
            """, test),
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

    /// <summary>바닥(빨간 점)·가운데(읽는 레이어)·위(파란 점) 세 레이어.</summary>
    private static (SpriteProject P, Layer Bottom, Layer Middle, Layer Top) ThreeLayers()
    {
        var p = new SpriteProject(W, H);
        var bottom = p.Layers[0];
        var middle = p.AddLayer();
        var top = p.AddLayer();
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, Dot(W, H, 4, 4, new Color32(0, 255, 0)));
        p.SetCel(2, 0, Dot(W, H, 5, 2, Blue));
        return (p, bottom, middle, top);
    }

    [Fact]
    public void Below_and_above_without_effects_are_raw_cels_aligned_to_the_padded_image()
    {
        var (p, _, middle, _) = ThreeLayers();
        // 아래 레이어 효과는 원본 읽기에 들어가지 않는다
        p.SetLayerEffects(0, [new ColorOverlayEffect { Color = Blue, Amount = 100 }]);

        p.SetLayerEffects(1, [Copier("layerBelow")]);
        var below = Rendered(p, middle);
        Assert.Equal((W + 2 * Pad, H + 2 * Pad), (below.Width, below.Height));
        Assert.True(Aligned(Dot(W, H, 1, 1, Red), below.Width, below.Height, new Point(Pad, Pad)).ContentEquals(below));

        p.SetLayerEffects(1, [Copier("layerAbove")]);
        var above = Rendered(p, middle);
        Assert.True(Aligned(Dot(W, H, 5, 2, Blue), above.Width, above.Height, new Point(Pad, Pad)).ContentEquals(above));
    }

    [Fact]
    public void With_effects_variants_render_the_neighbor_effects_for_this_frame_including_spill()
    {
        var (p, bottom, middle, top) = ThreeLayers();
        var outline = new OutlineEffect { Color = new Color32(10, 20, 30) };
        p.SetLayerEffects(0, [outline]);
        p.SetLayerEffects(2, [new ColorOverlayEffect { Color = Red, Amount = 100 }]);

        PixelImage Expected(Layer neighbor, int width, int height)
        {
            var (fx, dx, dy) = LayerEffects.Render(p.GetCel(neighbor, p.Frames[0])!, p.EffectsAt(neighbor, 0));
            var expected = new PixelImage(width, height);
            expected.Composite(fx, Pad + dx, Pad + dy);
            return expected;
        }

        p.SetLayerEffects(1, [Copier("layerBelowWithEffects")]);
        var below = Rendered(p, middle);
        Assert.True(Expected(bottom, below.Width, below.Height).ContentEquals(below));
        Assert.Equal(outline.Color, below[Pad + 0, Pad + 1]); // 셀 밖으로 번진 외곽선도 같은 자리에

        p.SetLayerEffects(1, [Copier("layerAboveWithEffects")]);
        var above = Rendered(p, middle);
        Assert.True(Expected(top, above.Width, above.Height).ContentEquals(above));
        Assert.NotEqual(Blue, above[Pad + 5, Pad + 2]);
    }

    [Fact]
    public void Part_neighbors_are_aligned_through_their_part_offsets()
    {
        var p = new SpriteProject(W, H);
        var group = p.AddGroup("g");
        var lower = p.AddPart(group, "lower", 3, 3, new Point(1, 1));
        var upper = p.AddPart(group, "upper", 3, 3, new Point(4, 2));
        p.SetCel(p.IndexOf(lower), 0, Dot(3, 3, 2, 0, Red)); // 캔버스 (3, 1)
        p.SetCel(p.IndexOf(upper), 0, Dot(3, 3, 0, 0, Blue));
        p.SetLayerEffects(p.IndexOf(upper), [Copier("layerBelow")]);

        var image = Rendered(p, upper);
        // upper 원본 왼쪽 위(캔버스 (4, 2))가 그림의 (2, 2) → 캔버스 (3, 1)은 (1, 1)
        Assert.Equal((3 + 2 * Pad, 3 + 2 * Pad), (image.Width, image.Height));
        Assert.Equal(Red, image[1, 1]);
        Assert.Equal(1, image.Pixels.Count(c => c.A != 0));

        // 맨 위 부위의 위는 없다(그룹 밖 레이어는 이웃이 아님)
        p.SetLayerEffects(p.IndexOf(upper), [Copier("layerAbove")]);
        Assert.All(Rendered(p, upper).Pixels, c => Assert.Equal(new Color32(7, 7, 7, 7), c));
    }

    [Fact]
    public void Missing_layers_and_cels_give_null_and_hidden_layers_still_count()
    {
        var p = new SpriteProject(W, H);
        p.SetCel(0, 0, Dot(W, H, 0, 0, Red));
        var only = p.Layers[0];
        p.SetLayerEffects(0, [Copier("layerBelow")]);
        Assert.All(Rendered(p, only).Pixels, c => Assert.Equal(new Color32(7, 7, 7, 7), c));
        p.SetLayerEffects(0, [Copier("layerAbove")]);
        Assert.All(Rendered(p, only).Pixels, c => Assert.Equal(new Color32(7, 7, 7, 7), c));

        // 위 레이어가 있어도 이 프레임 셀이 없으면 null
        var hidden = p.AddLayer();
        Assert.All(Rendered(p, only).Pixels, c => Assert.Equal(new Color32(7, 7, 7, 7), c));

        // 숨긴 레이어도 건너뛰지 않고 그 그림을 준다
        p.SetCel(1, 0, Dot(W, H, 3, 3, Blue));
        p.SetLayerProperties(1, visible: false);
        Assert.Equal(Blue, Rendered(p, only)[Pad + 3, Pad + 3]);
        Assert.False(hidden.Visible);
    }

    [Fact]
    public void Two_layers_reading_each_other_with_effects_render_deterministically_without_recursion()
    {
        var p = new SpriteProject(W, H);
        var a = p.Layers[0];
        var b = p.AddLayer();
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, Dot(W, H, 6, 4, Blue));
        var overlay = new ColorOverlayEffect { Color = new Color32(0, 200, 0), Amount = 100 };
        p.SetLayerEffects(0, [overlay, Marker("layerAboveWithEffects")]);
        p.SetLayerEffects(1, [Marker("layerBelowWithEffects")]);

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
    public void Effect_names_by_name_and_offset_list_only_effects_enabled_on_the_frame()
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
                effect({ name: "names", render(img, ctx, s) {
                  const want = ctx.frame === 0 ? "{{expected0}}" : "{{expected1}}";
                  img.data[0] = ctx.layerEffectsByName("base").join("|") === want ? 1 : 2;
                  img.data[1] = ctx.layerEffectsAt(-1).join("|") === want ? 1 : 2;
                  img.data[2] = ctx.layerEffectsByName("nobody").length === 0 && ctx.layerEffectsAt(5).length === 0 ? 1 : 2;
                  img.data[3] = 255;
                  img.data[4] = ctx.layerEffectsAt(0)[0] === "names" && Array.isArray(ctx.layerEffectsAt(1)) ? 1 : 2;
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

        var layers = p.ScriptLayersAt(reader, 0)!;
        Assert.Equal([new ColorOverlayEffect().DisplayName], layers.EffectsAt(1));
        Assert.Equal([outline.DisplayName], layers.EffectsByName("base"));
        // 효과 목록을 따로 주면 그 목록 기준(효과 창 미리보기): 같은 이름 중 자기 자신이 가장 가깝다
        Assert.Equal([fourth.Effects[0].DisplayName, fourth.Effects[1].DisplayName], p.ScriptLayersAt(fourth, 0, [script])!.EffectsByName("base"));
        Assert.Null(p.ScriptLayersAt(fourth, 0));
        Assert.Empty(layers.EffectsAt(-2));
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
            effect({ name: "alone", render(img, ctx, s) {
              img.data[0] = ctx.layerBelow() === null && ctx.layerAbove() === null && ctx.layerByName("a") === null ? 1 : 2;
              img.data[1] = ctx.layerBelowWithEffects() === null && ctx.layerAboveWithEffects() === null && ctx.layerByNameWithEffects("a") === null ? 1 : 2;
              img.data[2] = ctx.layerEffectsByName("a").length + ctx.layerEffectsAt(-1).length;
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
    public void Fast_path_reads_center_and_falls_back_to_jint_for_slice_and_layer_functions()
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

        foreach (var (name, body) in new[] { ("ctx.slice", "img.data[0] = ctx.slice ? 1 : 0;"), ("ctx.layerBelow", "img.data[0] = ctx.layerBelow() ? 1 : 0;"),
                     ("ctx.layerEffectsAt", "img.data[0] = ctx.layerEffectsAt(1).length;"),
                     ("ctx.layerByName", "img.data[0] = ctx.layerByName(\"a\") ? 1 : 0;"),
                     ("ctx.layerByNameWithEffects", "img.data[0] = ctx.layerByNameWithEffects(\"a\") ? 1 : 0;") })
        {
            var program = ScriptLibrary.Runnable(Trusted($$"""effect({ name: "x", render(img, ctx, s) { {{body}} } });"""))!;
            Assert.False(program.ExecutionPath.Compiled);
            Assert.Contains(name, program.ExecutionPath.Reason);
        }
    }

    [Fact]
    public void Render_cache_does_not_serve_stale_results_when_the_neighbor_cel_or_effects_change()
    {
        var p = new SpriteProject(W, H);
        var bottom = p.Layers[0];
        var top = p.AddLayer();
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, Dot(W, H, 6, 4, Blue));
        // 읽는 효과 뒤에 효과가 하나 더 있어야 중간 결과(효과 1까지)가 캐시에 담긴다
        p.SetLayerEffects(1, [Marker("layerBelowWithEffects"), new OutlineEffect { Color = new Color32(1, 2, 3) }]);
        var cache = new EffectRenderCache(64L << 20);
        PixelImage Cached() => p.PlanFrame(0).Render(new RenderPlanOptions { Cache = cache, CheckpointLayer = top });

        var before = Cached();
        Assert.True(before.ContentEquals(Cached()));
        Assert.True(cache.Bytes > 0);

        // 이웃 효과만 바뀜(앱은 이때 효과 결과 캐시를 비우지 않는다)
        p.SetLayerEffects(0, [new OutlineEffect { Color = new Color32(9, 9, 9) }]);
        var afterEffects = Cached();
        Assert.False(before.ContentEquals(afterEffects));
        Assert.True(p.Flatten(0).ContentEquals(afterEffects));
        Assert.Equal(Mark, afterEffects[0, 1]); // 이웃 외곽선 자리도 표시됨

        // 이웃 셀이 새 그림으로 바뀜
        p.SetCel(0, 0, Dot(W, H, 3, 3, Red));
        var afterCel = Cached();
        Assert.False(afterEffects.ContentEquals(afterCel));
        Assert.True(p.Flatten(0).ContentEquals(afterCel));
        Assert.Same(bottom, p.Layers[0]);
    }

    [Fact]
    public void Top_layer_reading_layer_below_changes_when_the_bottom_layer_is_edited()
    {
        var p = new SpriteProject(W, H);
        p.AddLayer();
        p.SetCel(0, 0, Dot(W, H, 2, 2, Red));
        p.SetCel(1, 0, new PixelImage(W, H)); // 빈 셀이 있어야 위 레이어(와 그 효과)가 그려진다
        p.SetLayerEffects(1, [Marker("layerBelow")]);
        var before = p.Flatten(0);
        Assert.Equal(Mark, before[2, 2]);

        p.SetCel(0, 0, Dot(W, H, 5, 3, Red));
        var after = p.Flatten(0);
        Assert.Equal(Mark, after[5, 3]);
        Assert.Equal(0, after[2, 2].A);
    }

    [Fact]
    public void By_name_reads_the_nearest_other_layer_with_that_name_aligned_like_layer_below()
    {
        var p = new SpriteProject(W, H);
        var reader = p.AddLayer(name: "reader");
        p.AddLayer(name: "x2");
        p.AddLayer(name: "x3");
        var green = new Color32(0, 255, 0);
        p.SetLayerProperties(0, name: "ground");
        p.SetLayerProperties(2, name: "ground"); // 읽는 레이어(1)에서 0과 2가 같은 거리 — 아래쪽(0)
        p.SetLayerProperties(3, name: "ground");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(1, 0, new PixelImage(W, H));
        p.SetCel(2, 0, Dot(W, H, 5, 2, Blue));
        p.SetCel(3, 0, Dot(W, H, 6, 4, green));
        // 찾은 레이어 효과는 원본 읽기에 들어가지 않는다
        p.SetLayerEffects(0, [new ColorOverlayEffect { Color = Blue, Amount = 100 }]);

        var copier = Copier("layerByName", "\"ground\"");
        Assert.True(ScriptLibrary.InfoOf(copier.ScriptHash) is { ReadsOtherLayers: true, ReadsNamedLayers: true });
        p.SetLayerEffects(1, [copier]);
        var image = Rendered(p, reader);
        Assert.Equal((W + 2 * Pad, H + 2 * Pad), (image.Width, image.Height));
        Assert.True(Aligned(Dot(W, H, 1, 1, Red), image.Width, image.Height, new Point(Pad, Pad)).ContentEquals(image));

        // 가장 가까운 것이 이름을 바꾸면 다음으로 가까운 것, 숨겨도 센다
        p.SetLayerProperties(0, name: "base");
        p.SetLayerProperties(2, visible: false);
        Assert.True(Aligned(Dot(W, H, 5, 2, Blue), image.Width, image.Height, new Point(Pad, Pad)).ContentEquals(Rendered(p, reader)));

        // 없는 이름은 null
        p.SetLayerEffects(1, [Copier("layerByName", "\"nobody\"")]);
        Assert.All(Rendered(p, reader).Pixels, c => Assert.Equal(new Color32(7, 7, 7, 7), c));

        // 자기 레이어는 찾지 않는다: 같은 이름의 다른 레이어가 생겨야 그것을 준다
        p.SetLayerEffects(1, [Copier("layerByName", "\"reader\"")]);
        Assert.All(Rendered(p, reader).Pixels, c => Assert.Equal(new Color32(7, 7, 7, 7), c));
        p.SetLayerProperties(3, name: "reader");
        var same = Rendered(p, reader);
        Assert.Equal(green, same[Pad + 6, Pad + 4]);
        Assert.Equal(1, same.Pixels.Count(c => c.A != 0));
    }

    [Fact]
    public void By_name_finds_groups_and_parts_but_not_the_group_holding_the_reader()
    {
        var p = new SpriteProject(W, H);
        var group = p.AddGroup("g");
        var lower = p.AddPart(group, "lower", 3, 3, new Point(1, 1));
        var upper = p.AddPart(group, "upper", 3, 3, new Point(4, 2));
        p.SetCel(p.IndexOf(lower), 0, Dot(3, 3, 2, 0, Red)); // 캔버스 (3, 1)
        p.SetCel(p.IndexOf(upper), 0, Dot(3, 3, 0, 0, Blue)); // 캔버스 (4, 2)
        var reader = p.AddLayer(name: "reader");
        p.SetCel(p.IndexOf(reader), 0, new PixelImage(W, H));

        // 최상위 레이어가 그룹을 이름으로: 보이는 부위를 합친 효과 없는 그림
        p.SetLayerEffects(p.IndexOf(reader), [Copier("layerByName", "\"g\"")]);
        var image = Rendered(p, reader);
        Assert.Equal(Red, image[Pad + 3, Pad + 1]);
        Assert.Equal(Blue, image[Pad + 4, Pad + 2]);
        Assert.Equal(2, image.Pixels.Count(c => c.A != 0));

        // 부위가 다른 부위를 이름으로: 부위 자리를 거쳐 맞춘다(upper 원본 왼쪽 위 = 캔버스 (4, 2) = 그림 (2, 2))
        p.SetLayerEffects(p.IndexOf(upper), [Copier("layerByName", "\"lower\"")]);
        var part = Rendered(p, upper);
        Assert.Equal(Red, part[1, 1]);
        Assert.Equal(1, part.Pixels.Count(c => c.A != 0));

        // 자기를 담은 그룹은 찾지 않는다(그 그림에 자기가 들어 있음)
        p.SetLayerEffects(p.IndexOf(upper), [Copier("layerByName", "\"g\"")]);
        Assert.All(Rendered(p, upper).Pixels, c => Assert.Equal(new Color32(7, 7, 7, 7), c));
        Assert.NotNull(p.Flatten(0));
    }

    [Fact]
    public void By_name_with_effects_renders_the_layer_effects_and_follows_the_no_recursion_rule()
    {
        var p = new SpriteProject(W, H);
        var ground = p.Layers[0];
        p.AddLayer(name: "middle");
        var reader = p.AddLayer(name: "reader");
        p.SetLayerProperties(0, name: "ground");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(2, 0, new PixelImage(W, H));
        var outline = new OutlineEffect { Color = new Color32(10, 20, 30) };
        p.SetLayerEffects(0, [outline]);
        p.SetLayerEffects(2, [Copier("layerByNameWithEffects", "\"ground\"")]);

        var image = Rendered(p, reader);
        var (fx, dx, dy) = LayerEffects.Render(p.GetCel(ground, p.Frames[0])!, p.EffectsAt(ground, 0));
        var expected = new PixelImage(image.Width, image.Height);
        expected.Composite(fx, Pad + dx, Pad + dy);
        Assert.True(expected.ContentEquals(image));
        Assert.Equal(outline.Color, image[Pad + 0, Pad + 1]); // 셀 밖으로 번진 외곽선도 같은 자리에

        // 서로를 이름으로 효과까지 읽는 두 레이어(사이에 다른 레이어): 상대의 읽는 효과는 건너뛴다
        var q = new SpriteProject(W, H);
        var a = q.Layers[0];
        q.AddLayer(name: "between");
        var b = q.AddLayer(name: "b");
        q.SetLayerProperties(0, name: "a");
        q.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        q.SetCel(2, 0, Dot(W, H, 6, 4, Blue));
        q.SetLayerEffects(0, [new ColorOverlayEffect { Color = new Color32(0, 200, 0), Amount = 100 }, Marker("layerByNameWithEffects", "\"b\"")]);
        q.SetLayerEffects(2, [Marker("layerByNameWithEffects", "\"a\"")]);

        var seenByB = Rendered(q, b);
        Assert.Equal(Mark, seenByB[1, 1]);
        Assert.Equal(Blue, seenByB[6, 4]);
        Assert.Equal(2, seenByB.Pixels.Count(c => c.A != 0));
        var seenByA = Rendered(q, a);
        Assert.Equal(Mark, seenByA[6, 4]);
        Assert.NotEqual(Mark, seenByA[1, 1]);
        Assert.Equal(2, seenByA.Pixels.Count(c => c.A != 0));
        var first = q.Flatten(0);
        Assert.True(first.ContentEquals(q.Flatten(0)));
        Assert.Equal(Mark, first[1, 1]);
    }

    [Fact]
    public void Render_cache_does_not_serve_stale_results_when_the_named_layer_cel_or_effects_change()
    {
        var p = new SpriteProject(W, H);
        var ground = p.Layers[0];
        p.AddLayer(name: "middle"); // 바로 아래가 아닌 레이어를 이름으로 읽는다
        var reader = p.AddLayer(name: "reader");
        p.SetLayerProperties(0, name: "ground");
        p.SetCel(0, 0, Dot(W, H, 1, 1, Red));
        p.SetCel(2, 0, Dot(W, H, 6, 4, Blue));
        // 읽는 효과 뒤에 효과가 하나 더 있어야 중간 결과(효과 1까지)가 캐시에 담긴다
        p.SetLayerEffects(2, [Marker("layerByNameWithEffects", "\"ground\""), new OutlineEffect { Color = new Color32(1, 2, 3) }]);
        var cache = new EffectRenderCache(64L << 20);
        PixelImage Cached() => p.PlanFrame(0).Render(new RenderPlanOptions { Cache = cache, CheckpointLayer = reader });

        var before = Cached();
        Assert.True(before.ContentEquals(Cached()));
        Assert.True(cache.Bytes > 0);
        Assert.Equal(Mark, before[1, 1]);

        // 이름으로 읽는 레이어의 효과만 바뀜(앱은 이때 효과 결과 캐시를 비우지 않는다)
        p.SetLayerEffects(0, [new OutlineEffect { Color = new Color32(9, 9, 9) }]);
        var afterEffects = Cached();
        Assert.False(before.ContentEquals(afterEffects));
        Assert.True(p.Flatten(0).ContentEquals(afterEffects));
        Assert.Equal(Mark, afterEffects[0, 1]);

        // 이름으로 읽는 레이어의 셀이 새 그림으로 바뀜
        p.SetCel(0, 0, Dot(W, H, 3, 3, Red));
        var afterCel = Cached();
        Assert.False(afterEffects.ContentEquals(afterCel));
        Assert.True(p.Flatten(0).ContentEquals(afterCel));
        Assert.Equal(Mark, afterCel[3, 3]);
        Assert.Same(ground, p.Layers[0]);
    }
}
