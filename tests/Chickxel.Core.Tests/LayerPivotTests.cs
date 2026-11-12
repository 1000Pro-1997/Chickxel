using System.Drawing;
using System.IO.Compression;
using System.Text.Json.Nodes;
using Chickxel.Core.Edit;
using Chickxel.Core.Effects;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>
/// 레이어마다의 피벗(일반 레이어·그룹·부위, 프레임마다): 효과의 피벗, 그림을 옮기거나 바꿀 때 따라가기, 캔버스 피벗의 대표 레이어,
/// 저장·읽기(형식 5)와 예전 형식(4 이하) 바꿔 읽기, 실행취소.
/// </summary>
public class LayerPivotTests
{
    private static readonly Color32 White = new(255, 255, 255);

    /// <summary>피벗을 기준으로 시계 방향 90°(공식 회전 스크립트).</summary>
    private static LayerEffect Rotate90() => LegacyEffects.ToScript(new RotateEffect { Angle = 90 }, EffectParity.Official("rotate"));

    /// <summary>8×8 캔버스의 (<paramref name="x"/>, <paramref name="y"/>)에 흰 점 하나.</summary>
    private static PixelImage Dot(int x, int y)
    {
        var image = new PixelImage(8, 8);
        image[x, y] = White;
        return image;
    }

    /// <summary>하나뿐인 점의 자리(그림 기준).</summary>
    private static Point OnlyDot(PixelImage image)
    {
        var b = image.GetOpaqueBounds();
        Assert.Equal(new Size(1, 1), b.Size);
        return b.Location;
    }

    /// <summary>캔버스 픽셀 → 캔버스 기준 피벗(8×8 — 0.5 단위 좌표는 이진수로 정확).</summary>
    private static Pivot At(float x, float y) => Pivot.FromLocalPixel(x, y, 8, 8);

    [Fact]
    public void EffectPivot_EachLayerRotatesAroundItsOwnPivotPerFrame()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        var second = p.AddLayer();
        for (int f = 0; f < 2; f++)
        {
            p.SetCel(0, f, Dot(4, 4));
            p.SetCel(1, f, Dot(2, 2));
        }
        // 점 가운데를 각 레이어의 피벗으로: 회전해도 점은 제자리(같은 피벗이었다면 한쪽은 옮겨짐)
        p.SetLayerPivot(0, At(4.5f, 4.5f));
        p.SetLayerPivot(1, At(2.5f, 2.5f));
        p.SetLayerEffects(0, [Rotate90()]);
        p.SetLayerEffects(1, [Rotate90()]);
        Assert.Equal(new PointF(4.5f, 4.5f), p.EffectPivot(p.Layers[0], 1));
        Assert.Equal(new PointF(2.5f, 2.5f), p.EffectPivot(second, 1));
        Assert.Equal(new Point(4, 4), OnlyDot(p.PlanCel(0, 1)!.Render()));
        Assert.Equal(new Point(2, 2), OnlyDot(p.PlanCel(1, 1)!.Render()));

