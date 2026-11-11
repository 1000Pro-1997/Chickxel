using System.Text;
using System.Text.Json.Nodes;
using Chickxel.Core.Assets;
using Chickxel.Core.Edit;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>모양 펜 세트 에셋(<c>shape-set</c>): 패키지 왕복·설치·개인 저장, 내용 검사(형식·PNG·개수·크기), 미리보기 격자, 최소 앱 버전.</summary>
public class ShapeSetAssetTests
{
    private const string Path0 = "assets/shapes/x.json";
    private const string AssetId = AssetSamples.PackageId + ".x";

    private static PixelImage Shape(int width, int height, Color32 color)
    {
        var image = new PixelImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image[x, y] = (x + y) % 2 == 0 ? color : new Color32(color.R, color.G, color.B, 128);
        return image;
    }

    private static StampSet Set(int count = 3, int size = 5, string name = "Ornaments")
    {
        var set = new StampSet { Name = name, Order = StampOrder.Sequential };
        for (int i = 0; i < count; i++) set.Shapes.Add(Shape(size, size + i % 2, new Color32((byte)(40 * i), 200, 90)));
        return set;
    }

    private static void AssertSameSet(StampSet expected, StampSet actual)
    {
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Order, actual.Order);
        Assert.Equal(expected.Shapes.Count, actual.Shapes.Count);
        for (int i = 0; i < expected.Shapes.Count; i++) Assert.True(expected.Shapes[i].ContentEquals(actual.Shapes[i]), $"모양 {i}");
    }

    private static string Base64Png(PixelImage image)
    {
        using var stream = new MemoryStream();
        PngIO.Save(image, stream);
        return Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>세트 파일 JSON(<paramref name="shapes"/> = base64 글 목록 JSON 조각).</summary>
    private static string Json(string shapes, string format = "chickxel-shapes", string name = "\"S\"", string order = "\"random\"", int version = 1)
        => $$"""{ "format": "{{format}}", "version": {{version}}, "set": { "name": {{name}}, "order": {{order}}, "shapes": [ {{shapes}} ] } }""";

    private static string Shapes(int count, int width = 3, int height = 3)
        => string.Join(", ", Enumerable.Range(0, count).Select(_ => $"\"{Base64Png(Shape(width, height, new Color32(255, 0, 0)))}\""));

    private static IReadOnlyList<AssetIssue> Read(string json, out ShapeSetAsset? asset)
    {
        var context = new AssetReadContext(Path0, strict: true) { AssetId = AssetId };
        asset = ShapeSetAssetFormat.Read(Encoding.UTF8.GetBytes(json), context);
        return context.Issues;
    }

    private static void AssertRejected(string json, AssetIssueCode code, string? detail = null)
    {
        var issues = Read(json, out var asset);
        Assert.Null(asset);
        Assert.True(issues.Any(i => i.Code == code && (detail is null || i.Detail == detail)),
            $"{code} {detail} 없음: " + string.Join(" | ", issues.Select(i => $"{i.Code} {i.Detail}")));
    }

    [Fact]
    public void Package_RoundTripsSet_InstallsAsShapeSet_AndRequiresApp1_14_120()
    {
        var set = Set();
        var builder = new AssetPackageBuilder(AssetSamples.Header());
        string id = builder.Add(set, segment: "ornaments");
        using var buffer = new MemoryStream();
        var manifest = builder.Write(buffer);
        byte[] bytes = buffer.ToArray();

        // 헤더가 더 낮은 버전을 적어도 이 종류가 든 패키지는 1.14.120 이상을 요구한다
        Assert.Equal(AssetKinds.ShapeSetMinAppVersion, manifest.MinAppVersion);
        Assert.Equal("assets/shapes/ornaments.json", manifest.Assets[0].Path);
        var old = Assert.Throws<AssetPackageException>(() => AssetPackageValidator.Read(new MemoryStream(bytes), new AssetValidationOptions(AppVersion: new SemVersion(1, 14, 119))));
        Assert.Contains(old.Issues, issue => issue.Code == AssetIssueCode.AppTooOld);

        var package = AssetPackageValidator.Read(new MemoryStream(bytes), new AssetValidationOptions(AppVersion: new SemVersion(1, 14, 120)));
        var read = Assert.IsType<ShapeSetAsset>(Assert.Single(package.Items).Data);
        Assert.Equal(id, read.Id);
        AssertSameSet(set, read.Set);
        // 파일 내용은 세트 파일과 같은 바이트(내보낸 .chickxel-shapes를 그대로 올릴 수 있음)
        Assert.Equal(Encoding.UTF8.GetBytes(StampLibrary.SerializeSet(set)), package.Files[manifest.Assets[0].Path]);
        Assert.NotNull(StampLibrary.ParseSet(Encoding.UTF8.GetString(package.Files[manifest.Assets[0].Path])));
        Assert.Equal(AssetPreview.Package(ShapeSetAsset.From(set))!.Png, package.Files[manifest.Assets[0].Preview!]);

        using var dir = new TempDir();
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        store.InstallFile(AssetSamples.Save(dir.Path, bytes));
        var entry = Assert.Single(store.Catalog.ShapeSets);
        AssertSameSet(set, entry.ShapeSet!);
        Assert.False(entry.Modified);

        // 다시 연 저장소도 같은 세트
        var reopened = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        AssertSameSet(set, Assert.Single(reopened.Catalog.ShapeSets).ShapeSet!);
    }

    [Fact]
    public void PersonalShapeSet_SavesInPlace_SurvivesReopen_AndRejectsInvalidSets()
    {
        using var dir = new TempDir();
        var store = AssetStore.Open(Path.Combine(dir.Path, "Assets"));
        var set = Set();
        var saved = store.SaveShapeSet(null, set);
        // 저장한 값은 사본: 원래 세트를 고쳐도 바뀌지 않는다
        set.Shapes.RemoveAt(0);
        Assert.Equal(3, saved.ShapeSet!.Shapes.Count);

        var again = store.SaveShapeSet(saved.Id, set);
        Assert.Equal(saved.Id, again.Id);
        AssertSameSet(set, Assert.Single(AssetStore.Open(Path.Combine(dir.Path, "Assets")).Catalog.ShapeSets).ShapeSet!);

        Assert.Throws<InvalidDataException>(() => store.SaveShapeSet(null, Set(count: 0)));
        Assert.Throws<InvalidDataException>(() => store.SaveShapeSet(null, Set(count: 65)));
        var big = Set(count: 1);
        big.Shapes[0] = Shape(StampLibrary.MaxShapeSize + 1, 2, new Color32(1, 2, 3));
        Assert.Throws<InvalidDataException>(() => store.SaveShapeSet(null, big));
        Assert.Single(store.Catalog.ShapeSets);
    }

    [Fact]
    public void Read_AcceptsLimitsAndOrderForms()
    {
        Assert.Empty(Read(Json(Shapes(64, StampLibrary.MaxShapeSize, 1)), out var max));
        Assert.Equal(64, max!.Set.Shapes.Count);
        Assert.Equal(AssetId, max.Set.Id);
        Assert.Empty(Read(Json(Shapes(1), order: "\"Selected\""), out var named));
        Assert.Equal(StampOrder.Selected, named!.Set.Order);
        Assert.Empty(Read(Json(Shapes(1), order: "1"), out var numbered));
        Assert.Equal(StampOrder.Sequential, numbered!.Set.Order);
    }

    [Fact]
    public void Read_RejectsWrongFormatBadPngsAndLimits()
    {
        AssertRejected(Json(Shapes(1), format: "other"), AssetIssueCode.InvalidField, "format");
        AssertRejected(Json(Shapes(1), version: 2), AssetIssueCode.FutureSchema, "2");
        AssertRejected("[1, 2]", AssetIssueCode.InvalidJson);
        AssertRejected("{ nope", AssetIssueCode.InvalidJson);
        AssertRejected(Json(Shapes(1), name: "\"  \""), AssetIssueCode.InvalidField, "set.name");
        AssertRejected(Json(Shapes(1), order: "\"zigzag\""), AssetIssueCode.InvalidField, "set.order");
        AssertRejected(Json(Shapes(1), order: "\"2\""), AssetIssueCode.InvalidField, "set.order");
        AssertRejected(Json(""), AssetIssueCode.InvalidField, "set.shapes");
        AssertRejected(Json(Shapes(65)), AssetIssueCode.TooManyItems, "set.shapes");
        AssertRejected(Json(Shapes(1, StampLibrary.MaxShapeSize + 1, 1)), AssetIssueCode.InvalidField, "set.shapes[0]");
        AssertRejected(Json(Shapes(1) + ", \"not base64!\""), AssetIssueCode.InvalidField, "set.shapes[1]");
        AssertRejected(Json($"\"{Convert.ToBase64String(Encoding.UTF8.GetBytes("plain text, not a png at all........"))}\""), AssetIssueCode.InvalidField, "set.shapes[0]");
        // PNG 머리만 맞고 내용이 깨진 것
        var png = Convert.FromBase64String(Base64Png(Shape(4, 4, new Color32(9, 9, 9))));
        Array.Clear(png, 33, png.Length - 33);
        AssertRejected(Json($"\"{Convert.ToBase64String(png)}\""), AssetIssueCode.InvalidField, "set.shapes[0]");
        // 엄격 모드는 모르는 키도 거부
        var extra = JsonNode.Parse(Json(Shapes(1)))!.AsObject();
        extra["set"]!.AsObject()["id"] = "x";
        AssertRejected(extra.ToJsonString(), AssetIssueCode.InvalidField, "set.id");
    }

    [Fact]
    public void Package_WithBadShapeFile_IsRejectedBeforeInstall()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [Path0] = Encoding.UTF8.GetBytes(Json(Shapes(65))) };
        using var stream = new MemoryStream();
        AssetPackageWriter.Write(stream, AssetSamples.Header() with { Assets = [new AssetManifestEntry(AssetId, AssetKinds.ShapeSet, Path0, null)] }, files);
        var ex = Assert.Throws<AssetPackageException>(() => AssetPackageValidator.Read(new MemoryStream(stream.ToArray())));
        Assert.Contains(ex.Issues, issue => issue.Code == AssetIssueCode.TooManyItems && issue.Path == Path0);
    }

    [Fact]
    public void PreviewGrid_PlacesShapesInSquareCells_AndFitsTheLimit()
    {
        var a = Shape(2, 2, new Color32(255, 0, 0));
        var b = Shape(4, 1, new Color32(0, 255, 0));
        var c = Shape(1, 1, new Color32(0, 0, 255));
        // 칸 4, 틈 1, 열 2 → 9×9 격자를 256 안 가장 큰 정수 배(28)로
        var grid = AssetPreview.RenderShapeSet([a, b, c]);
        Assert.Equal((252, 252), (grid.Width, grid.Height));
        Assert.Equal(a[0, 0], grid[1 * 28, 1 * 28]);   // a는 칸(0,0) 가운데 (1,1)
        Assert.Equal(b[0, 0], grid[5 * 28, 1 * 28]);   // b는 칸(1,0) x=5, 세로 가운데 y=1
        Assert.Equal(c[0, 0], grid[1 * 28, 6 * 28]);   // c는 칸(0,1) 가운데 (1,6)
        Assert.Equal(0, grid[4 * 28, 0].A);            // 틈은 투명

        // 큰 세트는 긴 변이 한도 안으로 줄어든다(패키지 미리보기 한도 안)
        var large = AssetPreview.RenderShapeSet(Enumerable.Range(0, 64).Select(_ => Shape(StampLibrary.MaxShapeSize, 40, new Color32(1, 1, 1))).ToList());
        Assert.True(Math.Max(large.Width, large.Height) <= AssetPreview.ShapeSetPreviewSize);
        Assert.True(AssetPreview.ShapeSetPreviewSize <= AssetPackageLimits.Default.MaxPreviewSize);
    }
}
