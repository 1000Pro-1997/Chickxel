using System.Drawing;
using System.Text.Json.Nodes;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
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
        var font = FontLibrary.Default.Font;
        Assert.Equal((14, 2, 16), (font.Ascent, font.Descent, font.LineHeight));
        Assert.Same(font, FontLibrary.Default.Font);
        Assert.True(font.Contains('가') && font.Contains('A') && font.Contains(0xFFFD));
        string[] builtIns = ["Galmuri7", "Galmuri9", "Galmuri11", "Galmuri11Bold", "Galmuri14"];
        Assert.Equal(builtIns, FontLibrary.List().Where(f => f.BuiltIn).Select(f => f.Name));
        foreach (var name in builtIns)
        {
            var info = FontLibrary.Find(name.ToLowerInvariant())!;
            Assert.Equal((name, FontKind.Bitmap, true), (info.Name, info.Kind, info.BuiltIn));
            Assert.Equal(TextLayout.Measure(new TextEffect { FontName = name }).Lines[0].Height, info.NativeSize);
        }
        Assert.Equal(16, FontLibrary.Find(FontLibrary.DefaultName)!.NativeSize);
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
        Assert.Equal(new Size(8 + letterSpacing, 16), FontLibrary.Default.Font.Measure("!!", letterSpacing, 0));
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
        Assert.Equal(new Size(4, 35), FontLibrary.Default.Font.Measure(e.Text, 0, 3));
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
            var e = new TextEffect { Text = "가나다\nABC", X = x, Y = y, FontName = "Galmuri14" };
            var image = Draw(e, 30, 30, new Rectangle(4, 6, 10, 9));
            Assert.All(Ink(image), p => Assert.True(p.X >= 4 && p.X < 14 && p.Y >= 6 && p.Y < 15, $"{x},{y}: {p}"));
        }
        Assert.NotEmpty(Ink(Draw(new TextEffect { Text = "가나다", X = -3, Y = -8 }, 10, 10)));
    }

    [Fact]
    public void MissingGlyph_UsesTheReplacementGlyph_AndTabIsFourSpaces()
    {
        var font = FontLibrary.Default.Font;
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
        Assert.Equal(("안녕\n세상", "Galmuri9"), (((TextEffect)effect).Text, ((TextEffect)effect).FontName));
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
            Text = "글씨 Text\n둘째 줄\t끝", FontName = "Galmuri11Bold", Color = new Color32(1, 2, 3, 4), X = -7, Y = 300, LetterSpacing = -20, LineSpacing = 50,
            Size = 32, Bold = true, Italic = true, Smooth = true, ScaleX = 150, ScaleY = 75, BoxWidth = 90, BoxHeight = 40,
            Align = TextAlign.Right, Typewriter = true, Repeat = EffectRepeat.Loop, RepeatCount = 2, Frames = new FrameRange(0, 3),
        };
        p.SetLayerEffects(0, [fx]);
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        Assert.Equal([fx], ProjectIO.Load(stream).Layers[0].Effects);
    }

    [Fact]
    public void OldJson_WithFontEnumName_LoadsAsFontName_AndNewSettingsDefault()
    {
        var json = (JsonObject)JsonNode.Parse("""
            { "type": "chickxel.effect.text", "settings": { "text": "!", "font": "Galmuri14", "color": "#FF0000", "x": 2, "y": 1,
              "letterSpacing": 1, "lineSpacing": 2, "align": "Center", "typewriter": false } }
            """)!;
        var e = Assert.IsType<TextEffect>(EffectCodec.Read(json));
        Assert.Equal(("Galmuri14", 2, 1, 1, 2, TextAlign.Center), (e.FontName, e.X, e.Y, e.LetterSpacing, e.LineSpacing, e.Align));
        Assert.Equal((0, false, false, false, 100, 100, 0, 0), (e.Size, e.Bold, e.Italic, e.Smooth, e.ScaleX, e.ScaleY, e.BoxWidth, e.BoxHeight));
        Assert.False(FontLibrary.IsMissing(e.FontName));

        // 예전 기본 글꼴(Galmuri11) 글씨는 예전과 같은 자리에 그린다
        Assert.Equal(Points(BangAt(3, 4)), Ink(Draw(Bang with { FontName = "galmuri11", X = 2, Y = 1 }, 10, 20)));
        Assert.Equal("Galmuri11", (Bang with { FontName = "  " }).FontName);
    }

    [Theory]
    [InlineData(TextAlign.Left, 0)]
    [InlineData(TextAlign.Center, 2)]
    [InlineData(TextAlign.Right, 4)]
    public void Box_WrapsAtSpaces_AndAlignsEachLineInsideTheBox(TextAlign align, int offset)
    {
        // "!!" 너비 8, 상자 12: 빈칸 뒤에서 줄이 바뀌고 줄 끝 빈칸은 너비에 넣지 않는다
        var e = Bang with { Text = "!! !! !!", X = 5, Y = 2, BoxWidth = 12, Align = align };
        var layout = TextLayout.Measure(e);
        Assert.Equal(
            [new TextLine(0, 3, 5 + offset, 2, 8, 16), new TextLine(3, 3, 5 + offset, 18, 8, 16), new TextLine(6, 2, 5 + offset, 34, 8, 16)],
            layout.Lines);
        Assert.Equal(new Rectangle(5, 2, 12, 48), layout.Frame);
        Assert.Equal(new Rectangle(5, 2, 12, 60), TextLayout.Measure(e with { BoxHeight = 60 }).Frame);
        Assert.True(Rectangle.Union(layout.Frame, layout.Ink) == layout.Frame, layout.Ink.ToString());
        Assert.Equal(Points(BangAt(6 + offset, 5), BangAt(10 + offset, 5), BangAt(6 + offset, 21), BangAt(10 + offset, 21), BangAt(6 + offset, 37), BangAt(10 + offset, 37)),
            Ink(Draw(e, 30, 60)));
    }

    [Fact]
    public void Box_BreaksALongWordByCharacter_AndNewlinesStillBreak()
    {
        // 한 글자 4px, 상자 9px → 두 글자씩
        var layout = TextLayout.Measure(Bang with { Text = "!!!!!\n!", BoxWidth = 9 });
        Assert.Equal([(0, 2), (2, 2), (4, 1), (6, 1)], layout.Lines.Select(l => (l.Start, l.Length)));
        Assert.InRange(layout.Ink.Right, 0, 9);

        // 상자가 없으면 줄을 바꾸지 않고, 글 상자 = 줄 범위(빈 글도 1px × 한 줄)
        var auto = TextLayout.Measure(Bang with { Text = "!!!!!", X = 10, Align = TextAlign.Center });
        Assert.Single(auto.Lines);
        Assert.Equal(new Rectangle(0, 0, 20, 16), auto.Frame);
        Assert.Equal(new Rectangle(3, 7, 1, 16), TextLayout.Measure(Bang with { Text = "", X = 3, Y = 7 }).Frame);
    }

    [Fact]
    public void Caret_AndHitTest_RoundTrip_ForHangulNewlinesSurrogatesAndWrappedLines()
    {
        foreach (var e in new[]
                 {
                     Bang with { Text = "가A\n\n!\U0001F600b", X = 3, Y = 5, LineSpacing = 2 },
                     Bang with { Text = "!! 가나 !!!!!\n!", BoxWidth = 13, Align = TextAlign.Center },
                 })
        {
            var layout = TextLayout.Measure(e);
            for (int i = 0; i <= e.Text.Length; i++)
            {
                var caret = layout.CaretRect(i);
                if (i > 0 && i < e.Text.Length && char.IsHighSurrogate(e.Text[i - 1]) && char.IsLowSurrogate(e.Text[i]))
                {
                    Assert.Equal(layout.CaretRect(i - 1), caret); // 서로게이트 쌍 가운데 = 쌍 앞
                    continue;
                }
                Assert.Equal(1, caret.Width);
                Assert.Equal(i, layout.HitTest(new Point(caret.X, caret.Y + caret.Height / 2)));
            }
        }

        var plain = TextLayout.Measure(Bang with { Text = "가A\n\n!b", X = 3, Y = 5, LineSpacing = 2 });
        Assert.Equal([5, 5, 5, 23, 41, 41, 41], Enumerable.Range(0, 7).Select(i => plain.CaretRect(i).Y));
        Assert.True(plain.CaretRect(1).X - plain.CaretRect(0).X >= 8, "가 폭");
        Assert.Equal(2, plain.HitTest(new Point(200, 10)));    // 첫 줄 오른쪽 끝 = 줄바꿈 앞
        Assert.Equal(0, plain.HitTest(new Point(-50, -50)));
        Assert.Equal(6, plain.HitTest(new Point(500, 500)));
        Assert.Equal(3, plain.HitTest(new Point(100, 30)));    // 빈 줄

        // 선택: 첫 줄 일부 + 줄바꿈, 빈 줄(줄바꿈), 셋째 줄 첫 글자
        var rects = plain.SelectionRects(1, 5);
        Assert.Equal([5, 23, 41], rects.Select(r => r.Y));
        Assert.Equal(plain.CaretRect(1).X, rects[0].X);
        Assert.True(rects[0].Right > plain.CaretRect(2).X);
        Assert.Equal((plain.CaretRect(4).X, plain.CaretRect(5).X - plain.CaretRect(4).X), (rects[2].X, rects[2].Width));
        Assert.Empty(plain.SelectionRects(2, 2));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(16, 1)]
    [InlineData(20, 1)]
    [InlineData(32, 2)]
    [InlineData(48, 3)]
    public void Size_ScalesBitmapFontsByAWholeFactor(int size, int factor)
    {
        var e = Bang with { Size = size };
        Assert.Equal(new Rectangle(factor, 3 * factor, factor, 11 * factor), TextLayout.Bounds(e));
        Assert.Equal(16 * factor, TextLayout.Measure(e).Lines[0].Height);
        Assert.Equal(10 * factor * factor, Ink(Draw(e, 60, 200)).Length);
        Assert.Equal(4 * factor, TextLayout.Measure(e with { Text = "!!" }).CaretRect(1).X);
    }

    [Fact]
    public void ScaleXY_StretchBitmapFontsNearestNeighbour()
    {
        Assert.Equal(new Rectangle(2, 3, 2, 11), TextLayout.Bounds(Bang with { ScaleX = 200 }));
        Assert.Equal(new Rectangle(1, 9, 1, 33), TextLayout.Bounds(Bang with { ScaleY = 300 }));
        Assert.Equal(48, TextLayout.Measure(Bang with { ScaleY = 300 }).Lines[0].Height);
        Assert.Equal(new Rectangle(4, 9, 4, 33), TextLayout.Bounds(Bang with { Size = 32, ScaleX = 200, ScaleY = 150 }));
        // 반 배율: "!!" 펜 이동 4 → 2
        Assert.Equal(2, TextLayout.Measure(Bang with { Text = "!!", ScaleX = 50 }).CaretRect(1).X);
    }

    [Fact]
    public void BoldAndItalic_ChangeTheInk()
    {
        // 굵게: 오른쪽으로 1도트 넓히고 펜 이동도 1 더
        var bold = Draw(Bang with { Bold = true }, 10, 20);
        Assert.Equal(Points(BangAt(1, 3), BangAt(2, 3)), Ink(bold));
        Assert.Equal(new Rectangle(1, 3, 7, 11), TextLayout.Bounds(Bang with { Text = "!!", Bold = true }));

        // 기울임: 기준선 위 줄(맨 아래)은 그대로, 위로 4줄마다 오른쪽으로 1
        var italic = Ink(Draw(Bang with { Italic = true }, 10, 20));
        Assert.Equal(10, italic.Length);
        Assert.Equal(new Point(1, 13), italic[^1]);
        Assert.Equal(new Point(3, 3), italic[0]);
        Assert.Equal(new Rectangle(1, 3, 3, 11), TextLayout.Bounds(Bang with { Italic = true }));
        Assert.NotEqual(Ink(Draw(new TextEffect { Text = "가A" }, 40, 20)), Ink(Draw(new TextEffect { Text = "가A", Bold = true, Italic = true }, 40, 20)));
    }

    [Fact]
    public void Style_RoundTripsThroughTheEffect_AndClampsRanges()
    {
        var style = new TextStyle
        {
            FontName = "Galmuri9", Size = 18, Bold = true, Italic = true, Smooth = true, LetterSpacing = -3, LineSpacing = 7, ScaleX = 80, ScaleY = 250, Align = TextAlign.Right,
        };
        var e = style.ApplyTo(Bang with { X = 4, BoxWidth = 30, Typewriter = true });
        Assert.Equal(style, TextStyle.From(e));
        Assert.Equal((Bang.Text, Bang.Color, 4, 30, true), (e.Text, e.Color, e.X, e.BoxWidth, e.Typewriter));

        var clamped = new TextStyle { Size = 999, LetterSpacing = -99, LineSpacing = 999, ScaleX = 1, ScaleY = 9999 }.ApplyTo(new TextEffect());
        Assert.Equal((TextEffect.MaxSize, TextEffect.MinLetterSpacing, TextEffect.MaxLineSpacing, TextEffect.MinScale, TextEffect.MaxScale),
            (clamped.Size, clamped.LetterSpacing, clamped.LineSpacing, clamped.ScaleX, clamped.ScaleY));
        Assert.Equal((0, TextEffect.MaxBoxSize), ((Bang with { BoxWidth = -5 }).BoxWidth, (Bang with { BoxHeight = 99999 }).BoxHeight));
    }
}
