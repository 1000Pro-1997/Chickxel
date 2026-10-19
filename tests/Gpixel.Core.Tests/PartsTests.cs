using System.Drawing;
using System.IO.Compression;
using System.Text;
using Gpixel.Core.Edit;
using Gpixel.Core.IO;
using Gpixel.Core.Model;
using Gpixel.Core.Slicing;

namespace Gpixel.Core.Tests;

public class PartsTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);
    private static readonly Color32 Green = new(0, 255, 0);

    private static readonly string[] PlayerNames = ["Head", "Body", "Arm", "lag"];
    private static readonly Rectangle[] PlayerRects =
        [new(2, 2, 16, 10), new(4, 14, 12, 16), new(22, 4, 6, 14), new(22, 20, 6, 6)];
    private static readonly Pivot[] PlayerPivots =
        [new(0.5f, 0f), new(0.5f, 0f), new(0.5f, 0.9285714f), new(0.5f, 1f)];

    private static void Paint(SpriteProject p, int layer, int frame, int x, int y, Color32 c)
    {
        p.BeginCelStroke(layer, frame).Set(x, y, c);
        p.CommitCelStroke("점");
    }

    private static string CopyPlayerFixture(TempDir dir)
    {
        var png = dir.File("player@2x.png");
        File.Copy(TestUtil.Fixture("player@2x.png"), png);
        File.Copy(TestUtil.Fixture("player@2x.png.meta"), png + ".meta");
        return png;
    }

    private static List<string> Names(SpriteProject p) => p.Layers.Select(l => l.Name).ToList();

    /// <summary>그룹의 부위는 그룹 바로 아래에 이어 붙어 있고, 부위는 항상 그룹이 있다.</summary>
    private static void AssertHierarchy(SpriteProject p)
    {
        var layers = p.Layers;
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i];
            if (l.IsPart)
            {
                Assert.NotNull(l.Parent);
                Assert.True(l.Parent!.IsGroup);
                int g = p.IndexOf(l.Parent);
                Assert.True(g > i, $"{l.Name}의 그룹이 위에 있어야 합니다.");
                for (int j = i; j < g; j++) Assert.Same(l.Parent, layers[j].Parent);
            }
            else Assert.Null(l.Parent);
            if (l.IsGroup)
            {
                int n = layers.Count(x => ReferenceEquals(x.Parent, l));
                Assert.Equal(n, p.ChildCount(l));
                Assert.Equal(layers.Skip(i - n).Take(n), p.ChildrenOf(l));
            }
        }
    }

    // ───────────── 셀·합성 ─────────────

    [Fact]
    public void Parts_HaveOwnCelSize_FlattenUsesOffsetOpacityVisibility()
    {
        var p = new SpriteProject(8, 6);
        var g = p.AddGroup("캐릭터");
        var body = p.AddPart(g, "Body", 4, 3);                    // 가운데 (2,1)
        var head = p.AddPart(g, "Head", 2, 2, new Point(-1, 5));  // 일부는 캔버스 밖
        Assert.Equal(["레이어 1", "Body", "Head", "캐릭터"], Names(p));
        Assert.Equal([body, head], p.ChildrenOf(g));
        Assert.Equal(new Point(2, 1), body.PartOffset);
        Assert.Equal(new Rectangle(-1, 5, 2, 2), head.PartRect);
        Assert.Throws<ArgumentException>(() => p.AddPart(body, "x", 1, 1));
        Assert.Throws<ArgumentException>(() => p.AddPart(g, "x", 0, 1));
        Assert.Equal("Head 2", p.AddPart(g, "Head", 1, 1).Name);
        p.Undo();

        var cel = p.BeginCelStroke(1, 0);
        Assert.Equal(new Size(4, 3), cel.Size);
        cel.Clear(Red);
        p.CommitCelStroke("칠");
        Paint(p, 2, 0, 1, 0, Blue);  // 캔버스 (0,5)
        Paint(p, 2, 0, 0, 0, Blue);  // 캔버스 (-1,5) → 밖
        Paint(p, 0, 0, 7, 0, Green);
        Assert.Equal(new Size(2, 2), p.GetCel(2, 0)!.Size);
        Assert.Null(p.GetCel(3, 0));
        Assert.Throws<InvalidOperationException>(() => p.BeginCelStroke(3, 0));
        Assert.Throws<InvalidOperationException>(() => p.SetCel(3, 0, new PixelImage(8, 6)));
        Assert.Throws<ArgumentException>(() => p.SetCel(1, 0, new PixelImage(8, 6)));

        var flat = p.Flatten(0);
        Assert.Equal(Red, flat.Get(2, 1));
        Assert.Equal(Red, flat.Get(5, 3));
        Assert.Equal(Color32.Transparent, flat.Get(6, 3));
        Assert.Equal(Blue, flat.Get(0, 5));
        Assert.Equal(Green, flat.Get(7, 0));

        var group = p.FlattenGroup(g, 0);
        Assert.Equal(Red, group.Get(2, 1));
        Assert.Equal(Color32.Transparent, group.Get(7, 0));

        var only = new PixelImage(8, 6);
        p.FlattenInto(only, 0, layerFilter: l => l == head);
        Assert.Equal(Blue, only.Get(0, 5));
        Assert.Equal(Color32.Transparent, only.Get(2, 1));

        // 불투명도 = 그룹 × 부위
        p.SetLayerProperties(3, opacity: 128);
        p.SetLayerProperties(1, opacity: 128);
        Assert.Equal(64, p.Flatten(0).Get(2, 1).A);

        // 숨긴 그룹은 부위도 숨긴다
        p.SetLayerProperties(3, visible: false);
        Assert.False(p.IsLayerVisible(1));
        Assert.True(p.IsLayerVisible(0));
        Assert.Equal(Color32.Transparent, p.Flatten(0).Get(2, 1));
        Assert.Equal(64, p.Flatten(0, includeHidden: true).Get(2, 1).A);
        Assert.Equal(Red.WithAlpha(64), p.FlattenGroup(g, 0).Get(2, 1)); // 썸네일은 그룹 보이기 무시
        p.SetLayerProperties(3, visible: true);
        p.SetLayerProperties(2, visible: false);
        Assert.Equal(Color32.Transparent, p.Flatten(0).Get(0, 5));
        Assert.Equal(Color32.Transparent, p.FlattenGroup(g, 0).Get(0, 5));

        // 잠긴 그룹의 부위에는 그릴 수 없다
        p.SetLayerProperties(3, locked: true);
        Assert.True(p.IsLayerLocked(1));
        Assert.False(p.IsLayerLocked(0));
        Assert.Throws<InvalidOperationException>(() => p.BeginCelStroke(1, 0));

        // 프레임: 새 프레임은 비고, 복제는 부위 셀까지
        p.AddFrame();
        Assert.Null(p.GetCel(1, 1));
        p.DuplicateFrame(0);
        Assert.True(p.GetCel(1, 1)!.ContentEquals(p.GetCel(1, 0)!));
        Assert.NotSame(p.GetCel(1, 1), p.GetCel(1, 0));
    }

    // ───────────── 구조 ─────────────

    [Fact]
    public void Hierarchy_StaysValid_UnderAddDuplicateMoveMergeDelete()
    {
        var p = new SpriteProject(4, 4);
        var g = p.AddGroup("G");
        var a = p.AddPart(g, "A", 2, 2);
        p.AddPart(g, "B", 1, 1);
        Paint(p, 1, 0, 1, 1, Red);
        var top = p.AddLayer(2, "위"); // 그룹 안 → 그룹 바로 위로
        Assert.Equal(["레이어 1", "A", "B", "G", "위"], Names(p));
        AssertHierarchy(p);

        var dupG = p.DuplicateLayer(3);
        Assert.Equal(["레이어 1", "A", "B", "G", "A 복사", "B 복사", "G 복사", "위"], Names(p));
        Assert.True(dupG.IsGroup);
        Assert.Equal(2, p.ChildCount(dupG));
        Assert.Equal(Red, p.GetCel(4, 0)!.Get(1, 1));
        Assert.NotSame(p.GetCel(1, 0), p.GetCel(4, 0));
        Assert.Equal(a.PartSize, p.Layers[4].PartSize);
        AssertHierarchy(p);

        var dupB = p.DuplicateLayer(2);
        Assert.Same(g, dupB.Parent);
        Assert.Equal(["레이어 1", "A", "B", "B 복사 2", "G", "A 복사", "B 복사", "G 복사", "위"], Names(p));
        AssertHierarchy(p);

        // 부위는 자기 그룹 안에서만
        p.MoveLayer(1, 99);
        Assert.Equal(["레이어 1", "B", "B 복사 2", "A", "G"], Names(p).Take(5));
        p.MoveLayer(3, 0);
        Assert.Equal(["레이어 1", "A", "B", "B 복사 2", "G"], Names(p).Take(5));
        AssertHierarchy(p);

        // 그룹은 덩어리째
        p.MoveLayer(7, 0);
        Assert.Equal(["A 복사", "B 복사", "G 복사", "레이어 1", "A", "B", "B 복사 2", "G", "위"], Names(p));
        // 그룹 안으로 넣으면 가까운 경계로
        p.MoveLayer(8, 5);
        Assert.Equal(["A 복사", "B 복사", "G 복사", "레이어 1", "위", "A", "B", "B 복사 2", "G"], Names(p));
        AssertHierarchy(p);

        // ▲▼
        p.MoveLayerUp(4);
        Assert.Equal(["A 복사", "B 복사", "G 복사", "레이어 1", "A", "B", "B 복사 2", "G", "위"], Names(p));
        p.MoveLayerDown(7);
        Assert.Equal(["A 복사", "B 복사", "G 복사", "A", "B", "B 복사 2", "G", "레이어 1", "위"], Names(p));
        p.MoveLayerDown(6);
        Assert.Equal(["A", "B", "B 복사 2", "G", "A 복사", "B 복사", "G 복사", "레이어 1", "위"], Names(p));
        int undo = p.History.UndoCount;
        p.MoveLayerDown(3);   // 맨 아래 덩어리
        p.MoveLayerUp(8);     // 맨 위
        p.MoveLayerUp(2);     // 맨 위 부위
        p.MoveLayerDown(0);   // 맨 아래 부위
        Assert.Equal(undo, p.History.UndoCount);
        p.MoveLayerDown(2);
        Assert.Equal(["A", "B 복사 2", "B", "G"], Names(p).Take(4));
        p.MoveLayerUp(1);
        Assert.Equal(["A", "B", "B 복사 2", "G"], Names(p).Take(4));
        AssertHierarchy(p);

        // 병합은 일반 → 일반만
        Assert.Throws<InvalidOperationException>(() => p.MergeLayerDown(2));
        Assert.Throws<InvalidOperationException>(() => p.MergeLayerDown(3));
        Assert.Throws<InvalidOperationException>(() => p.MergeLayerDown(7)); // 아래가 그룹
        Paint(p, 8, 0, 0, 0, Blue);
        p.MergeLayerDown(8);
        Assert.Equal(Blue, p.GetCel(7, 0)!.Get(0, 0));
        Assert.DoesNotContain(top, p.Layers);

        // 그룹 삭제는 부위까지
        p.DeleteLayer(3);
        Assert.Equal(["A 복사", "B 복사", "G 복사", "레이어 1"], Names(p));
        AssertHierarchy(p);
        p.Undo();
        Assert.Equal(Red, p.GetCel(0, 0)!.Get(1, 1));
        AssertHierarchy(p);

        // 남는 레이어가 없으면 거부
        var q = new SpriteProject(2, 2);
        var qg = q.AddGroup();
        q.AddPart(qg, "p", 1, 1);
        q.DeleteLayer(0);
        Assert.Equal(2, q.Layers.Count);
        var ex = Assert.Throws<InvalidOperationException>(() => q.DeleteLayer(1));
        Assert.Equal("마지막 레이어는 지울 수 없습니다.", ex.Message);
        q.DeleteLayer(0); // 부위만 지우면 빈 그룹이 남는다
        Assert.True(Assert.Single(q.Layers).IsGroup);
    }

    // ───────────── 크기·캔버스 ─────────────

    [Fact]
    public void ResizePart_KeepsPixelsOnCanvas_AndPivotOnSamePixel()
    {
        var p = new SpriteProject(10, 10);
        var g = p.AddGroup();
        var part = p.AddPart(g, "팔", 4, 4, new Point(3, 3));
        int pi = p.IndexOf(part);
        Paint(p, pi, 0, 0, 3, Red);   // 캔버스 (3,6)
        p.AddFrame();
        Paint(p, pi, 1, 3, 0, Blue);  // 캔버스 (6,3)
        p.SetPartPivot(pi, Pivot.BottomCenter); // 픽셀 (2,4)

        p.ResizePart(pi, 6, 5, Anchor.TopRight); // 그림이 (2,0)만큼 옮겨짐
        Assert.Equal(new Size(6, 5), part.PartSize);
        Assert.Equal(new Point(1, 3), part.PartOffset);
        Assert.Equal(Red, p.GetCel(pi, 0)!.Get(2, 3));
        Assert.Equal(Blue, p.GetCel(pi, 1)!.Get(5, 0));
        Assert.Equal(Red, p.Flatten(0).Get(3, 6));
        Assert.Equal(Blue, p.Flatten(1).Get(6, 3));
        Assert.Equal((4f, 4f), part.PartPivot.ToLocalPixel(6, 5));
        Assert.Throws<ArgumentException>(() => p.ResizePart(0, 2, 2, Anchor.Center)); // 일반 레이어

        p.Undo();
        Assert.Equal((new Size(4, 4), new Point(3, 3), Pivot.BottomCenter), (part.PartSize, part.PartOffset, part.PartPivot));
        Assert.Equal(Red, p.GetCel(pi, 0)!.Get(0, 3));
    }

    [Fact]
    public void ResizeCanvas_ShiftsPartOffsets_AndTransformsMatchCompositeTransform()
    {
        var p = new SpriteProject(10, 8);
        Paint(p, 0, 0, 0, 0, Green);
        var g = p.AddGroup();
        var part = p.AddPart(g, "몸", 4, 3, new Point(5, 2));
        int pi = p.IndexOf(part);
        var cel = p.BeginCelStroke(pi, 0);
        cel.Set(0, 0, Red);
        cel.Set(3, 2, Blue);
        p.CommitCelStroke("점");
        p.SetPartBorder(pi, new SpriteBorder(1, 2, 3, 4));
        p.SetPartPivot(pi, new Pivot(0.25f, 0f));

        p.ResizeCanvas(14, 12, Anchor.BottomRight); // (4,4)만큼
        Assert.Equal(new Point(9, 6), part.PartOffset);
        Assert.Equal(new Size(4, 3), p.GetCel(pi, 0)!.Size);
        Assert.Equal(new Size(14, 12), p.GetCel(0, 0)!.Size);
        Assert.Equal(Red, p.Flatten(0).Get(9, 6));
        Assert.Equal(Green, p.Flatten(0).Get(4, 4));

        var transforms = new (Action Apply, Func<PixelImage, PixelImage> Image)[]
        {
            (p.FlipCanvasHorizontal, ImageTransform.FlipHorizontal),
            (p.FlipCanvasVertical, ImageTransform.FlipVertical),
            (() => p.RotateCanvas90(), img => ImageTransform.Rotate90(img)),
            (() => p.RotateCanvas90(clockwise: false), img => ImageTransform.Rotate90(img, false)),
        };
        foreach (var (apply, image) in transforms)
        {
            var before = p.Flatten(0);
            apply();
            Assert.True(p.Flatten(0).ContentEquals(image(before)));
        }

        // 시계 방향 회전: 크기가 바뀌고 피벗·테두리도 회전
        var size = part.PartSize;
        var (border, pivot) = (part.PartBorder, part.PartPivot);
        p.RotateCanvas90();
        Assert.Equal(new Size(size.Height, size.Width), part.PartSize);
        Assert.Equal(new SpriteBorder(border.Bottom, border.Left, border.Top, border.Right), part.PartBorder);
        Assert.Equal(new Pivot(pivot.Y, 1f - pivot.X), part.PartPivot);
        p.Undo();
        Assert.Equal(size, part.PartSize);
    }

    // ───────────── 실행취소 ─────────────

    [Fact]
    public void PartOps_UndoRedo_AndCollapseIsNotUndoable()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup("G");
        var part = p.AddPart(g, "P", 3, 2);
        p.SetPartOffset(1, new Point(-2, 7));
        p.SetPartOffset(1, new Point(-2, 7)); // 같으면 기록 없음
        p.SetPartPivot(1, Pivot.BottomCenter);
        p.SetPartBorder(1, new SpriteBorder(1, 0, 1, 0));
        p.ResizePart(1, 5, 2, Anchor.MiddleLeft);
        Assert.Equal(6, p.History.UndoCount);

        p.IsDirty = false;
        p.SetLayerCollapsed(2, true);
        Assert.True(g.Collapsed);
        Assert.False(p.IsDirty);
        Assert.Equal(6, p.History.UndoCount);
        Assert.Throws<ArgumentException>(() => p.SetLayerCollapsed(1, true));
        Assert.Throws<ArgumentException>(() => p.SetPartOffset(2, Point.Empty));

        for (int i = 0; i < 4; i++) p.Undo();
        Assert.Equal((new Size(3, 2), new Point(2, 3), Pivot.Center, SpriteBorder.None),
            (part.PartSize, part.PartOffset, part.PartPivot, part.PartBorder));
        p.Undo();
        Assert.DoesNotContain(part, p.Layers);
        p.Undo();
        Assert.Equal(["레이어 1"], Names(p));
        Assert.True(g.Collapsed); // 접힘은 기록 밖

        while (p.Redo()) { }
        Assert.Equal(["레이어 1", "P", "G"], Names(p));
        Assert.Same(g, part.Parent);
        Assert.True(part.IsPart);
        Assert.Equal((new Size(5, 2), new Point(-2, 7), new SpriteBorder(1, 0, 1, 0)),
            (part.PartSize, part.PartOffset, part.PartBorder));
        AssertHierarchy(p);
    }

    // ───────────── 파일 ─────────────

    [Fact]
    public void ProjectIO_V2RoundTrip_KeepsGroupsPartsAndPartSizedCels()
    {
        using var dir = new TempDir();
        var p = new SpriteProject(12, 10);
        Paint(p, 0, 0, 11, 9, Green);
        var g = p.AddGroup("캐릭터");
        p.AddPart(g, "Body", 4, 6, new Point(4, 4));
        p.AddPart(g, "Head", 8, 5, new Point(-1, -2));
        p.AddLayer(name: "위");
        p.AddFrame();
        Paint(p, 1, 0, 3, 5, Red);
        Paint(p, 2, 1, 7, 4, Blue);
        p.SetPartPivot(2, new Pivot(0.25f, 0.75f));
        p.SetPartBorder(1, new SpriteBorder(1, 2, 3, 4));
        p.SetLayerProperties(3, opacity: 200, locked: true);
        p.SetLayerCollapsed(3, true);
        p.Edit("내보내기", ProjectChange.Settings, () =>
        {
            p.Export.ExportFrames = false;
            p.Export.ExportParts = true;
            p.Export.PartsFileName = "hero_parts";
        });
        var path = dir.File("hero.gpixel");
        ProjectIO.Save(p, path);

        using (var zip = ZipFile.OpenRead(path))
        {
            Assert.Equal(["cels/L0_F0.png", "cels/L1_F0.png", "cels/L2_F1.png"],
                zip.Entries.Select(e => e.FullName).Where(n => n.StartsWith("cels/")).OrderBy(n => n));
            using var es = zip.GetEntry("cels/L2_F1.png")!.Open();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            ms.Position = 0;
            Assert.Equal(new Size(8, 5), PngIO.Load(ms).Size);
            using var json = new StreamReader(zip.GetEntry(ProjectIO.ProjectEntryName)!.Open());
            Assert.Contains("\"version\": 2", json.ReadToEnd());
        }

        var warnings = new List<string>();
        var back = ProjectIO.Load(path, warnings);
        Assert.Empty(warnings);
        Assert.Equal(Names(p), Names(back));
        Assert.Equal(p.Layers.Select(l => (l.Kind, l.Parent?.Name, l.Visible, l.Locked, l.Opacity, l.PartSize, l.PartOffset, l.PartPivot, l.PartBorder, l.PartSourceRect)),
            back.Layers.Select(l => (l.Kind, l.Parent?.Name, l.Visible, l.Locked, l.Opacity, l.PartSize, l.PartOffset, l.PartPivot, l.PartBorder, l.PartSourceRect)));
        Assert.True(back.Layers[3].Collapsed);
        AssertHierarchy(back);
        for (int f = 0; f < 2; f++)
        {
            Assert.True(p.Flatten(f).ContentEquals(back.Flatten(f)));
            for (int l = 0; l < p.Layers.Count; l++)
                Assert.Equal(p.GetCel(l, f)?.Size, back.GetCel(l, f)?.Size);
        }
        Assert.Equal((false, true, "hero_parts"), (back.Export.ExportFrames, back.Export.ExportParts, back.Export.PartsFileName));
        Assert.Same(back.Layers[3], back.Layers[1].Parent);
    }

    [Fact]
    public void ProjectIO_LoadsVersion1AnimationFile()
    {
        using var dir = new TempDir();
        var path = dir.File("old.gpixel");
        var cel = new PixelImage(4, 3);
        cel.Set(1, 2, Red);
        WriteZip(path, """
            {
              "version": 1,
              "kind": "Animation",
              "width": 4,
              "height": 3,
              "layers": [ { "name": "바닥", "visible": true, "locked": false, "opacity": 255 }, { "name": "위", "opacity": 100 } ],
              "frames": [ { "durationMs": 80 }, { "durationMs": 120, "name": "hit" } ],
              "tags": [ { "name": "run", "from": 0, "to": 1, "direction": "PingPong", "loop": true } ],
              "export": { "onSave": true, "fileName": "hero", "columns": 2 }
            }
            """, new() { ["cels/L1_F1.png"] = cel });

        var p = ProjectIO.Load(path);
        Assert.Equal(["바닥", "위"], Names(p));
        Assert.All(p.Layers, l => Assert.Equal(LayerKind.Normal, l.Kind));
        Assert.Equal((80, 120, "hit"), (p.Frames[0].DurationMs, p.Frames[1].DurationMs, p.Frames[1].Name));
        Assert.Equal(TagDirection.PingPong, Assert.Single(p.Tags).Direction);
        Assert.Equal(Red, p.GetCel(1, 1)!.Get(1, 2));
        Assert.Equal(("hero", 2, true, true, (string?)null),
            (p.Export.FileName, p.Export.Columns, p.Export.ExportFrames, p.Export.ExportParts, p.Export.PartsFileName));
    }

    /// <summary>예전(버전 1) 아틀라스 파일: 플레이어 고정 파일의 칸을 스프라이트로, 셀은 원본 시트에서 잘라 넣는다.</summary>
    private static void WritePlayerV1Atlas(string path, PixelImage sheet)
    {
        var sprites = string.Join(",\n", PlayerNames.Select((n, i) =>
        {
            var r = PlayerRects[i];
            var pv = PlayerPivots[i];
            return $$"""{ "name": "{{n}}", "width": {{r.Width}}, "height": {{r.Height}}, "pivot": { "x": {{pv.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "y": {{pv.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}} }, "source": [{{r.X}}, {{r.Y}}, {{r.Width}}, {{r.Height}}] }""";
        }));
        var cels = PlayerRects.Select((r, i) => (i, r)).ToDictionary(x => $"atlas/S{x.i}_L0.png", x => sheet.Crop(x.r));
        WriteZip(path, $$"""
            {
              "version": 1,
              "kind": "Atlas",
              "width": 16,
              "height": 16,
              "atlasSourceWidth": 32,
              "atlasSourceHeight": 32,
              "sprites": [ {{sprites}} ],
              "layers": [ { "name": "레이어 1", "visible": true, "locked": false, "opacity": 255 } ],
              "frames": [ { "durationMs": 100 } ],
              "tags": [],
              "unity": { "spriteMode": "Multiple", "pixelsPerUnit": 16, "defaultPivot": { "x": 0.5, "y": 0.5 }, "fullRect": true, "readable": false },
              "export": { "onSave": true, "layout": "Columns", "columns": 0, "spacing": 0, "trim": false, "singleFrame": 0,
                          "atlasMaxWidth": 0, "atlasPowerOfTwo": false, "atlasOrder": "KeepOrder", "keepImportedRects": true }
            }
            """, cels);
    }

    private static void WriteZip(string path, string json, Dictionary<string, PixelImage> cels)
    {
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        using (var w = new StreamWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open(), new UTF8Encoding(false)))
            w.Write(json);
        foreach (var (name, img) in cels)
        {
            using var es = zip.CreateEntry(name).Open();
            PngIO.Save(img, es);
        }
    }

    [Fact]
    public void ProjectIO_Version1Atlas_BecomesParts_AndExportKeepsUnityIds()
    {
        using var dir = new TempDir();
        var png = CopyPlayerFixture(dir);
        var sheet = PngIO.Load(png);
        var original = UnityMeta.Read(png + ".meta");
        var path = dir.File("player@2x.gpixel");
        WritePlayerV1Atlas(path, sheet);

        var warnings = new List<string>();
        var p = ProjectIO.Load(path, warnings);
        Assert.Empty(warnings);
        Assert.Equal(new Size(32, 32), p.Size);
        Assert.Equal(["lag", "Arm", "Body", "Head", "부위"], Names(p)); // 첫 스프라이트 = 맨 위 부위
        var group = p.Layers[4];
        Assert.True(group.IsGroup);
        AssertHierarchy(p);
        Assert.Single(p.Frames);
        for (int i = 0; i < 4; i++)
        {
            var part = p.Layers[3 - i];
            Assert.Equal((PlayerNames[i], PlayerRects[i].Size, PlayerRects[i].Location, PlayerPivots[i], (Rectangle?)PlayerRects[i]),
                (part.Name, part.PartSize, part.PartOffset, part.PartPivot, part.PartSourceRect));
            Assert.True(p.GetCel(3 - i, 0)!.ContentEquals(sheet.Crop(PlayerRects[i])));
        }
        Assert.True(p.Flatten(0).ContentEquals(sheet));
        Assert.Equal((true, "player@2x", true, "player@2x_frames"),
            (p.Export.ExportParts, p.Export.PartsFileName, p.Export.ExportFrames, p.Export.FileName));

        // 저장(v2로 바뀜) + 내보내기: 부위 아틀라스가 예전 아틀라스 자리 → guid·spriteID·칸 그대로
        var saved = ProjectIO.SaveWithExport(p);
        Assert.Equal(png, saved.Export!.Parts!.ImagePath);
        Assert.Equal(dir.File("player@2x_frames.png"), saved.Export.Frames!.ImagePath);
        var meta = UnityMeta.Read(png + ".meta");
        Assert.Equal(original.Guid, meta.Guid);
        Assert.Equal(original.Sprites.Select(s => (s.Name, s.UnityRect, s.Alignment, s.Pivot, s.Border, s.SpriteId, s.InternalId)),
            meta.Sprites.Select(s => (s.Name, s.UnityRect, s.Alignment, s.Pivot, s.Border, s.SpriteId, s.InternalId)));
        Assert.True(PngIO.Load(png).ContentEquals(sheet));
        Assert.True(ProjectIO.Load(path).Layers[4].IsGroup);
    }

    [Fact]
    public void ProjectIO_Version1Atlas_WithoutSourceRects_LaysPartsOutAndMergesVisibleLayers()
    {
        using var dir = new TempDir();
        var path = dir.File("parts.gpixel");
        var a0 = new PixelImage(3, 2);
        a0.Set(0, 0, Red);
        var a1 = new PixelImage(3, 2);
        a1.Set(2, 1, Blue);
        var hidden = new PixelImage(5, 4);
        hidden.Set(0, 0, Green);
        WriteZip(path, """
            {
              "version": 1,
              "kind": "Atlas",
              "width": 8,
              "height": 8,
              "sprites": [ { "name": "a", "width": 3, "height": 2, "border": [1, 0, 1, 0] }, { "name": "bad", "width": 0, "height": 2 }, { "name": "b", "width": 5, "height": 4 } ],
              "layers": [ { "name": "레이어 1" }, { "name": "레이어 2" }, { "name": "숨김", "visible": false } ],
              "unity": { "defaultPivot": { "x": 0, "y": 1 } },
              "export": { "onSave": true, "fileName": "custom" }
            }
            """, new() { ["atlas/S0_L0.png"] = a0, ["atlas/S0_L1.png"] = a1, ["atlas/S2_L2.png"] = hidden });

        var warnings = new List<string>();
        var p = ProjectIO.Load(path, warnings);
        Assert.Contains(warnings, w => w.Contains("합쳤습니다"));
        Assert.Equal(new Size(3 + 1 + 5, 4), p.Size);
        Assert.Equal(["b", "a", "부위"], Names(p));
        var a = p.Layers[1];
        var b = p.Layers[0];
        Assert.Equal((new Point(0, 0), new Point(4, 0)), (a.PartOffset, b.PartOffset));
        Assert.Equal(new Pivot(0, 1), a.PartPivot); // 피벗이 없으면 기본 피벗
        Assert.Equal(new SpriteBorder(1, 0, 1, 0), a.PartBorder);
        Assert.Null(a.PartSourceRect);
        var merged = p.GetCel(1, 0)!;
        Assert.Equal((Red, Blue), (merged.Get(0, 0), merged.Get(2, 1)));
        Assert.Null(p.GetCel(0, 0)); // 숨긴 레이어만 있던 스프라이트는 빈 셀
        Assert.Equal(("custom", "custom_frames"), (p.Export.PartsFileName, p.Export.FileName));
    }

    // ───────────── 내보내기 ─────────────

    [Fact]
    public void Export_WritesFrameSheetAndPartsAtlas_ReexportKeepsIds()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "Assets"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "ProjectSettings"));
        var path = Path.Combine(dir.Path, "Assets", "hero.gpixel");
        var p = new SpriteProject(16, 16);
        var g = p.AddGroup("캐릭터");
        p.AddPart(g, "Body", 6, 8, new Point(5, 8));
        p.AddPart(g, "Head", 8, 5, new Point(4, 3));
        p.AddFrame();
        p.BeginCelStroke(1, 0).Clear(Red);
        p.CommitCelStroke("몸");
        p.BeginCelStroke(2, 0).Clear(Blue);
        p.CommitCelStroke("머리");
        Paint(p, 2, 1, 7, 4, Blue);
        p.SetPartPivot(2, Pivot.BottomCenter);
        p.SetPartBorder(1, new SpriteBorder(1, 1, 1, 1));

        var saved = ProjectIO.SaveWithExport(p, path);
        var frames = saved.Export!.Frames!;
        var parts = saved.Export.Parts!;
        Assert.Equal(Path.Combine(dir.Path, "Assets", "hero.png"), frames.ImagePath);
        Assert.Equal(Path.Combine(dir.Path, "Assets", "hero_parts.png"), parts.ImagePath);
        Assert.Equal(parts.ImagePath, ProjectExport.GetPartsImagePath(p));
        Assert.True(frames.MetaWritten && parts.MetaWritten);
        Assert.Equal(2, frames.SpriteCount);
        Assert.Equal(3, parts.SpriteCount);
        Assert.False(File.Exists(SidecarIO.GetPath(parts.ImagePath)));

        var meta1 = UnityMeta.Read(parts.ImagePath + ".meta");
        Assert.Equal(["Head", "Head_1", "Body"], meta1.Sprites.Select(s => s.Name));
        Assert.Equal(new SpriteBorder(1, 1, 1, 1), meta1.Sprites[2].Border);
        Assert.Equal(new Pivot(0.5f, 0f), meta1.Sprites[0].Pivot);
        var partsDoc = DocumentIO.Load(parts.ImagePath).Document;
        Assert.Empty(partsDoc.Animations);
        var head1 = partsDoc.FindSlice("Head_1")!;
        Assert.Equal(new Size(8, 5), head1.Rect.Size);
        Assert.Equal(Blue, partsDoc.Image.Get(head1.Rect.X + 7, head1.Rect.Y + 4));
        var frameSheet = PngIO.Load(frames.ImagePath);
        Assert.True(frameSheet.Crop(new Rectangle(0, 0, 16, 16)).ContentEquals(p.Flatten(0)));

        var preview = ProjectExport.BuildPartsSheet(p, "hero_parts");
        Assert.Equal(["Head", "Head_1", "Body"], preview.Entries.Select(e => e.Name));
        Assert.Same(p.Layers[2], preview.Entries[0].Part);

        // 프레임을 늘리고 다시 내보내면 같은 이름은 같은 guid·ID
        p.DuplicateFrame(1);
        ProjectIO.SaveWithExport(p);
        var meta2 = UnityMeta.Read(parts.ImagePath + ".meta");
        Assert.Equal(meta1.Guid, meta2.Guid);
        Assert.Equal(["Head", "Head_1", "Head_2", "Body"], meta2.Sprites.Select(s => s.Name));
        foreach (var s in meta1.Sprites)
        {
            var again = meta2.Sprites.Single(x => x.Name == s.Name);
            Assert.Equal((s.SpriteId, s.InternalId), (again.SpriteId, again.InternalId));
        }

        // 하나씩 끄기
        p.Edit("끄기", ProjectChange.Settings, () => p.Export.ExportFrames = false);
        var onlyParts = ProjectExport.Export(p);
        Assert.Null(onlyParts.Frames);
        Assert.NotNull(onlyParts.Parts);
        p.Edit("끄기", ProjectChange.Settings, () => { p.Export.ExportFrames = true; p.Export.ExportParts = false; });
        var onlyFrames = ProjectExport.Export(p);
        Assert.NotNull(onlyFrames.Frames);
        Assert.Null(onlyFrames.Parts);

        // 이름이 같으면 거부
        p.Edit("이름", ProjectChange.Settings, () => { p.Export.ExportParts = true; p.Export.PartsFileName = "hero"; });
        Assert.Throws<InvalidOperationException>(() => ProjectExport.Export(p));

        // 부위가 없으면 부위 아틀라스 없음
        var plain = new SpriteProject(4, 4);
        var plainResult = ProjectIO.SaveWithExport(plain, dir.File("plain.gpixel")).Export!;
        Assert.NotNull(plainResult.Frames);
        Assert.Null(plainResult.Parts);
        Assert.Empty(ProjectExport.BuildPartsSheet(plain, "plain_parts").Entries);
    }

    [Fact]
    public void PartsAtlas_AddingFrameKeepsFirstFrameSpriteId()
    {
        using var dir = new TempDir();
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup("캐릭터");
        p.AddPart(g, "Head", 4, 3, new Point(2, 1));
        p.BeginCelStroke(1, 0).Clear(Red);
        p.CommitCelStroke("머리");
        p.Edit("메타", ProjectChange.Settings, () => p.Export.WriteMeta = true);
        var parts = ProjectIO.SaveWithExport(p, dir.File("hero.gpixel")).Export!.Parts!;
        var before = UnityMeta.Read(parts.ImagePath + ".meta").Sprites.Single();
        Assert.Equal("Head", before.Name);

        p.DuplicateFrame(0);
        ProjectIO.SaveWithExport(p);
        var after = UnityMeta.Read(parts.ImagePath + ".meta").Sprites;
        Assert.Equal(["Head", "Head_1"], after.Select(s => s.Name));
        Assert.Equal(before.SpriteId, after[0].SpriteId);
    }

    [Fact]
    public void Export_ImportedParts_ReproducesFixtureMeta_AndResizedPartMoves()
    {
        using var dir = new TempDir();
        var png = CopyPlayerFixture(dir);
        var original = UnityMeta.Read(png + ".meta");
        var originalImage = PngIO.Load(png);
        var p = ProjectImport.Open(png).Project;

        var saved = ProjectIO.SaveWithExport(p);
        Assert.Equal(png, saved.Export!.Parts!.ImagePath);
        Assert.True(saved.Export.Parts.MetaWritten);
        Assert.Null(saved.Export.Frames); // 가져온 아틀라스만 다시 쓰고 프레임 시트 사본은 만들지 않는다
        Assert.False(File.Exists(dir.File("player@2x_frames.png")));
        var meta = UnityMeta.Read(png + ".meta");
        Assert.Equal(original.Guid, meta.Guid);
        Assert.Equal(SpriteMode.Multiple, meta.SpriteMode);
        Assert.Equal(original.Sprites.Select(s => (s.Name, s.UnityRect, s.Alignment, s.Pivot, s.Border, s.SpriteId, s.InternalId)),
            meta.Sprites.Select(s => (s.Name, s.UnityRect, s.Alignment, s.Pivot, s.Border, s.SpriteId, s.InternalId)));
        Assert.True(PngIO.Load(png).ContentEquals(originalImage));

        // 머리가 커져서 원래 칸에 안 들어가면 빈 곳으로 옮겨지고, 이름 기준 ID는 그대로
        p.ResizePart(3, 18, 10, Anchor.MiddleLeft);
        ProjectIO.SaveWithExport(p);
        var moved = UnityMeta.Read(png + ".meta");
        Assert.Equal(original.Guid, moved.Guid);
        var head = moved.Sprites.Single(s => s.Name == "Head");
        Assert.Equal(new Size(18, 10), head.UnityRect.Size);
        Assert.Equal((original.Sprites[0].SpriteId, original.Sprites[0].InternalId), (head.SpriteId, head.InternalId));
        foreach (var s in moved.Sprites.Where(s => s.Name != "Head"))
            Assert.Equal(original.Sprites.Single(o => o.Name == s.Name).UnityRect, s.UnityRect);
        var size = PngIO.ReadSize(png);
        AssertNoOverlap(moved.Sprites.Select(s => s.ToTopLeftRect(size.Height)).ToList(), 0);
    }

    [Fact]
    public void Export_PartsAtlasPackingOptions()
    {
        var p = new SpriteProject(8, 8);
        var g = p.AddGroup();
        p.AddPart(g, "tall", 3, 12);
        p.AddPart(g, "wide", 20, 4);
        p.BeginCelStroke(1, 0).Clear(Blue);
        p.CommitCelStroke("칠");
        p.BeginCelStroke(2, 0).Clear(Red);
        p.CommitCelStroke("칠");
        p.Edit("배치", ProjectChange.Settings, () =>
        {
            p.Export.Spacing = 2;
            p.Export.AtlasPowerOfTwo = true;
            p.Export.AtlasOrder = AtlasPackOrder.LargestFirst;
        });
        var sheet = ProjectExport.BuildPartsSheet(p, "parts");
        Assert.Equal(["wide", "tall"], sheet.Entries.Select(e => e.Name));
        Assert.True(IsPow2(sheet.Image.Width) && IsPow2(sheet.Image.Height));
        AssertNoOverlap(sheet.Entries.Select(e => e.Rect).ToList(), 2);
        Assert.Equal(new Point(0, 0), sheet.Entries[0].Rect.Location);
        Assert.Equal(Red, sheet.Image.Get(sheet.Entries[0].Rect.X + 19, sheet.Entries[0].Rect.Y + 3));
    }

    // ───────────── 가져오기 ─────────────

    [Fact]
    public void Import_DifferentlySizedSlices_BecomeParts()
    {
        using var dir = new TempDir();
        var png = CopyPlayerFixture(dir);

        var analysis = ProjectImport.Analyze(png);
        Assert.Equal(ImportMode.Parts, analysis.SuggestedMode);
        Assert.Equal(4, analysis.SliceCount);
        Assert.False(analysis.NeedsModeChoice);

        var result = ProjectImport.Import(analysis);
        var p = result.Project;
        Assert.True(result.Imported);
        Assert.Equal(dir.File("player@2x.gpixel"), p.Path);
        Assert.False(p.IsDirty);
        Assert.Equal(new Size(32, 32), p.Size);
        Assert.Equal(16f, p.Sprite.PixelsPerUnit);
        Assert.Single(p.Frames);
        Assert.Equal(["lag", "Arm", "Body", "Head", "player@2x"], Names(p));
        AssertHierarchy(p);
        var image = PngIO.Load(png);
        for (int i = 0; i < 4; i++)
        {
            var part = p.Layers[3 - i];
            Assert.Equal((PlayerNames[i], PlayerRects[i].Size, PlayerRects[i].Location, PlayerPivots[i], (Rectangle?)PlayerRects[i]),
                (part.Name, part.PartSize, part.PartOffset, part.PartPivot, part.PartSourceRect));
            Assert.True(p.GetCel(3 - i, 0)!.ContentEquals(image.Crop(PlayerRects[i])));
        }
        Assert.True(p.Flatten(0).ContentEquals(image));
        Assert.Equal((true, "player@2x", false, "player@2x_frames", true),
            (p.Export.ExportParts, p.Export.PartsFileName, p.Export.ExportFrames, p.Export.FileName, p.Export.KeepImportedRects));

        // 프레임으로 강제하면 칸마다 프레임
        Assert.Equal(4, ProjectImport.Open(png, mode: ImportMode.Frames).Project.Frames.Count);
    }

    [Fact]
    public void Import_SameSizeSlices_NeedsChoice_AndPartsChoiceWorks()
    {
        using var dir = new TempDir();
        var png = dir.File("tiles.png");
        var doc = new Document(TestUtil.RandomImage(32, 16, opaque: true), png);
        doc.Sprite.Mode = SpriteMode.Multiple;
        doc.Slices.Add(new Slice("a", new Rectangle(0, 0, 16, 16), Pivot.Center, new SpriteBorder(1, 2, 3, 4)));
        doc.Slices.Add(new Slice("b", new Rectangle(16, 0, 16, 16), Pivot.BottomCenter));
        DocumentIO.Save(doc, png, writeMeta: true);

        var analysis = ProjectImport.Analyze(png);
        Assert.True(analysis.NeedsModeChoice);
        Assert.Equal(ImportMode.Frames, analysis.SuggestedMode);
        var parts = ProjectImport.Import(analysis, ImportMode.Parts).Project;
        Assert.Equal(["b", "a", "tiles"], Names(parts));
        Assert.Equal(new SpriteBorder(1, 2, 3, 4), parts.Layers[1].PartBorder);
        Assert.Equal(Pivot.BottomCenter, parts.Layers[0].PartPivot);

        // 칸 없는 PNG를 부위로 → 이미지 전체 1개(이름이 겹치면 그룹 이름이 바뀜)
        var plain = dir.File("plain.png");
        PngIO.Save(TestUtil.RandomImage(5, 7), plain);
        var single = ProjectImport.Open(plain, mode: ImportMode.Parts).Project;
        Assert.Equal(["plain", "plain 2"], Names(single));
        Assert.Equal(new Size(5, 7), single.Layers[0].PartSize);

        // 이미 .gpixel이 있으면 묻지 않는다
        ProjectIO.Save(parts, ProjectImport.GetProjectPathForPng(png));
        var again = ProjectImport.Analyze(png);
        Assert.False(again.NeedsModeChoice);
        Assert.True(ProjectImport.Import(again).Project.Layers[2].IsGroup);
    }

    // ───────────── 이름 바꾸기 ─────────────

    [Fact]
    public void RenameProject_MovesDefaultPartsAtlasToo()
    {
        using var dir = new TempDir();
        var oldPath = dir.File("hero.gpixel");
        var newPath = dir.File("renamed.gpixel");
        var p = new SpriteProject(4, 4);
        var g = p.AddGroup();
        p.AddPart(g, "Head", 2, 2);
        Paint(p, 1, 0, 0, 0, Red);
        p.Edit("메타", ProjectChange.Settings, () => p.Export.WriteMeta = true);
        ProjectIO.SaveWithExport(p, oldPath);
        var oldPng = dir.File("hero.png");
        var oldParts = dir.File("hero_parts.png");
        var guid = UnityMeta.Read(oldParts + ".meta").Guid;
        Assert.Equal([oldPng, oldPng + ".meta", oldParts, oldParts + ".meta"],
            ProjectIO.GetOwnedDefaultExportPaths(oldPath));

        ProjectIO.RenameWithOwnedDefaultExport(oldPath, newPath);
        var newParts = dir.File("renamed_parts.png");
        Assert.False(File.Exists(oldParts));
        Assert.True(File.Exists(newParts));
        Assert.Equal(guid, UnityMeta.Read(newParts + ".meta").Guid);

        // 이름을 지정한 부위 아틀라스는 프로젝트 것이 아니다
        var loaded = ProjectIO.Load(newPath);
        loaded.Edit("이름", ProjectChange.Settings, () => loaded.Export.PartsFileName = "atlas");
        ProjectIO.SaveWithExport(loaded);
        Assert.DoesNotContain(ProjectIO.GetOwnedDefaultExportPaths(newPath), x => x.Contains("atlas"));
        Assert.DoesNotContain(ProjectIO.GetOwnedDefaultExportPaths(newPath), x => x.EndsWith("renamed_parts.png"));
    }

    // ───────────── 배치기 ─────────────

    [Fact]
    public void Packer_NoOverlap_SpacingMaxWidthAndPowerOfTwo()
    {
        var rng = new Random(3);
        var sizes = Enumerable.Range(0, 40).Select(_ => new Size(rng.Next(1, 30), rng.Next(1, 30))).ToList();
        foreach (var order in new[] { AtlasPackOrder.KeepOrder, AtlasPackOrder.LargestFirst })
            foreach (var spacing in new[] { 0, 1, 3 })
            {
                var r = AtlasPacker.Pack(sizes, new AtlasPackOptions(MaxWidth: 100, Spacing: spacing, PowerOfTwo: true, Order: order));
                Assert.Equal(sizes, r.Rects.Select(x => x.Size));
                AssertNoOverlap(r.Rects.ToList(), spacing);
                Assert.All(r.Rects, x => Assert.True(x.Right <= 100 && x.X >= 0 && x.Y >= 0));
                Assert.All(r.Rects, x => Assert.True(x.Right <= r.Size.Width && x.Bottom <= r.Size.Height));
                Assert.True(IsPow2(r.Size.Width) && IsPow2(r.Size.Height));
            }

        // 같은 높이 + 순서 유지 = 행 채우기
        var rows = AtlasPacker.Pack(Enumerable.Repeat(new Size(10, 10), 5).ToList(), new AtlasPackOptions(MaxWidth: 32, Spacing: 1));
        Assert.Equal([new Point(0, 0), new Point(11, 0), new Point(22, 0), new Point(0, 11), new Point(11, 11)],
            rows.Rects.Select(x => x.Location));
        Assert.Equal(new Size(32, 21), rows.Size);

        // 자동 너비: 가장 넓은 스프라이트 이상
        var auto = AtlasPacker.Pack([new Size(50, 2), new Size(3, 3)], new AtlasPackOptions());
        Assert.True(auto.Size.Width >= 50);
    }

    [Fact]
    public void Packer_FixedRects_KeptWhenFitting_OthersPlacedAround()
    {
        Rectangle?[] fixedRects = [new Rectangle(0, 0, 10, 10), new Rectangle(5, 5, 10, 10), null, new Rectangle(20, 0, 4, 4)];
        Size[] sizes = [new(10, 10), new(10, 10), new(6, 6), new(5, 5)];
        var r = AtlasPacker.Pack(sizes, new AtlasPackOptions(MaxWidth: 30, MinSize: new Size(40, 12)), fixedRects);
        Assert.Equal(new Rectangle(0, 0, 10, 10), r.Rects[0]);      // 고정
        Assert.NotEqual(new Point(5, 5), r.Rects[1].Location);       // 고정 칸과 겹쳐서 새로 배치
        Assert.NotEqual(new Point(20, 0), r.Rects[3].Location);      // 커져서 원래 칸에 안 들어감
        AssertNoOverlap(r.Rects.ToList(), 0);
        Assert.True(r.Size.Width >= 40 && r.Size.Height >= 12);
    }

    // ───────────── 도우미 ─────────────

    private static bool IsPow2(int v) => v > 0 && (v & (v - 1)) == 0;

    /// <summary>모든 쌍이 겹치지 않고 간격 이상 떨어져 있는지.</summary>
    private static void AssertNoOverlap(IReadOnlyList<Rectangle> rects, int spacing)
    {
        for (int i = 0; i < rects.Count; i++)
            for (int j = i + 1; j < rects.Count; j++)
                Assert.False(Rectangle.Inflate(rects[i], spacing, spacing).IntersectsWith(rects[j]),
                    $"{rects[i]}와 {rects[j]}가 간격 {spacing} 안에서 겹칩니다.");
    }
}
