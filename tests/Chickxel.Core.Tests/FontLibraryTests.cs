using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;
using Chickxel.Core.Text;

namespace Chickxel.Core.Tests;

/// <summary>
/// 사용자 글꼴 폴더(더하기·지우기·없는 글꼴 대신 그리기·밖에서 넣은 파일)와 벡터 글꼴 그리기(또렷하게·부드럽게·크기·없는 글자 대신 그리기).
/// <see cref="FontLibrary.UserFolder"/>는 전역이라 이 클래스 안에서만 바꾼다(테스트마다 새 임시 폴더, 끝나면 null).
/// 견본 글꼴 fixtures/fonts/ChickxelTestPixel.ttf: em 800, 1도트 = 100단위(크기 8에서 1px), ascent 7·descent 1, "A"(5×7)·"I"·"D"(직각삼각형)만 있음.
/// </summary>
public sealed class FontLibraryTests : IDisposable
{
    private const string TestFont = "ChickxelTestPixel.ttf";
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "fonts", TestFont);

    private static readonly string[] A = [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "chickxel-fonts-" + Guid.NewGuid().ToString("N"));

    public FontLibraryTests() => FontLibrary.UserFolder = _folder;

    public void Dispose()
    {
        FontLibrary.UserFolder = null;
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private static PixelImage Draw(TextEffect e, int width = 40, int height = 40)
    {
        var image = new PixelImage(width, height);
        LayerEffects.Apply(image, [e], new Rectangle(0, 0, width, height));
        return image;
    }

    private static Point[] Ink(PixelImage image)
    {
        var list = new List<Point>();
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image[x, y].A != 0) list.Add(new Point(x, y));
        return [.. list];
    }

    /// <summary>도트 무늬(위에서 아래 줄, '#' = 잉크)를 <paramref name="scale"/>배로 (left, top)에 놓은 점들(<see cref="Ink"/> 차례).</summary>
    private static Point[] Pattern(string[] rows, int left, int top, int scale = 1)
        => [.. (from r in Enumerable.Range(0, rows.Length * scale)
                from c in Enumerable.Range(0, rows[0].Length * scale)
                where rows[r / scale][c / scale] == '#'
                select new Point(left + c, top + r)).OrderBy(p => p.Y).ThenBy(p => p.X)];

    [Fact]
    public void Add_CopiesIntoTheFolder_ListsFinds_AndRemoveFallsBackToTheDefaultFont()
    {
        int changed = 0;
        void OnChanged() => Interlocked.Increment(ref changed);
        FontLibrary.Changed += OnChanged;
        try
        {
            var missing = new TextEffect { Text = "AI", FontName = TestFont, Color = Color32.Black };
            Assert.True(FontLibrary.IsMissing(TestFont));
            var fallback = Ink(Draw(missing));
            Assert.Equal(Ink(Draw(missing with { FontName = FontLibrary.DefaultName })), fallback);

            var info = FontLibrary.Add(Fixture);
            Assert.Equal(new FontInfo(TestFont, "Chickxel Test Pixel", FontKind.Vector, 0, false), info);
            Assert.True(File.Exists(Path.Combine(_folder, TestFont)));
            Assert.True(File.Exists(Fixture));
            Assert.Equal(info, FontLibrary.Find(TestFont.ToUpperInvariant()));
            Assert.False(FontLibrary.IsMissing(TestFont));
            Assert.Equal(info, FontLibrary.List()[^1]);
            Assert.All(FontLibrary.List().Take(5), f => Assert.True(f.BuiltIn));
            Assert.NotEqual(fallback, Ink(Draw(missing)));

            // 같은 이름이 있으면 새 이름, 폴더 안 파일을 다시 더하면 복사하지 않음
            Assert.Equal("ChickxelTestPixel (2).ttf", FontLibrary.Add(Fixture).Name);
            Assert.Equal(info, FontLibrary.Add(Path.Combine(_folder, TestFont)));
            Assert.Equal(2, Directory.GetFiles(_folder).Length);

            FontLibrary.Remove(TestFont);
            FontLibrary.Remove("ChickxelTestPixel (2).ttf");
            FontLibrary.Remove("nothing.ttf");
            Assert.Empty(Directory.GetFiles(_folder));
            Assert.True(FontLibrary.IsMissing(TestFont));
            Assert.Equal(fallback, Ink(Draw(missing)));
            Assert.Throws<InvalidOperationException>(() => FontLibrary.Remove("Galmuri9"));
            Assert.True(changed >= 3, changed.ToString());
        }
        finally
        {
            FontLibrary.Changed -= OnChanged;
        }
    }

    [Fact]
    public void Add_RejectsUnsupportedAndBrokenFiles_WithoutCopying()
    {
        var dir = Directory.CreateTempSubdirectory("chickxel-font-src").FullName;
        try
        {
            var text = Path.Combine(dir, "note.txt");
            File.WriteAllText(text, "hello");
            Assert.Throws<InvalidDataException>(() => FontLibrary.Add(text));
            var broken = Path.Combine(dir, "broken.ttf");
            File.WriteAllBytes(broken, [1, 2, 3, 4, 5, 6, 7, 8]);
            var error = Assert.Throws<InvalidDataException>(() => FontLibrary.Add(broken));
            Assert.Contains("broken.ttf", error.Message);
            var badBdf = Path.Combine(dir, "bad.bdf");
            File.WriteAllText(badBdf, "STARTFONT 2.1\nCHARS 0\nENDFONT\n");
            Assert.Throws<InvalidDataException>(() => FontLibrary.Add(badBdf));
            Assert.False(Directory.Exists(_folder) && Directory.GetFiles(_folder).Length > 0);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FilesPutIntoTheFolderFromOutside_AreFoundOnRescan()
    {
        Directory.CreateDirectory(_folder);
        int changed = 0;
        void OnChanged() => Interlocked.Increment(ref changed);
        FontLibrary.Changed += OnChanged;
        try
        {
            File.Copy(Fixture, Path.Combine(_folder, "Outside.ttf"));
            File.WriteAllText(Path.Combine(_folder, "readme.txt"), "not a font");
            Assert.Contains(FontLibrary.List(), f => f.Name == "Outside.ttf" && f.Kind == FontKind.Vector);
            Assert.DoesNotContain(FontLibrary.List(), f => f.Name == "readme.txt");
            Assert.Equal(1, changed);
        }
        finally
        {
            FontLibrary.Changed -= OnChanged;
        }
    }

    [Fact]
    public void BdfUserFont_ScalesByWholeFactor_AndMissingGlyphsUseGalmuri11()
    {
        var dir = Directory.CreateTempSubdirectory("chickxel-font-src").FullName;
        try
        {
            // 줄 높이 4(ascent 3, descent 1), "A" = 2×3 꽉 찬 사각형(펜 + 0, 기준선 위)
            var bdf = Path.Combine(dir, "Tiny.bdf");
            File.WriteAllText(bdf, """
                STARTFONT 2.1
                FONT -Test-Tiny-Bold-R-Normal--4-40-75-75-C-30-ISO10646-1
                SIZE 4 75 75
                FONTBOUNDINGBOX 3 4 0 -1
                STARTPROPERTIES 4
                FAMILY_NAME "Tiny"
                WEIGHT_NAME "Bold"
                FONT_ASCENT 3
                FONT_DESCENT 1
                ENDPROPERTIES
                CHARS 1
                STARTCHAR A
                ENCODING 65
                SWIDTH 750 0
                DWIDTH 3 0
                BBX 2 3 0 0
                BITMAP
                C0
                C0
                C0
                ENDCHAR
                ENDFONT
                """);
            var info = FontLibrary.Add(bdf);
            Assert.Equal(new FontInfo("Tiny.bdf", "Tiny Bold", FontKind.Bitmap, 4, false), info);

            var e = new TextEffect { Text = "A", FontName = "Tiny.bdf", Size = 8 };
            Assert.Equal(new Rectangle(0, 0, 4, 6), TextLayout.Bounds(e));
            Assert.Equal(8, TextLayout.Measure(e).Lines[0].Height);
            Assert.Equal(6, TextLayout.Measure(e with { Text = "AA" }).CaretRect(1).X);

            // 없는 글자 "!"는 Galmuri11(1배, 같은 기준선): Galmuri11 '!'은 기준선 위 11줄, 펜 + 1
            var bang = TextLayout.Bounds(e with { Text = "!", Size = 0 });
            Assert.Equal(new Rectangle(1, 3 - 11, 1, 11), bang);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void VectorFont_IsCrispByDefault_AndSmoothGivesPartialAlpha()
    {
        FontLibrary.Add(Fixture);
        var e = new TextEffect { Text = "A", FontName = TestFont, Size = 8, X = 2, Y = 3, Color = new Color32(10, 20, 30) };
        var layout = TextLayout.Measure(e);
        Assert.Equal(8, layout.Lines[0].Height);
        var crisp = Draw(e);
        Assert.Equal(Pattern(A, 2, 3), Ink(crisp));
        Assert.All(Ink(crisp), p => Assert.Equal(e.Color, crisp[p.X, p.Y]));

        // 크기 16 = 2배, 가로 200% = 가로만 2배
        Assert.Equal(Pattern(A, 2, 3, 2), Ink(Draw(e with { Size = 16 })));
        Assert.Equal(new Rectangle(2, 3, 10, 7), TextLayout.Bounds(e with { ScaleX = 200 }));
        Assert.Equal(new Rectangle(2, 3, 11, 7), TextLayout.Bounds(e with { Text = "AA" }));
        Assert.Equal(8, TextLayout.Measure(e with { Text = "AA" }).CaretRect(1).X);

        // 비스듬한 변: 또렷하게는 알파 0/255만, 부드럽게는 반투명 픽셀이 생김
        var diagonal = e with { Text = "D", Size = 16 };
        var sharp = Draw(diagonal);
        Assert.All(Ink(sharp), p => Assert.Equal(255, sharp[p.X, p.Y].A));
        var smooth = Draw(diagonal with { Smooth = true });
        Assert.Contains(Ink(smooth), p => smooth[p.X, p.Y].A is > 0 and < 255);
        Assert.NotEmpty(Ink(sharp));

        // 굵게·기울임은 잉크를 바꾼다
        Assert.True(TextLayout.Bounds(e with { Text = "I", Bold = true }).Width > TextLayout.Bounds(e with { Text = "I" }).Width);
        Assert.NotEqual(Ink(crisp), Ink(Draw(e with { Italic = true })));
    }

    [Fact]
    public void VectorFont_MissingGlyphs_UseGalmuri11OnTheSameBaseline()
    {
        FontLibrary.Add(Fixture);
        // 견본 글꼴 기준선 = 줄 위 + 7, Galmuri11 기준선 = 줄 위 + 14 → 대신 그린 글자는 7px 위
        var vector = new TextEffect { Text = "가", FontName = TestFont, Size = 8, Y = 20 };
        var galmuri = vector with { FontName = FontLibrary.DefaultName, Size = 0 };
        Assert.Equal(Ink(Draw(galmuri)).Select(p => p with { Y = p.Y - 7 }), Ink(Draw(vector)));
        // 크기 24 → Galmuri11 2배
        Assert.Equal(TextLayout.Bounds(galmuri with { Size = 32 }).Size, TextLayout.Bounds(vector with { Size = 24 }).Size);
    }
}
