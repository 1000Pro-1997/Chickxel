using System.Drawing;
using System.Text.Json.Nodes;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Chickxel.Core.Text;

namespace Chickxel.Core.Tests;

/// <summary>글씨: 글자 비트 자리, 맞춤, 여러 줄, 타자기, 자르기, 없는 글자, 저장, 셀 없는 글씨 레이어(합성·아래로 합치기·영구 적용).</summary>
public class TextEffectTests
{
    private static readonly Color32 Red = new(255, 0, 0);

    // Galmuri11 '!': DWIDTH 4, BBX 1 11 1 0(아홉째 줄만 빔), FONT_ASCENT 14 → 줄 위에서 3~13줄, 펜 + 1
    private static readonly TextEffect Bang = new() { Text = "!", Color = Red };

    private static PixelImage Draw(TextEffect e, int width, int height, Rectangle? source = null)
    {
        var image = new PixelImage(width, height);
        LayerEffects.Apply(image, [e], source ?? new Rectangle(0, 0, width, height));
        return image;
    }

    /// <summary>보이는 픽셀 자리(위에서 아래, 왼쪽에서 오른쪽 차례).</summary>
    private static Point[] Ink(PixelImage image)
    {
        var list = new List<Point>();
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image[x, y].A != 0) list.Add(new Point(x, y));
        return [.. list];
    }

    /// <summary>점들을 <see cref="Ink"/>와 같은 차례로(겹친 점은 하나).</summary>
    private static Point[] Points(params IEnumerable<Point>[] parts)
        => [.. parts.SelectMany(p => p).Distinct().OrderBy(p => p.Y).ThenBy(p => p.X)];

    private static IEnumerable<Point> BangAt(int left, int top)
        => Enumerable.Range(0, 11).Where(row => row != 8).Select(row => new Point(left, top + row));

    [Fact]
    public void Font_LoadsAscentDescentAndFallbackGlyph()
    {
        var font = PixelFonts.Get(TextFont.Galmuri11);
        Assert.Equal((14, 2, 16), (font.Ascent, font.Descent, font.LineHeight));
        Assert.Same(font, PixelFonts.Get(TextFont.Galmuri11));
        Assert.True(font.Contains('가') && font.Contains('A') && font.Contains(0xFFFD));
        foreach (var f in Enum.GetValues<TextFont>()) Assert.True(PixelFonts.Get(f).LineHeight > 0, f.ToString());
    }

    [Fact]
    public void Glyph_DrawsItsBitmapBelowTheAscent()
    {
        var image = Draw(Bang with { X = 2, Y = 1 }, 10, 20);
        Assert.Equal(Points(BangAt(3, 4)), Ink(image));
        Assert.All(Ink(image), p => Assert.Equal(Red, image[p.X, p.Y]));
        Assert.Equal(new Rectangle(1, 3, 1, 11), TextLayout.Bounds(Bang));
    }

    [Fact]
    public void Color_IsBlendedOverThePixelsBelow()
    {
        var image = new PixelImage(4, 16);
        image[1, 5] = new Color32(0, 0, 255);
        LayerEffects.Apply(image, [Bang with { Color = new Color32(255, 0, 0, 128) }], new Rectangle(0, 0, 4, 16));
        var c = image[1, 5];
        Assert.Equal(255, c.A);
        Assert.True(c.R > 100 && c.B > 100, c.ToHex());
    }

    [Theory]
    [InlineData(TextAlign.Left, 0, 11)]
    [InlineData(TextAlign.Center, 0, 7)]
    [InlineData(TextAlign.Right, 0, 3)]
    [InlineData(TextAlign.Right, 2, 1)]
    public void Align_AnchorsTheLineAtX(TextAlign align, int letterSpacing, int firstInk)
    {
        // "!!" 너비 = (4 + 간격) × 2 − 간격
        var e = Bang with { Text = "!!", X = 10, Align = align, LetterSpacing = letterSpacing };
        var bounds = TextLayout.Bounds(e);
        Assert.Equal(firstInk, bounds.Left);
        Assert.Equal(firstInk + 4 + letterSpacing, bounds.Right - 1);
        Assert.Equal(new Size(8 + letterSpacing, 16), PixelFonts.Get(TextFont.Galmuri11).Measure("!!", letterSpacing, 0));
    }

    [Fact]
    public void Center_UsesEachLinesOwnWidth()
    {
        // 짧은 둘째 줄도 같은 X 가운데: "!!" 왼쪽 = 10 − 4, "!" 왼쪽 = 10 − 2
        var image = Draw(Bang with { Text = "!!\n!", X = 10, Align = TextAlign.Center }, 20, 40);
        Assert.Equal(Points(BangAt(7, 3), BangAt(11, 3), BangAt(9, 19)), Ink(image));
    }

    [Fact]
    public void Lines_StepByLineHeightPlusLineSpacing()
    {
        var e = Bang with { Text = "!\n!", LineSpacing = 3 };
        Assert.Equal(Points(BangAt(1, 3), BangAt(1, 3 + 19)), Ink(Draw(e, 4, 40)));
        Assert.Equal(new Size(4, 35), PixelFonts.Get(TextFont.Galmuri11).Measure(e.Text, 0, 3));
        Assert.Equal(new Rectangle(1, 3, 1, 30), TextLayout.Bounds(e));
        Assert.Equal("a\nb", (Bang with { Text = "a\r\nb" }).Text);
    }

    [Fact]
    public void Typewriter_ShowsNothingFirst_AllLast_AndGrowsInBetween()
    {
        var e = Bang with { Text = "!!\n!", Typewriter = true };
        int[] counts = [.. Enumerable.Range(0, 5).Select(f => Ink(Draw((TextEffect)LayerEffects.At([e], f, 5)[0], 12, 40)).Length)];
        Assert.Equal(0, counts[0]);
        Assert.Equal(Ink(Draw(e with { Typewriter = false }, 12, 40)).Length, counts[^1]);
        Assert.Equal(counts.Order().ToArray(), counts);
        Assert.Equal(10, counts[2]); // 진행 0.5 × 3글자 → 1글자
    }

    [Fact]
    public void Text_IsClippedToTheEffectSource()
    {
        foreach (var (x, y) in new[] { (-3, -8), (5, 5), (-4096, 4096), (4096, -4096) })
        {
            var e = new TextEffect { Text = "가나다\nABC", X = x, Y = y, Font = TextFont.Galmuri14 };
            var image = Draw(e, 30, 30, new Rectangle(4, 6, 10, 9));
            Assert.All(Ink(image), p => Assert.True(p.X >= 4 && p.X < 14 && p.Y >= 6 && p.Y < 15, $"{x},{y}: {p}"));
        }
        Assert.NotEmpty(Ink(Draw(new TextEffect { Text = "가나다", X = -3, Y = -8 }, 10, 10)));
    }

    [Fact]
    public void MissingGlyph_UsesTheReplacementGlyph_AndTabIsFourSpaces()
    {
        var font = PixelFonts.Get(TextFont.Galmuri11);
        Assert.False(font.Contains(0x10FFFD));
        Assert.Equal(Ink(Draw(Bang with { Text = "\uFFFD" }, 20, 20)), Ink(Draw(Bang with { Text = "\U0010FFFD" }, 20, 20)));
        Assert.Equal(4 * font.Measure(" ", 0, 0).Width, font.Measure("\t", 0, 0).Width);
        Assert.Equal(TextLayout.Bounds(Bang with { Text = "    !" }), TextLayout.Bounds(Bang with { Text = "\t!" }));
        Assert.Equal(Rectangle.Empty, TextLayout.Bounds(Bang with { Text = " \n " }));
    }

    [Fact]
    public void Settings_AcceptText_AndRejectTooLong()
    {
        var (effect, _) = EffectSettings.Merge(new TextEffect(), new JsonObject { ["text"] = "안녕\r\n세상", ["font"] = "galmuri9" });
        Assert.Equal(("안녕\n세상", TextFont.Galmuri9), (((TextEffect)effect).Text, ((TextEffect)effect).Font));
        var error = Assert.Throws<EffectSettingException>(() => EffectSettings.Merge(new TextEffect(), new JsonObject { ["text"] = new string('a', TextEffect.MaxLength + 1) }));
        Assert.Equal(EffectSettingError.InvalidValue, error.Error);
        Assert.Throws<EffectSettingException>(() => EffectSettings.Merge(new TextEffect(), new JsonObject { ["text"] = 3 }));
    }

    /// <summary>캔버스 16×20, 맨 아래 레이어 (0,0)에 파랑 한 점, 그 위에 셀 없는 글씨 레이어(프레임 3개).</summary>
    private static SpriteProject TextLayerProject(TextEffect text)
    {
        var p = new SpriteProject(16, 20);
        p.DuplicateFrame(0);
        p.DuplicateFrame(0);
        var cel = new PixelImage(16, 20);
        cel[0, 0] = new Color32(0, 0, 255);
        for (int f = 0; f < 3; f++) p.SetCel(0, f, cel);
        p.AddLayer();
        p.SetLayerEffects(1, [text]);
        return p;
    }

    [Fact]
    public void EmptyLayer_WithText_RendersInEveryPath_AndSurvivesSaveLoad()
    {
        var text = Bang with { Text = "!!", X = 3, Y = 2 };
        var p = TextLayerProject(text);
        Assert.Null(p.GetCel(1, 0));
        Assert.True(p.HasContentEffect(1, 0));
        Assert.False(p.HasContentEffect(0, 0));
        var expected = Points(BangAt(4, 5), BangAt(8, 5), [new Point(0, 0)]);
        Assert.Equal(expected, Ink(p.Flatten(1)));
        Assert.Equal(expected, Ink(p.FlattenOutfit(1, null)));
        Assert.Equal(Points(BangAt(4, 5), BangAt(8, 5)), Ink(p.PlanCel(1, 0)!.Render()));
        Assert.NotNull(p.EffectSource(p.Layers[1], 0));

        // 꺼진 효과·범위 밖 프레임은 셀이 없으면 그리지 않는다
        p.SetLayerEffects(1, [text with { Frames = new FrameRange(1, 2) }]);
        Assert.False(p.HasContentEffect(1, 0));
        Assert.Equal([new Point(0, 0)], Ink(p.Flatten(0)));
        Assert.Null(p.PlanCel(1, 0));
        p.SetLayerEffects(1, [text]);

        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        var loaded = ProjectIO.Load(stream);
        Assert.Equal([text], loaded.Layers[1].Effects);
        Assert.Null(loaded.GetCel(1, 2));
        Assert.Equal(expected, Ink(loaded.Flatten(2)));
    }

    [Fact]
    public void MergeDown_OfEmptyTextLayer_PutsTheTextIntoTheLayerBelow()
    {
        var p = TextLayerProject(Bang with { Text = "!", Typewriter = true, Frames = new FrameRange(1, 2) });
        p.MergeLayerDown(1);
        Assert.Single(p.Layers);
        Assert.Equal([new Point(0, 0)], Ink(p.GetCel(0, 0)!));                      // 범위 밖
        Assert.Equal([new Point(0, 0)], Ink(p.GetCel(0, 1)!));                      // 타자기 첫 프레임
        Assert.Equal(Points(BangAt(1, 3), [new Point(0, 0)]), Ink(p.GetCel(0, 2)!));
    }

    [Fact]
    public void Bake_OfEmptyTextLayer_CreatesCels()
    {
        var text = Bang with { Text = "!", X = 5 };
        var p = TextLayerProject(text);
        var before = p.Flatten(0);
        var (baked, _, cels, _) = p.BakeLayerEffects(1, [0]);
        Assert.Equal([text], baked);
        Assert.Equal(3, cels);
        Assert.Empty(p.Layers[1].Effects);
        Assert.Equal(Points(BangAt(6, 3)), Ink(p.GetCel(1, 0)!));
        Assert.Equal(before.Pixels, p.Flatten(0).Pixels);
        Assert.False(p.HasContentEffect(1, 0));
    }

    [Fact]
    public void SaveLoad_RoundTripsEverySetting()
    {
        var p = new SpriteProject(8, 8);
        for (int i = 0; i < 4; i++) p.DuplicateFrame(0);
        var fx = new TextEffect
        {
            Text = "글씨 Text\n둘째 줄\t끝", Font = TextFont.Galmuri11Bold, Color = new Color32(1, 2, 3, 4), X = -7, Y = 300, LetterSpacing = -2, LineSpacing = 5,
            Align = TextAlign.Right, Typewriter = true, Repeat = EffectRepeat.Loop, RepeatCount = 2, Frames = new FrameRange(0, 3),
        };
        p.SetLayerEffects(0, [fx]);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        Assert.Equal([fx], ProjectIO.Load(stream).Layers[0].Effects);
    }
}
