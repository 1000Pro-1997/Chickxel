using System.Drawing;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

public class ShapeStampTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 HalfBlue = new(0, 0, 255, 128);

    /// <summary>3×2 모양: 위 줄 빨강·투명·반투명 파랑, 아래 줄 빨강 셋.</summary>
    private static PixelImage Shape()
    {
        var s = new PixelImage(3, 2);
        s.Set(0, 0, Red);
        s.Set(2, 0, HalfBlue);
        s.Set(0, 1, Red);
        s.Set(1, 1, Red);
        s.Set(2, 1, Red);
        return s;
    }

    [Fact]
    public void Origin_CentersLikeTheBrush_EvenSizesGrowRightAndDown()
    {
        Assert.Equal(new Point(4, 4), ShapeStamp.Origin(new Point(5, 5), 3, 3));
        Assert.Equal(new Point(4, 5), ShapeStamp.Origin(new Point(5, 5), 4, 2));
        Assert.Equal(new Point(5, 5), ShapeStamp.Origin(new Point(5, 5), 1, 1));
    }

    [Fact]
    public void Stamp_BlendsOverAndKeepsWhatIsUnderTransparentPixels()
    {
        var target = new PixelImage(5, 5);
        target.Clear(Color32.White);
        ShapeStamp.Stamp(target, Shape(), new Point(2, 2));

        // 왼쪽 위 = (1, 2)
        Assert.Equal(Red, target.Get(1, 2));
        Assert.Equal(Color32.White, target.Get(2, 2));
        Assert.Equal(Color32.BlendOver(Color32.White, HalfBlue), target.Get(3, 2));
        Assert.Equal(Red, target.Get(3, 3));
        Assert.Equal(Color32.White, target.Get(1, 1));
    }

    [Fact]
    public void Stamp_OnTransparentKeepsTheShapeAlpha()
    {
        var target = new PixelImage(5, 5);
        ShapeStamp.Stamp(target, Shape(), new Point(2, 2));
        Assert.Equal(HalfBlue, target.Get(3, 2));
    }

    [Fact]
    public void Stamp_RespectsClipAndImageEdge()
    {
        var target = new PixelImage(3, 3);
        var clip = new PixelMask(new Rectangle(0, 0, 3, 1));
        ShapeStamp.Stamp(target, Shape(), new Point(0, 0), clip);
        // 왼쪽 위 = (-1, 0): 위 줄 (0,0)은 투명 칸, (1,0)은 반투명 파랑. 아래 줄은 잘림 영역 밖.
        Assert.Equal(Color32.Transparent, target.Get(0, 0));
        Assert.Equal(HalfBlue, target.Get(1, 0));
        Assert.Equal(Color32.Transparent, target.Get(0, 1));
    }

    [Fact]
    public void Erase_RemovesByShapeOpacity()
    {
        var target = new PixelImage(5, 5);
        target.Clear(Red);
        ShapeStamp.Stamp(target, Shape(), new Point(2, 2), erase: true);
        Assert.Equal(Color32.Transparent, target.Get(1, 2));
        Assert.Equal(Red, target.Get(2, 2));
        Assert.Equal(127, target.Get(3, 2).A);
        Assert.Equal(Red.WithAlpha(127), target.Get(3, 2));
    }

    [Fact]
    public void Tint_MainColorBecomesTheColor_ShadingAndAlphaStay()
    {
        var dark = new Color32(0x2B, 0x53, 0x29);
        var mid = new Color32(0x4C, 0x9A, 0x3A);
        var s = new PixelImage(4, 1);
        s.Set(0, 0, mid);
        s.Set(1, 0, mid);
        s.Set(2, 0, dark.WithAlpha(100));
        s.Set(3, 0, new Color32(0x6B, 0x4A, 0x2A));
        var target = new Color32(40, 120, 200);

        var tinted = ShapeStamp.Tint(s, target);

        AssertNear(target, tinted.Get(0, 0));
        var shade = tinted.Get(2, 0);
        Assert.Equal(100, shade.A);
        // 어두운 테두리는 여전히 더 어둡다
        Assert.True(shade.ToHsv().V < target.ToHsv().V);
        Assert.InRange(shade.ToHsv().H, target.ToHsv().H - 4, target.ToHsv().H + 4);
        // 다른 색상(갈색 줄기)도 같은 색상으로 — 색상 차를 지키면 엉뚱한 색이 된다
        Assert.InRange(tinted.Get(3, 0).ToHsv().H, target.ToHsv().H - 4, target.ToHsv().H + 4);
    }

    [Fact]
    public void Tint_GrayShapeTakesTheColor()
    {
        var s = new PixelImage(1, 1);
        s.Set(0, 0, new Color32(128, 128, 128));
        var tinted = ShapeStamp.Tint(s, new Color32(200, 40, 40));
        AssertNear(new Color32(200, 40, 40), tinted.Get(0, 0));
    }

    private static void AssertNear(Color32 expected, Color32 actual)
    {
        Assert.InRange(actual.R, expected.R - 1, expected.R + 1);
        Assert.InRange(actual.G, expected.G - 1, expected.G + 1);
        Assert.InRange(actual.B, expected.B - 1, expected.B + 1);
        Assert.Equal(expected.A, actual.A);
    }

    [Fact]
    public void Picker_Sequential_WrapsAround()
    {
        var picker = new StampPicker(1);
        Assert.Equal(0, picker.First(3, StampOrder.Sequential));
        Assert.Equal(1, picker.Next(0, 3, StampOrder.Sequential));
        Assert.Equal(2, picker.Next(1, 3, StampOrder.Sequential));
        Assert.Equal(0, picker.Next(2, 3, StampOrder.Sequential));
        Assert.Equal(0, picker.Next(0, 1, StampOrder.Sequential));
    }

    [Fact]
    public void Picker_Selected_KeepsTheChosenShape()
    {
        var picker = new StampPicker(3);
        Assert.Equal(0, picker.First(5, StampOrder.Selected));
        Assert.Equal(2, picker.Next(2, 5, StampOrder.Selected));
        Assert.Equal(4, picker.Next(4, 5, StampOrder.Selected));
        Assert.Equal(4, picker.Next(9, 5, StampOrder.Selected));
    }

    [Fact]
    public void Picker_Random_IsSeededAndNeverRepeatsTheLastShape()
    {
        var a = new StampPicker(42);
        var b = new StampPicker(42);
        int ca = a.First(4, StampOrder.Random), cb = b.First(4, StampOrder.Random);
        Assert.Equal(ca, cb);
        var seen = new HashSet<int> { ca };
        for (int i = 0; i < 200; i++)
        {
            int na = a.Next(ca, 4, StampOrder.Random), nb = b.Next(cb, 4, StampOrder.Random);
            Assert.Equal(na, nb);
            Assert.NotEqual(ca, na);
            Assert.InRange(na, 0, 3);
            seen.Add(na);
            ca = na;
            cb = nb;
        }
        Assert.Equal(4, seen.Count);
        Assert.Equal(0, a.Next(0, 1, StampOrder.Random));
    }

    [Fact]
    public void Trail_StampsEverySpacingAlongTheDrag()
    {
        var trail = new StampTrail(new Point(0, 0));
        var every4 = StampTrail.Spacing(4);
        var points = trail.MoveTo(new Point(10, 0), every4).ToList();
        Assert.Equal([new Point(4, 0), new Point(8, 0)], points);
        // 이어서 끌면 마지막 도장(8,0)에서 다시 잰다
        Assert.Equal([new Point(12, 0)], trail.MoveTo(new Point(13, 0), every4).ToList());
        Assert.Empty(trail.MoveTo(new Point(13, 0), every4));
    }

    [Theory]
    [InlineData(12, 0)]
    [InlineData(9, 9)]
    [InlineData(-4, 11)]
    [InlineData(7, -3)]
    public void Trail_SpacingOne_StampsEveryPixelSoTheTrailIsContinuous(int dx, int dy)
    {
        var start = new Point(20, 20);
        var end = new Point(start.X + dx, start.Y + dy);
        var trail = new StampTrail(start);
        // 포인터가 몇 번에 나눠 움직여도 같은 자국
        var points = new List<Point> { start };
        var mid = new Point(start.X + dx / 2, start.Y + dy / 3);
        points.AddRange(trail.MoveTo(mid, StampTrail.Spacing(1)));
        points.AddRange(trail.MoveTo(end, StampTrail.Spacing(1)));
        Assert.Equal(end, points[^1]);
        for (int i = 1; i < points.Count; i++)
        {
            Assert.InRange(Math.Abs(points[i].X - points[i - 1].X), 0, 1);
            Assert.InRange(Math.Abs(points[i].Y - points[i - 1].Y), 0, 1);
            Assert.NotEqual(points[i - 1], points[i]);
        }
    }

    /// <summary>3×3 불투명 네모와 모서리가 투명한 3×3 십자.</summary>
    private static PixelImage Square() => Fill(3, 3, (_, _) => true);

    private static PixelImage Plus() => Fill(3, 3, (x, y) => x == 1 || y == 1);

    /// <summary>5×3인데 가운데 줄만 불투명(투명한 둘레가 있는 모양).</summary>
    private static PixelImage Bar() => Fill(5, 3, (_, y) => y == 1);

    private static PixelImage Fill(int w, int h, Func<int, int, bool> opaque)
    {
        var image = new PixelImage(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (opaque(x, y)) image.Set(x, y, Red);
        return image;
    }

    /// <summary>
    /// 캔버스처럼 최적화 간격으로 끈다: 다음 모양(<paramref name="shapes"/>를 돌아가며)이 앞 도장과 겹치지 않는 첫 자리에 찍는다.
    /// 포인터는 몇 번에 나눠 움직인다. 찍은 (모양, 가운데)를 돌려준다.
    /// </summary>
    private static List<(PixelImage Shape, Point At)> OptimizedDrag(PixelImage[] shapes, Point from, Point to)
    {
        var stamps = new List<(PixelImage, Point)> { (shapes[0], from) };
        var trail = new StampTrail(from);
        bool Due(Point last, Point p) => !ShapeStamp.Overlaps(stamps[^1].Item1, last, shapes[stamps.Count % shapes.Length], p);
        for (int step = 1; step <= 4; step++)
        {
            var at = new Point(from.X + (to.X - from.X) * step / 4, from.Y + (to.Y - from.Y) * step / 4);
            foreach (var p in trail.MoveTo(at, Due)) stamps.Add((shapes[stamps.Count % shapes.Length], p));
        }
        return stamps;
    }

    /// <summary>두 도장의 불투명 픽셀이 8방향으로 닿는지(겹치지 않고 붙음).</summary>
    private static bool Touch(PixelImage a, Point ca, PixelImage b, Point cb)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx, dy) != (0, 0) && ShapeStamp.Overlaps(a, ca, b, new Point(cb.X + dx, cb.Y + dy))) return true;
        return false;
    }

    [Theory]
    [InlineData(30, 0)]
    [InlineData(0, -30)]
    [InlineData(24, 24)]
    [InlineData(-21, 21)]
    [InlineData(30, 11)]
    public void Trail_OptimizedSpacing_StampsTouchWithoutOverlap(int dx, int dy)
    {
        PixelImage[][] sets = [[Square()], [Plus()], [Square(), Plus(), Bar()], [Bar(), Square()]];
        foreach (var shapes in sets)
        {
            var stamps = OptimizedDrag(shapes, new Point(40, 40), new Point(40 + dx, 40 + dy));
            Assert.True(stamps.Count >= 4);
            for (int i = 1; i < stamps.Count; i++)
            {
                var (a, ca) = stamps[i - 1];
                var (b, cb) = stamps[i];
                Assert.False(ShapeStamp.Overlaps(a, ca, b, cb));
                Assert.True(Touch(a, ca, b, cb), $"stamp {i} at {cb} does not touch {ca}");
            }
        }
    }

    [Fact]
    public void Trail_OptimizedSpacing_IgnoresTransparentMarginsAndUsesTheNextShape()
    {
        // 네모는 3칸마다, 십자는 대각선이면 모서리가 비어 2칸마다
        Assert.Equal(new Point(43, 40), OptimizedDrag([Square()], new Point(40, 40), new Point(50, 40))[1].At);
        Assert.Equal(new Point(42, 42), OptimizedDrag([Plus()], new Point(40, 40), new Point(50, 50))[1].At);
        // 세로로 끌면 가로 막대(가운데 줄만 불투명)는 1칸마다 — 위아래 투명 줄은 셈하지 않음
        Assert.Equal(new Point(40, 41), OptimizedDrag([Bar()], new Point(40, 40), new Point(40, 50))[1].At);
        // 다음 모양이 다르면 그 모양으로 잰다: 막대(반 폭 2) 다음 네모(반 폭 1)는 4칸
        Assert.Equal(new Point(44, 40), OptimizedDrag([Bar(), Square()], new Point(40, 40), new Point(50, 40))[1].At);
    }

    [Fact]
    public void Overlaps_CountsOnlyOpaquePixels()
    {
        Assert.True(ShapeStamp.Overlaps(Square(), new Point(0, 0), Square(), new Point(2, 2)));
        Assert.False(ShapeStamp.Overlaps(Square(), new Point(0, 0), Square(), new Point(3, 0)));
        Assert.False(ShapeStamp.Overlaps(Plus(), new Point(0, 0), Plus(), new Point(2, 2)));
        Assert.False(ShapeStamp.Overlaps(Bar(), new Point(0, 0), Bar(), new Point(0, 1)));
    }

    [Fact]
    public void Library_RoundTripsSetsShapesOrderAndAlpha()
    {
        var library = new StampLibrary();
        var first = new StampSet { Name = "First" };
        first.Shapes.Add(Plus());
        var mine = new StampSet { Name = "Mine", Order = StampOrder.Sequential };
        mine.Shapes.Add(Shape());
        library.Sets.AddRange([first, mine]);
        library.Sets[0].Name = "Renamed";

        var back = StampLibrary.Parse(library.Serialize());

        Assert.NotNull(back);
        Assert.Equal(library.Sets.Select(s => s.Id), back.Sets.Select(s => s.Id));
        Assert.Equal("Renamed", back.Sets[0].DisplayName);
        var copy = back.Find(mine.Id)!;
        Assert.Equal("Mine", copy.Name);
        Assert.Equal(StampOrder.Sequential, copy.Order);
        Assert.True(copy.Shapes[0].ContentEquals(Shape()));
        Assert.Equal(HalfBlue, copy.Shapes[0].Get(2, 0));
        Assert.True(back.Sets[0].Shapes[0].ContentEquals(Plus()));
    }

    [Fact]
    public void Library_RejectsBrokenFiles()
    {
        Assert.Null(StampLibrary.Parse("{ not json"));
        Assert.Null(StampLibrary.Parse("""{"version":1,"sets":[{"id":"a","shapes":["not base64!"]}]}"""));
        Assert.Null(StampLibrary.Parse("""{"version":1,"sets":[{"id":"a","shapes":["AAAA"]}]}"""));
    }

    [Fact]
    public void Library_OldBuiltIns_UnchangedOfficialCopiesDropped_OthersStayAsNamedUserSets()
    {
        // 1.14.122에서 저장한 파일: 나뭇잎·반짝이는 처음 그대로(이제 받은 세트로 설치되는 공식 세트), 단풍잎은 고쳤고, 풀은 공식 세트가 아니다
        var shape = Convert.ToBase64String(PngBytes(Shape()));
        string Shapes(string packageId) => string.Join(",", OfficialShapeSets.Packages.Single(p => p.Id == packageId).Create().Shapes
            .Select(s => $"\"{Convert.ToBase64String(PngBytes(s))}\""));
        var old = $$"""
            {"version":1,"knownBuiltIns":["leaves","autumn","grass","stars","bubbles"],"sets":[
              {"id":"leaves","shapes":[{{Shapes("leaves")}}]},
              {"id":"autumn","shapes":["{{shape}}"]},
              {"id":"mine","name":"Mine","shapes":["{{shape}}"]},
              {"id":"sparkles","shapes":[{{Shapes("sparkles")}}]},
              {"id":"grass","shapes":["{{shape}}"]}]}
            """;

        var library = StampLibrary.Parse(old)!;

        Assert.Equal(["autumn", "mine", "grass"], library.Sets.Select(s => s.Id));
        Assert.Equal(StampBuiltIns.RetiredName("autumn"), library.Find("autumn")!.Name);
        Assert.Equal(StampBuiltIns.RetiredName("grass"), library.Find("grass")!.Name);
        // 이름을 붙여 저장하면 다시 읽어도 그대로(이름 있는 세트는 공식 사본으로 보지 않음)
        var again = StampLibrary.Parse(library.Serialize())!;
        Assert.Equal(library.Sets.Select(s => (s.Id, s.Name)), again.Sets.Select(s => (s.Id, s.Name)));
        Assert.DoesNotContain("knownBuiltIns", library.Serialize());
    }

    [Fact]
    public void Library_RetiredBuiltInsInOldFile_StayAsNamedEditableUserSets()
    {
        // 1.14.120에서 저장한 파일: 기본 세트 스무 가지를 알았고, 솔잎은 이름 없이(기본 세트 이름), 빗방울은 사용자가 이름을 바꿨고, 구름은 지웠다
        var shape = Convert.ToBase64String(PngBytes(Shape()));
        string[] known = ["leaves", "autumn", "pine", "grass", "flowers", "petals", "sparkles", "stars", "dots", "bubbles",
            "clouds", "rocks", "bricks", "rain", "snow", "hearts", "puffs", "wood", "cracks", "treasure"];
        var old = $$"""
            {"version":1,"knownBuiltIns":[{{string.Join(",", known.Select(id => $"\"{id}\""))}}],"sets":[
              {"id":"leaves","shapes":["{{shape}}"]},
              {"id":"pine","shapes":["{{shape}}","{{shape}}"]},
              {"id":"rain","name":"My rain","order":"sequential","shapes":["{{shape}}"]},
              {"id":"grass","shapes":["{{shape}}"]},
              {"id":"stars","shapes":["{{shape}}"]}]}
            """;

        var library = StampLibrary.Parse(old)!;

        // 지운 예전 기본 세트(구름 등)는 돌아오지 않고, 남은 예전 기본 세트는 그대로 남는다(고친 나뭇잎도)
        Assert.Equal(["leaves", "pine", "rain", "grass", "stars"], library.Sets.Select(s => s.Id));
        var pine = library.Find("pine")!;
        Assert.Equal(StampBuiltIns.RetiredName("pine"), pine.Name);
        Assert.Equal(2, pine.Shapes.Count);
        Assert.True(pine.Shapes[0].ContentEquals(Shape()));
        var rain = library.Find("rain")!;
        Assert.Equal(("My rain", StampOrder.Sequential), (rain.Name, rain.Order));

        // 사용자 세트처럼 고쳐 저장하면 고친 대로 남는다
        pine.Name = "Needles";
        pine.Shapes.RemoveAt(1);
        var again = StampLibrary.Parse(library.Serialize())!;
        var pineAgain = again.Find("pine")!;
        Assert.Equal(("Needles", 1), (pineAgain.Name, pineAgain.Shapes.Count));
        Assert.Equal("My rain", again.Find("rain")!.Name);
        Assert.Null(again.Find("clouds"));
    }

    [Fact]
    public void Library_RetiredNameMatchingItsOwnId_GetsNoNumberSuffix()
    {
        // 영어 이름이 이름표와 대소문자만 다른 예전 기본 세트(flowers → "Flowers")는 자기 이름표와 겹친다고 보지 않는다. 다른 세트와 겹치면 번호를 붙인다
        var shape = Convert.ToBase64String(PngBytes(Shape()));
        var old = $$"""{"version":1,"sets":[{"id":"flowers","shapes":["{{shape}}"]},{"id":"hearts","shapes":["{{shape}}"]},{"id":"mine","name":"Hearts","shapes":[]}]}""";
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("en");
            var library = StampLibrary.Parse(old)!;
            Assert.Equal("Flowers", library.Find("flowers")!.Name);
            Assert.Equal("Hearts (2)", library.Find("hearts")!.Name);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Library_NewInstallIsEmpty_BuiltInSetsAreFiveOfficialPackages()
    {
        Assert.Empty(StampLibrary.Load(Path.Combine(Path.GetTempPath(), "chx-stamp-" + Guid.NewGuid().ToString("N"), StampLibrary.FileName)).Sets);
        Assert.Equal(["chickxel-chicks", "flowers", "flowers-2", "flowers-3", "sparkles", "bubbles", "autumn-leaves", "leaves"], OfficialShapeSets.Packages.Select(p => p.Id));
        Assert.All(StampBuiltIns.RetiredIds, id => Assert.NotNull(StampBuiltIns.RetiredName(id)));
        // 예전 기본 세트 이름표로 고른 설정은 같은 그림의 공식 세트로 잇는다
        Assert.Equal(Official("autumn-leaves").AssetId, OfficialShapeSets.AssetIdOfRetired("autumn"));
        Assert.Equal(Official("flowers").AssetId, OfficialShapeSets.AssetIdOfRetired("flowers"));
        Assert.Null(OfficialShapeSets.AssetIdOfRetired("grass"));
    }

    private static byte[] PngBytes(PixelImage image)
    {
        using var stream = new MemoryStream();
        PngIO.Save(image, stream);
        return stream.ToArray();
    }

    [Fact]
    public void Library_LoadMissingGivesEmpty_BrokenIsKeptAside()
    {
        var dir = Path.Combine(Path.GetTempPath(), "chx-stamp-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, StampLibrary.FileName);
            Assert.Empty(StampLibrary.Load(path).Sets);

            var library = new StampLibrary();
            library.Sets.Add(new StampSet { Name = "Only" });
            library.Save(path);
            Assert.Equal("Only", Assert.Single(StampLibrary.Load(path).Sets).Name);

            File.WriteAllText(path, "{ broken");
            Assert.Empty(StampLibrary.Load(path).Sets);
            Assert.Equal("{ broken", File.ReadAllText(path + ".bad"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void OfficialSets_FiveSetsOfDistinctTrimmedShapes_SomeSemiTransparent_SameEveryTime()
    {
        var sets = OfficialShapeSets.Packages.Select(p => p.Create()).ToList();
        foreach (var set in sets)
        {
            Assert.InRange(set.Shapes.Count, 3, PreviewChicks.Ids.Count);
            for (int i = 0; i < set.Shapes.Count; i++)
                for (int j = i + 1; j < set.Shapes.Count; j++)
                    Assert.False(set.Shapes[i].ContentEquals(set.Shapes[j]), $"{set.Name}: shapes {i} and {j} are the same");
        }
        foreach (var shape in sets.SelectMany(s => s.Shapes))
        {
            Assert.Equal(shape.Bounds, shape.GetOpaqueBounds());
            Assert.False(StampLibrary.TooBig(shape));
        }
        Assert.Contains(sets.SelectMany(s => s.Shapes).SelectMany(s => s.Pixels), c => c.A is > 0 and < 255);
        Assert.All(sets, s => Assert.False(string.IsNullOrEmpty(s.Name)));
        // 씨앗 고정: 다시 만들어도 같은 그림, 같은 패키지 바이트(앱이 설치하는 것 = 상점에 올린 것)
        Assert.All(OfficialShapeSets.Packages, p => Assert.Equal(p.Build(), p.Build()));
    }

    [Fact]
    public void OfficialSets_UnchangedCopyIsOnlyAnUnnamedRetiredIdWithTheSameShapes()
    {
        var leaves = Official("leaves").Create();
        StampSet Copy(string id, string? name, IEnumerable<PixelImage> shapes)
        {
            var set = new StampSet { Id = id, Name = name };
            set.Shapes.AddRange(shapes);
            return set;
        }
        Assert.True(OfficialShapeSets.IsUnchangedCopy(Copy("leaves", null, leaves.Shapes)));
        Assert.False(OfficialShapeSets.IsUnchangedCopy(Copy("leaves", "Leaves", leaves.Shapes)));
        Assert.False(OfficialShapeSets.IsUnchangedCopy(Copy("leaves", null, leaves.Shapes.Skip(1))));
        Assert.False(OfficialShapeSets.IsUnchangedCopy(Copy("grass", null, leaves.Shapes)));
        Assert.False(OfficialShapeSets.IsUnchangedCopy(Copy("mine", null, leaves.Shapes)));
    }

    [Fact]
    public void Trim_CropsToContent()
    {
        var image = new PixelImage(10, 10);
        image.Set(3, 4, Red);
        image.Set(5, 6, HalfBlue);
        var trimmed = StampLibrary.Trim(image)!;
        Assert.Equal(new Size(3, 3), trimmed.Size);
        Assert.Equal(Red, trimmed.Get(0, 0));
        Assert.Equal(HalfBlue, trimmed.Get(2, 2));
        Assert.Null(StampLibrary.Trim(new PixelImage(4, 4)));
    }

    [Fact]
    public void SetFile_RoundTripsNameOrderAndAlpha_AsANewSet()
    {
        var set = new StampSet { Name = "Leaves of mine", Order = StampOrder.Selected };
        set.Shapes.Add(Shape());
        set.Shapes.Add(Plus());

        var back = StampLibrary.ParseSet(StampLibrary.SerializeSet(set));

        Assert.NotNull(back);
        Assert.NotEqual(set.Id, back.Id);
        Assert.Equal("Leaves of mine", back.Name);
        Assert.Equal(StampOrder.Selected, back.Order);
        Assert.Equal(2, back.Shapes.Count);
        Assert.True(back.Shapes[0].ContentEquals(Shape()));
        Assert.Equal(HalfBlue, back.Shapes[0].Get(2, 0));
        Assert.True(back.Shapes[1].ContentEquals(Plus()));
    }

    [Fact]
    public void SetFile_OfficialSetExportsItsName_AndImportGetsAFreshId()
    {
        var leaves = Official("leaves").Create();
        var json = StampLibrary.SerializeSet(leaves);
        Assert.DoesNotContain("\"id\"", json);
        var back = StampLibrary.ParseSet(json)!;
        Assert.Equal(leaves.DisplayName, back.Name);
        Assert.NotEqual(leaves.Id, back.Id);
        Assert.Equal(leaves.Shapes.Count, back.Shapes.Count);
    }

    [Fact]
    public void SetFile_RejectsBrokenOrForeignFiles()
    {
        Assert.Null(StampLibrary.ParseSet("{ broken"));
        Assert.Null(StampLibrary.ParseSet("""{"version":1,"set":{"name":"a","shapes":[]}}"""));
        Assert.Null(StampLibrary.ParseSet("""{"format":"something-else","version":1,"set":{"name":"a","shapes":[]}}"""));
        Assert.Null(StampLibrary.ParseSet("""{"format":"chickxel-shapes","version":1,"set":{"name":"a","shapes":["not base64!"]}}"""));
        Assert.Null(StampLibrary.ParseSet("""{"format":"chickxel-shapes","version":1,"set":{"name":"a","shapes":["AAAA"]}}"""));
        Assert.Null(StampLibrary.ParseSet("""{"format":"chickxel-shapes","version":1}"""));
        // 세트 모음 파일은 세트 파일이 아니다
        Assert.Null(StampLibrary.ParseSet(new StampLibrary().Serialize()));
    }

    [Fact]
    public void AddImported_AppendsAndNeverOverwrites_CollidingNamesGetANumber()
    {
        var library = new StampLibrary();
        library.Sets.Add(Official("leaves").Create());
        int before = library.Sets.Count;
        var leaves = library.Sets[0];
        var leavesName = leaves.DisplayName;

        var first = StampLibrary.ParseSet(StampLibrary.SerializeSet(leaves))!;
        library.AddImported(first, "file");
        var second = StampLibrary.ParseSet(StampLibrary.SerializeSet(leaves))!;
        library.AddImported(second, "file");
        var unnamed = new StampSet();
        library.AddImported(unnamed, "my-file");

        Assert.Equal(before + 3, library.Sets.Count);
        Assert.Same(unnamed, library.Sets[^1]);
        Assert.Equal($"{leavesName} (2)", first.Name);
        Assert.Equal($"{leavesName} (3)", second.Name);
        Assert.Equal("my-file", unnamed.Name);
        Assert.Equal(leavesName, leaves.Name);
        Assert.Equal("Fresh", library.UniqueName("Fresh"));
    }

    private static OfficialShapeSetPackage Official(string id) => OfficialShapeSets.Packages.Single(p => p.Id == id);

    [Fact]
    public void OfficialSets_FlowersAreOneShapePerSet_OnlyColorsDiffer()
    {
        // 무작위로 흩뿌릴 때 비슷한 것끼리 나오게: 꽃 세트마다 모양(투명 칸 배치)이 하나뿐
        foreach (var id in new[] { "flowers-2", "flowers-3" })
        {
            var masks = Official(id).Create().Shapes.Select(s => string.Concat(s.Pixels.Select(c => c.A > 0 ? '#' : '.'))).Distinct();
            Assert.Single(masks);
        }
        Assert.Equal(6, Official("flowers").Create().Shapes.Count); // 세 색 × 좌우
    }
}