        // 한 프레임만의 피벗은 그 프레임의 효과에만
        p.SetLayerFramePivot(1, 1, At(4.5f, 4.5f));
        Assert.Equal(new PointF(4.5f, 4.5f), p.EffectPivot(second, 1));
        Assert.Equal(new PointF(2.5f, 2.5f), p.EffectPivot(second, 0));
        Assert.NotEqual(new Point(2, 2), OnlyDot(p.PlanCel(1, 1)!.Render()));
        // 캔버스 피벗은 레이어 피벗과 따로
        Assert.Equal(Pivot.Center, p.GetFramePivot(1));
        p.Undo();
        Assert.False(p.HasLayerFramePivot(second, 1));
        Assert.Equal(new Point(2, 2), OnlyDot(p.PlanCel(1, 1)!.Render()));
    }

    [Fact]
    public void NewLayersAndGroups_StartWithTheCanvasDefaultPivot()
    {
        var p = new SpriteProject(8, 8);
        p.Edit("피벗", ProjectChange.Settings, () => p.Sprite.DefaultPivot = Pivot.BottomCenter);
        Assert.Equal(Pivot.BottomCenter, p.AddLayer().Pivot);
        Assert.Equal(Pivot.BottomCenter, p.AddGroup("g").Pivot);
    }

    [Fact]
    public void ShiftCels_MovesTheLayerPivotOnlyInTheMovedFrames()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        p.AddFrame();
        for (int f = 0; f < 3; f++) p.SetCel(0, f, Dot(2, 2));
        var layer = p.Layers[0];
        Assert.Equal(new PointF(4, 4), p.LayerPivotPixel(layer, 0));

        // 프레임 0만: 그 프레임만의 피벗이 생기고 나머지는 그대로
        Assert.Equal(1, p.ShiftCels("옮기기", [(0, 0)], 2, 1));
        Assert.Equal([new PointF(6, 5), new PointF(4, 4), new PointF(4, 4)], Enumerable.Range(0, 3).Select(f => p.LayerPivotPixel(layer, f)));
        Assert.Equal([true, false, false], Enumerable.Range(0, 3).Select(f => p.HasLayerFramePivot(layer, f)));

        // 모든 프레임: 레이어 피벗과 그 프레임만의 피벗이 같이(새 프레임만의 피벗은 생기지 않음)
        Assert.Equal(3, p.ShiftCels("옮기기", [(0, 0), (0, 1), (0, 2)], -1, 0));
        Assert.Equal([new PointF(5, 5), new PointF(3, 4), new PointF(3, 4)], Enumerable.Range(0, 3).Select(f => p.LayerPivotPixel(layer, f)));
        Assert.Equal([true, false, false], Enumerable.Range(0, 3).Select(f => p.HasLayerFramePivot(layer, f)));

        p.Undo();
        p.Undo();
        Assert.Equal(Pivot.Center, layer.Pivot);
        Assert.All(Enumerable.Range(0, 3), f => Assert.False(p.HasLayerFramePivot(layer, f)));

        // 링크된 셀은 같이 옮겨지므로 그 프레임의 피벗도
        p.LinkCels([(0, 0), (0, 1)]);
        p.ShiftCels("옮기기", [(0, 0)], 1, 0);
        Assert.Equal([new PointF(5, 4), new PointF(5, 4), new PointF(4, 4)], Enumerable.Range(0, 3).Select(f => p.LayerPivotPixel(layer, f)));
    }

    [Fact]
    public void ShiftCels_WithoutMovingPivots_MovesOnlyThePixels()
    {
        var p = new SpriteProject(8, 8);
        p.SetCel(0, 0, Dot(2, 2));
        var layer = p.Layers[0];
        Assert.Equal(1, p.ShiftCels("옮기기", [(0, 0)], 2, 1, movePivots: false));
        Assert.Equal(new PointF(4, 4), p.LayerPivotPixel(layer, 0));
        Assert.False(p.HasLayerFramePivot(layer, 0));
        Assert.Equal(White, p.GetCel(0, 0)![4, 3]);
    }

    [Fact]
    public void CanvasFlipAndRotate_MapEveryLayerPivotInItsOwnSpace()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        var group = p.AddGroup("g");
        var part = p.AddPart(group, "부위", 4, 2, new Point(0, 0));
        p.SetLayerPivot(0, At(1.5f, 2.5f));
        p.SetLayerFramePivot(0, 1, At(0.5f, 0.5f));
        p.SetLayerPivot(p.IndexOf(group), At(6.5f, 6.5f));
        p.SetLayerPivot(p.IndexOf(part), Pivot.FromLocalPixel(1, 1, 4, 2));
        var normal = p.Layers[0];

        p.FlipCanvasHorizontal();
        Assert.Equal(new PointF(6.5f, 2.5f), p.LayerPivotPixel(normal, 0));
        Assert.Equal(new PointF(7.5f, 0.5f), p.LayerPivotPixel(normal, 1));
        Assert.Equal(new PointF(1.5f, 6.5f), p.LayerPivotPixel(group, 0));
        Assert.Equal(new PointF(7, 1), p.LayerPivotPixel(part, 0)); // 부위 (4,0)~(8,2)에서 뒤집힌 (3,1)
        p.Undo();
        Assert.Equal(new PointF(1.5f, 2.5f), p.LayerPivotPixel(normal, 0));

        p.RotateCanvas90(clockwise: true); // (x, y) → (h - y, x)
        Assert.Equal(new PointF(5.5f, 1.5f), p.LayerPivotPixel(normal, 0));
        Assert.Equal(new PointF(1.5f, 6.5f), p.LayerPivotPixel(group, 0));
        Assert.Equal(new PointF(7, 1), p.LayerPivotPixel(part, 0));
    }

    [Fact]
    public void PivotLayer_DrivesTheCanvasPivotAndTheExportedPivot()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        for (int f = 0; f < 2; f++) p.SetCel(0, f, Dot(3, 3));
        p.Export.Common.Trim = false;
        var hero = p.Layers[0];
        p.SetLayerPivot(0, At(2, 6));
        p.SetLayerFramePivot(0, 1, At(4, 2));
        Assert.Null(p.PivotLayer);
        Assert.Equal(Pivot.Center, p.GetFramePivot(0)); // 대표 레이어가 없으면 캔버스 자신의 피벗

        p.SetPivotLayer(hero);
        Assert.Same(hero, p.PivotLayer);
        Assert.Equal([At(2, 6), At(4, 2)], Enumerable.Range(0, 2).Select(p.GetFramePivot));
        Assert.Equal(Pivot.Center, p.GetCanvasOwnPivot(0));
        Assert.Equal([At(2, 6), At(4, 2)], ProjectExport.BuildSheet(p, "hero", allFrames: true).Entries.Select(e => e.Pivot));

        // 부위가 대표면 그 프레임의 부위 자리 + 부위 피벗(캔버스 기준으로)
        var group = p.AddGroup("g");
        var part = p.AddPart(group, "부위", 4, 4, new Point(2, 2));
        p.MovePartFrom(p.IndexOf(part), 1, 2, 0);
        p.SetPivotLayer(part);
        Assert.Equal([At(4, 4), At(6, 4)], Enumerable.Range(0, 2).Select(p.GetFramePivot));

        p.Undo();
        Assert.Same(hero, p.PivotLayer);
        // 대표 레이어를 지우면 없어지고, 되돌리면 돌아온다
        p.DeleteLayer(p.IndexOf(hero));
        Assert.Null(p.PivotLayer);
        Assert.Equal(Pivot.Center, p.GetFramePivot(0));
        p.Undo();
        Assert.Same(hero, p.PivotLayer);
        p.SetPivotLayer(null);
        Assert.Null(p.PivotLayer);
        Assert.Throws<ArgumentException>(() => p.SetPivotLayer(new Layer("밖")));
    }

    [Fact]
    public void DuplicateLayer_CopiesPivotsAndFramePivots()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        p.SetLayerPivot(0, At(1, 2));
        p.SetLayerFramePivot(0, 1, At(3, 4));
        var copy = p.DuplicateLayer(0);
        Assert.Equal(At(1, 2), copy.Pivot);
        Assert.Equal(At(3, 4), p.GetLayerPivot(copy, 1));
    }

    [Fact]
    public void MoveIntoAndOutOfGroup_KeepTheSamePivotPointOnTheCanvas()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        var image = new PixelImage(8, 8);
        image[2, 2] = White;
        image[5, 3] = White; // 부위 자리 (2,2) 4×2
        p.SetCel(0, 0, image);
        p.SetCel(0, 1, image.Clone());
        var layer = p.Layers[0];
        p.SetLayerPivot(0, At(3, 3));
        p.SetLayerFramePivot(0, 1, At(5, 4));
        var group = p.AddGroup("g");

        // 일반 → 부위: 피벗을 부위 기준으로(캔버스의 같은 점)
        p.MoveIntoGroup([layer], group, 0);
        Assert.True(layer.IsPart);
        Assert.Equal(new Rectangle(2, 2, 4, 2), layer.PartRect);
        Assert.Equal(Pivot.FromLocalPixel(1, 1, 4, 2), layer.Pivot);
        Assert.Equal([new PointF(3, 3), new PointF(5, 4)], Enumerable.Range(0, 2).Select(f => p.LayerPivotPixel(layer, f)));

        // 프레임 1만 옮긴 뒤 꺼내기: 부위 기준 → 캔버스 기준(옮긴 프레임은 그 프레임만의 피벗으로)
        p.MovePartFrom(p.IndexOf(layer), 1, 1, 0);
        Assert.Equal(new PointF(6, 4), p.LayerPivotPixel(layer, 1));
        p.MoveOutOfGroup([layer], 0);
        Assert.False(layer.IsPart);
        Assert.Equal(At(3, 3), layer.Pivot);
        Assert.Equal([new PointF(3, 3), new PointF(6, 4)], Enumerable.Range(0, 2).Select(f => p.LayerPivotPixel(layer, f)));
        p.Undo();
        Assert.True(layer.IsPart);
        Assert.Equal(Pivot.FromLocalPixel(1, 1, 4, 2), layer.Pivot);
    }

    [Fact]
    public void SaveAndLoad_KeepsLayerPivotsFramePivotsAndPivotLayer()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        var group = p.AddGroup("g");
        var part = p.AddPart(group, "부위", 4, 4);
        p.SetLayerPivot(0, At(1, 2));
        p.SetLayerFramePivot(0, 1, At(3, 4));
        p.SetLayerPivot(p.IndexOf(group), At(5, 6));
        p.SetLayerFramePivot(p.IndexOf(part), 1, Pivot.BottomCenter);
        p.SetPivotLayer(group);

        using var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        ms.Position = 0;
        Assert.Equal(ProjectIO.CurrentVersion, ReadJson(ms)["version"]!.GetValue<int>());
        ms.Position = 0;
        var back = ProjectIO.Load(ms);
        Assert.Null(back.ConvertedFromVersion);
        Assert.Equal(p.Layers.Select(l => l.Pivot), back.Layers.Select(l => l.Pivot));
        for (int i = 0; i < p.Layers.Count; i++)
            Assert.Equal(Enumerable.Range(0, 2).Select(f => p.GetLayerPivot(p.Layers[i], f)), Enumerable.Range(0, 2).Select(f => back.GetLayerPivot(back.Layers[i], f)));
        Assert.Equal(p.IndexOf(group), back.IndexOf(back.PivotLayer!));
    }

    [Fact]
    public void LoadingAVersion4File_CopiesTheCanvasPivotsIntoLayers_RendersTheSame_AndReportsTheConversion()
    {
        // 예전 판의 뜻: 일반 레이어·그룹의 효과는 캔버스 피벗을 돌았다 — 레이어 피벗을 캔버스 피벗과 같게 둔 프로젝트로 만들고 형식 4로 낮춘다
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        for (int f = 0; f < 2; f++) p.SetCel(0, f, Dot(2, 2));
        var group = p.AddGroup("g");
        var part = p.AddPart(group, "부위", 4, 4, new Point(4, 4));
        p.SetCel(p.IndexOf(part), 0, new PixelImage(4, 4) { [1, 1] = White });
        var canvas = At(2.5f, 2.5f);
        var frame1 = At(4.5f, 4.5f);
        p.Edit("피벗", ProjectChange.Settings, () => p.Sprite.DefaultPivot = canvas);
        p.SetFramePivot(1, frame1);
        foreach (var layer in new[] { p.Layers[0], group })
        {
            p.SetLayerPivot(p.IndexOf(layer), canvas);
            p.SetLayerFramePivot(p.IndexOf(layer), 1, frame1);
        }
        p.SetLayerPivot(p.IndexOf(part), Pivot.BottomCenter);
        p.SetLayerFramePivot(p.IndexOf(part), 1, Pivot.Center);
        p.SetLayerEffects(0, [Rotate90()]);
        p.SetLayerEffects(p.IndexOf(group), [Rotate90()]);
        var expected = Enumerable.Range(0, 2).Select(f => p.Flatten(f)).ToList();

        var file = Downgrade(p);
        var old = ProjectIO.Load(file);
        Assert.Equal(4, old.ConvertedFromVersion);
        Assert.Null(old.PivotLayer);
        Assert.Equal(canvas, old.Sprite.DefaultPivot);
        Assert.Equal(frame1, old.Frames[1].Pivot);
        foreach (var i in new[] { 0, p.IndexOf(group) })
        {
            Assert.Equal(canvas, old.Layers[i].Pivot);
            Assert.Equal(frame1, old.GetLayerPivot(old.Layers[i], 1));
        }
        var oldPart = old.Layers[p.IndexOf(part)];
        Assert.Equal([Pivot.BottomCenter, Pivot.Center], Enumerable.Range(0, 2).Select(f => old.GetLayerPivot(oldPart, f)));
        for (int f = 0; f < 2; f++) Assert.True(expected[f].ContentEquals(old.Flatten(f)));

        // 다시 저장하면 새 형식
        using var ms = new MemoryStream();
        ProjectIO.Save(old, ms);
        ms.Position = 0;
        Assert.Equal(ProjectIO.CurrentVersion, ReadJson(ms)["version"]!.GetValue<int>());
    }

    [Fact]
    public void Merge_MovesNormalLayerPivotsWithThePicture()
    {
        var a = new SpriteProject(8, 8);
        a.AddFrame();
        a.SetCel(0, 0, Dot(1, 1));
        a.SetLayerPivot(0, At(1, 1));
        a.SetLayerFramePivot(0, 1, At(2, 2));
        var p = ProjectMerge.Merge([new MergeSource("a", a)], new MergeOptions(MergeMode.Layers, 12, 10, Anchor.BottomCenter, "x"));
        var layer = p.Layers.Single(l => !l.IsPart && !l.IsGroup);
        // 아래 가운데: 그림이 (2,2)만큼 옮겨지므로 피벗도
        foreach (var (f, x, y) in new[] { (0, 3f, 3f), (1, 4f, 4f) })
        {
            var at = p.LayerPivotPixel(layer, f);
            Assert.Equal(x, at.X, 3);
            Assert.Equal(y, at.Y, 3);
        }
    }

    [Fact]
    public void LoadingANewerVersion_IsRejected()
    {
        var p = new SpriteProject(4, 4);
        var file = Rewrite(p, json => json["version"] = ProjectIO.CurrentVersion + 1);
        Assert.Throws<InvalidDataException>(() => ProjectIO.Load(file));
    }

    [Fact]
    public void LayerPivotEdits_UndoAndRedo()
    {
        var p = new SpriteProject(8, 8);
        p.AddFrame();
        p.AddFrame();
        var layer = p.Layers[0];
        p.SetLayerPivot(0, At(1, 1));
        p.SetLayerFramesFrom(0, 1, own: true);
        Assert.Equal([false, true, true], Enumerable.Range(0, 3).Select(f => p.HasLayerFrameOverride(layer, f)));
        p.SetLayerPivotFrom(0, 1, At(3, 1));
        Assert.Equal([new PointF(1, 1), new PointF(3, 1), new PointF(3, 1)], Enumerable.Range(0, 3).Select(f => p.LayerPivotPixel(layer, f)));
        p.SetLayerFramesFrom(0, 2, own: false);
        Assert.Equal([new PointF(1, 1), new PointF(3, 1), new PointF(1, 1)], Enumerable.Range(0, 3).Select(f => p.LayerPivotPixel(layer, f)));
        p.Undo();
        p.Undo();
        p.Undo();
        Assert.All(Enumerable.Range(0, 3), f => Assert.False(p.HasLayerFramePivot(layer, f)));
        p.Undo();
        Assert.Equal(Pivot.Center, layer.Pivot);
        p.Redo();
        Assert.Equal(At(1, 1), layer.Pivot);
    }

    // ───────────── 파일 손보기 ─────────────

    private static JsonNode ReadJson(Stream zipStream)
    {
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        using var s = zip.GetEntry(ProjectIO.ProjectEntryName)!.Open();
        return JsonNode.Parse(s)!;
    }

    /// <summary>저장한 프로젝트의 <c>project.json</c>을 고쳐 쓴 파일(메모리).</summary>
    private static MemoryStream Rewrite(SpriteProject p, Action<JsonNode> change)
    {
        var ms = new MemoryStream();
        ProjectIO.Save(p, ms);
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.GetEntry(ProjectIO.ProjectEntryName)!;
            JsonNode json;
            using (var s = entry.Open()) json = JsonNode.Parse(s)!;
            change(json);
            entry.Delete();
            using var w = new StreamWriter(zip.CreateEntry(ProjectIO.ProjectEntryName).Open());
            w.Write(json.ToJsonString());
        }
        ms.Position = 0;
        return ms;
    }

    /// <summary>형식 4로 낮춘 파일: 부위만 피벗(<c>partPivot</c>·프레임의 <c>partPivots</c>), 일반 레이어·그룹 피벗과 대표 레이어 없음.</summary>
    private static MemoryStream Downgrade(SpriteProject p) => Rewrite(p, json =>
    {
        json["version"] = 4;
        json.AsObject().Remove("pivotLayer");
        var layers = json["layers"]!.AsArray();
        var parts = new HashSet<int>();
        for (int i = 0; i < layers.Count; i++)
        {
            var l = layers[i]!.AsObject();
            var pivot = l["pivot"]?.DeepClone();
            l.Remove("pivot");
            if (l["kind"]?.GetValue<string>() != "Part") continue;
            parts.Add(i);
            l["partPivot"] = pivot;
        }
        foreach (var f in json["frames"]!.AsArray())
        {
            var frame = f!.AsObject();
            if (frame["layerPivots"] is JsonArray list)
            {
                var kept = new JsonArray(list.Where(e => parts.Contains(e!["layer"]!.GetValue<int>())).Select(e => e!.DeepClone()).ToArray());
                if (kept.Count > 0) frame["partPivots"] = kept;
            }
            frame.Remove("layerPivots");
        }
    });
}
