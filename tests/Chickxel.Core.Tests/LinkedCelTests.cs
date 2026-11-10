using System.Drawing;
using System.IO.Compression;
using Chickxel.Core.IO;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>링크된 셀(여러 프레임 셀이 그림 하나를 같이 씀): 링크 걸기·풀기, 함께 고치기, 구조 편집·저장·실행취소에서 링크가 남는지.</summary>
public class LinkedCelTests
{
    private static readonly Color32 Red = new(255, 0, 0), Green = new(0, 255, 0), Blue = new(0, 0, 255);

    private static void Draw(SpriteProject p, int x, int y, Color32 color, int layer = 0, int frame = 0)
    {
        var img = p.BeginCelStroke(layer, frame);
        img.Set(x, y, color);
        Assert.True(p.CommitCelStroke("연필"));
    }

    /// <summary>프레임 <paramref name="frames"/>개, 프레임 0의 (0,0)만 빨강.</summary>
    private static SpriteProject Frames(int frames, int size = 4)
    {
        var p = new SpriteProject(size, size);
        for (int i = 1; i < frames; i++) p.AddFrame();
        Draw(p, 0, 0, Red);
        return p;
    }

    private static Color32 At(SpriteProject p, int frame, int x, int y, int layer = 0) => p.GetCel(layer, frame)?.Get(x, y) ?? default;

    /// <summary>레이어 0의 프레임들을 링크한다.</summary>
    private static int Link(SpriteProject p, params int[] frames) => p.LinkCels(frames.Select(f => (0, f)));

    [Fact]
    public void LinkCels_EmptyFramesShareFirstCel_EditsFollow_UndoRestores()
    {
        var p = Frames(4);
        Assert.Equal(3, Link(p, 0, 1, 2, 3));
        Assert.All([0, 1, 2, 3], f => Assert.True(p.IsCelLinked(0, f)));
        Assert.Equal([0, 1, 2, 3], p.LinkedFrames(0, 2));

        Draw(p, 1, 1, Green, frame: 2);
        Assert.All([0, 1, 3], f => Assert.Equal(Green, At(p, f, 1, 1)));

        p.Undo(); // 그리기
        Assert.All([0, 1, 2, 3], f => Assert.Equal(default, At(p, f, 1, 1)));
        p.Undo(); // 링크
        Assert.False(p.IsCelLinked(0, 0));
        Assert.True(p.IsCelEmpty(0, 1));
        p.Redo();
        Assert.True(p.AreCelsLinked(0, 0, 3));
    }

    [Fact]
    public void LinkCels_NoPixelsInSelection_DoesNothing()
    {
        var p = Frames(3);
        int steps = p.History.UndoCount;
        Assert.Equal(0, Link(p, 1, 2));
        Assert.Equal(steps, p.History.UndoCount);
    }

    [Fact]
    public void LinkCels_ScatteredCels_LinkPerLayerOnlyThosePicked()
    {
        var p = Frames(4);
        p.AddLayer();
        Draw(p, 1, 1, Blue, layer: 1, frame: 1);
        Assert.Equal(2, p.LinkCels([(0, 0), (0, 2), (1, 1), (1, 3)]));
        Assert.True(p.AreCelsLinked(0, 0, 2));
        Assert.False(p.IsCelLinked(0, 1));
        Assert.True(p.AreCelsLinked(1, 1, 3));
        Assert.False(p.IsCelLinked(1, 0));
        Assert.Equal(Blue, At(p, 3, 1, 1, layer: 1));
        Assert.Equal(default, At(p, 3, 1, 1, layer: 0));
    }

    [Fact]
    public void LinkCels_PickingAnExistingLink_JoinsIt()
    {
        var p = Frames(5);
        Draw(p, 2, 2, Blue, frame: 1); // 프레임 1은 프레임 0과 다른 그림
        Draw(p, 3, 3, Green, frame: 3);
        Link(p, 3, 4); // 묶음 3~4(초록)
        Assert.Equal(2, Link(p, 0, 1, 3)); // 앞 칸이 아니라 이미 있는 묶음의 그림으로
        Assert.All([0, 1, 4], f => Assert.True(p.AreCelsLinked(0, 3, f)));
        Assert.Equal(Green, At(p, 0, 3, 3));
        Assert.Equal(default, At(p, 1, 2, 2));
        Assert.False(p.IsCelLinked(0, 2));
    }

    [Fact]
    public void LinkCels_MainFrame_IsSourceOverFirstAndExistingLink_EmptyMainFallsBack()
    {
        var p = Frames(5);
        Draw(p, 2, 2, Blue, frame: 2);
        Draw(p, 3, 3, Green, frame: 3);
        Link(p, 3, 4); // 묶음 3~4(초록)
        Assert.Equal(3, p.LinkCels([(0, 0), (0, 1), (0, 2), (0, 3)], mainFrame: 2)); // 첫 칸·이미 있는 묶음이 아니라 누른 칸(파랑)의 그림으로
        Assert.All([0, 1, 3], f => Assert.True(p.AreCelsLinked(0, 2, f)));
        Assert.All([0, 1, 3], f => Assert.Equal(Blue, At(p, f, 2, 2)));
        Assert.Equal(default, At(p, 0, 0, 0));
        Assert.False(p.AreCelsLinked(0, 3, 4));

        var q = Frames(3);
        Assert.Equal(2, q.LinkCels([(0, 0), (0, 1), (0, 2)], mainFrame: 1)); // 누른 칸이 비었으면 그림 있는 첫 칸으로
        Assert.All([0, 1, 2], f => Assert.Equal(Red, At(q, f, 0, 0)));
    }

    [Fact]
    public void LinkCels_LinkedMainFrame_OthersJoinItsLink()
    {
        var p = Frames(5);
        Draw(p, 3, 3, Green, frame: 3);
        Link(p, 3, 4); // 묶음 3~4(초록)
        Assert.Equal(2, p.LinkCels([(0, 0), (0, 1), (0, 4)], mainFrame: 4));
        Assert.All([0, 1, 3], f => Assert.True(p.AreCelsLinked(0, 4, f)));
        Assert.Equal(Green, At(p, 0, 3, 3));
    }

    [Fact]
    public void UnlinkCels_EachCelEditedAlone()
    {
        var p = Frames(3);
        Link(p, 0, 1, 2);
        Assert.Equal(new UnlinkResult(2, 0), p.UnlinkCels([(0, 0), (0, 1), (0, 2)])); // 마지막 셀은 혼자 남아 사본이 필요 없다
        Assert.All([0, 1, 2], f => Assert.False(p.IsCelLinked(0, f)));
        Assert.All([0, 1, 2], f => Assert.Equal(Red, At(p, f, 0, 0)));

        Draw(p, 2, 2, Blue, frame: 1);
        Assert.Equal(default, At(p, 0, 2, 2));
        Assert.Equal(default, At(p, 2, 2, 2));
    }

    [Fact]
    public void SetCel_ReplacesAllLinked_PlaceCel_ReplacesOnlyOne()
    {
        var p = Frames(3);
        Link(p, 0, 1, 2);
        var green = new PixelImage(4, 4);
        green.Set(3, 3, Green);
        p.SetCel(0, 1, green);
        Assert.All([0, 1, 2], f => Assert.Equal(Green, At(p, f, 3, 3)));
        Assert.True(p.AreCelsLinked(0, 0, 2));

        var blue = new PixelImage(4, 4);
        blue.Set(3, 3, Blue);
        p.PlaceCel(0, 1, blue);
        Assert.Equal(Blue, At(p, 1, 3, 3));
        Assert.Equal(Green, At(p, 0, 3, 3));
        Assert.False(p.IsCelLinked(0, 1));
        Assert.True(p.AreCelsLinked(0, 0, 2));

        p.SetCel(0, 0, null); // 비우기는 그 셀만
        Assert.True(p.IsCelEmpty(0, 0));
        Assert.Equal(Green, At(p, 2, 3, 3));
    }

    [Fact]
    public void MapCels_MapsLinkedImageOnce_AndUpdatesFramesOutsideFilter()
    {
        var p = Frames(3);
        Link(p, 0, 1, 2);
        int calls = 0;
        int changed = p.MapCels("색", (_, f) => ReferenceEquals(f, p.Frames[1]), cel =>
        {
            calls++;
            var copy = cel.Clone();
            copy.Set(0, 0, Blue);
            return copy;
        });
        Assert.Equal(1, calls);
        Assert.Equal(3, changed);
        Assert.All([0, 1, 2], f => Assert.Equal(Blue, At(p, f, 0, 0)));
        Assert.True(p.AreCelsLinked(0, 0, 2));
    }

    [Fact]
    public void ShiftCels_SelectingSeveralLinkedCels_ShiftsOnce()
    {
        var p = Frames(3);
        Link(p, 0, 1, 2);
        Assert.Equal(3, p.ShiftCels("옮기기", [(0, 0), (0, 1), (0, 2)], 1, 0));
        Assert.All([0, 1, 2], f => Assert.Equal(Red, At(p, f, 1, 0)));
        Assert.All([0, 1, 2], f => Assert.Equal(default, At(p, f, 2, 0)));
        Assert.True(p.AreCelsLinked(0, 0, 2));
    }

    [Fact]
    public void CanvasEdits_KeepLinks()
    {
        var p = Frames(3);
        Link(p, 0, 2); // 떨어진 칸
        p.FlipCanvasHorizontal();
        Assert.True(p.AreCelsLinked(0, 0, 2));
        Assert.Equal(Red, At(p, 2, 3, 0));
        p.ResizeCanvas(6, 6, Anchor.TopLeft);
        Assert.True(p.AreCelsLinked(0, 0, 2));
        Draw(p, 5, 5, Green, frame: 0);
        Assert.Equal(Green, At(p, 2, 5, 5));
        Assert.True(p.IsCelEmpty(0, 1));
    }

    [Fact]
    public void MergeDown_KeepsLowerLinksOnlyWhereResultsMatch()
    {
        var p = Frames(3);
        Link(p, 0, 1, 2);
        p.AddLayer();
        Draw(p, 3, 3, Blue, layer: 1, frame: 0);
        Draw(p, 3, 3, Blue, layer: 1, frame: 1);
        p.MergeLayerDown(1);
        Assert.True(p.AreCelsLinked(0, 0, 1)); // 같은 결과
        Assert.False(p.AreCelsLinked(0, 0, 2)); // 프레임 2는 위 그림이 없어 다르다
        Assert.Equal(Blue, At(p, 1, 3, 3));
        Assert.Equal(default, At(p, 2, 3, 3));
    }

    [Fact]
    public void SaveLoad_KeepsLinks_AndWritesEveryCelForOlderVersions()
    {
        var p = Frames(6);
        Link(p, 0, 1, 3);
        Draw(p, 0, 1, Blue, frame: 4);
        Link(p, 4, 5);
        Assert.True(p.SetLinkColor(0, 5, 5)); // 보라
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);

        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            Assert.All([0, 1, 3], f => Assert.NotNull(zip.GetEntry(ProjectIO.CelEntryName(0, f))));

        stream.Position = 0;
        var loaded = ProjectIO.Load(stream);
        Assert.True(loaded.AreCelsLinked(0, 0, 3));
        Assert.True(loaded.AreCelsLinked(0, 1, 3));
        Assert.True(loaded.IsCelEmpty(0, 2));
        Assert.False(loaded.IsCelLinked(0, 2));
        Draw(loaded, 1, 2, Green, frame: 1);
        Assert.Equal(Green, At(loaded, 3, 1, 2));
        Assert.Equal([0, 5], loaded.LinkGroups(0).Select(g => g.Color));
    }

    [Fact]
    public void DuplicateLayer_CopyKeepsLinksAmongItsOwnCels()
    {
        var p = Frames(3);
        Link(p, 0, 1);
        p.DuplicateLayer(0);
        Assert.True(p.AreCelsLinked(1, 0, 1));
        Draw(p, 2, 1, Green, layer: 1, frame: 0);
        Assert.Equal(Green, At(p, 1, 2, 1, layer: 1));
        Assert.Equal(default, At(p, 1, 2, 1, layer: 0));
    }

    private static int[] Colors(SpriteProject p, int layer = 0) => [.. p.LinkGroups(layer).Select(g => g.Color)];

    [Fact]
    public void LinkColors_FollowLinkingOrder_ReuseFreedColor_JoinKeepsColor()
    {
        var p = Frames(8);
        for (int f = 1; f < 8; f++) Draw(p, f % 4, f / 4, Blue, frame: f);
        Link(p, 4, 5); // 첫 링크 = 파랑(0) — 프레임 차례와 관계없이 묶은 차례
        Link(p, 0, 1); // 둘째 = 주황(1)
        Assert.Equal([1, 0], Colors(p)); // 묶음은 첫 프레임 차례: [0,1] 주황, [4,5] 파랑
        Link(p, 2, 4); // 이미 있는 파랑 묶음에 들어감
        Assert.Equal([1, 0], Colors(p));
        Assert.True(p.AreCelsLinked(0, 2, 5));

        p.UnlinkCels([(0, 0), (0, 1)]); // 주황이 빔
        Link(p, 6, 7);
        Assert.Equal([0, 1], Colors(p)); // 새 묶음은 비어 있는 첫 색(주황)
    }

    [Fact]
    public void SetLinkColor_IsUndoable_AndFollowsEdits()
    {
        var p = Frames(3);
        Link(p, 0, 1, 2);
        Assert.True(p.SetLinkColor(0, 1, 3));
        Assert.Equal([3], Colors(p));
        Draw(p, 3, 3, Green, frame: 2); // 그 자리 그리기
        p.ShiftCels("옮기기", [(0, 0)], 0, 1); // 새 그림으로 바뀜
        p.FlipCanvasHorizontal();
        Assert.Equal([3], Colors(p));
        p.Undo();
        p.Undo();
        p.Undo();
        p.Undo(); // 색 바꾸기
        Assert.Equal([0], Colors(p));
        p.Redo();
        Assert.Equal([3], Colors(p));
    }

    [Fact]
    public void EraseCels_ClearsLinkedDrawing_KeepsLinkAndColor()
    {
        var p = Frames(4);
        Link(p, 0, 1, 2);
        p.SetLinkColor(0, 0, 2);
        Draw(p, 3, 3, Blue, frame: 3);
        Assert.Equal(2, p.EraseCels([(0, 1), (0, 3)]));
        Assert.All([0, 1, 2], f => Assert.True(p.IsCelEmpty(0, f)));
        Assert.True(p.AreCelsLinked(0, 0, 2));
        Assert.Equal([2], Colors(p));
        Assert.Null(p.GetCel(0, 3)); // 링크 아닌 칸은 비움
        Draw(p, 1, 1, Green, frame: 2);
        Assert.Equal(Green, At(p, 0, 1, 1)); // 지운 뒤에도 같이 그려짐
    }

    [Fact]
    public void ExtendFrames_EachSourceGetsItsOwnColor()
    {
        var p = Frames(2);
        Draw(p, 1, 1, Blue, frame: 1);
        p.ExtendFrames(0, 1, 4, FrameExtendMode.Stretch); // 0,0,1,1
        Assert.Equal([0, 1], Colors(p));
        p.Undo();
        Assert.Empty(Colors(p));
    }

    [Fact]
    public void DuplicateFrame_LinkedCelJoinsTheLink_UnlinkedCelIsCopied()
    {
        var p = Frames(3);
        p.AddLayer();
        Draw(p, 1, 1, Blue, layer: 1, frame: 2);
        Link(p, 0, 2);
        p.DuplicateFrame(2);
        Assert.True(p.AreCelsLinked(0, 0, 3)); // 링크된 칸은 링크가 늘어남(색도 그대로)
        Assert.Single(Colors(p));
        Assert.False(p.IsCelLinked(1, 3)); // 링크 아닌 칸은 사본
        Assert.Equal(Blue, At(p, 3, 1, 1, layer: 1));
    }

    /// <summary>프레임 번호와 다른 레이어("ground")가 그 프레임에 그림이 있는지를 (0,0)에 적는 움직이는 스크립트.</summary>
    private static ScriptEffect Probe(FrameRange range, bool holdEnd)
    {
        var hash = Chickxel.Core.Scripting.ScriptLibrary.Add("""
            effect({ name: "hold probe", animated: true, settings: [{ key: "g", type: "layer", default: "ground" }],
              render(img, ctx, s) { const g = ctx.layer("g"); img.data[0] = ctx.frame * 10; img.data[1] = g && g.data[3] > 0 ? 200 : 50; img.data[3] = 255; } });
            """);
        Chickxel.Core.Scripting.ScriptLibrary.SetTrust(hash, true);
        return new ScriptEffect { ScriptHash = hash, Frames = range, HoldEnd = holdEnd };
    }

    [Fact]
    public void HoldEnd_LinkedCelAfterRange_IsExactlyTheLastRangeFrame()
    {
        // 바닥(ground)은 프레임 0~1에만 있고, 공(ball)은 0~3이 한 그림. 효과는 0~1 → 2(링크)는 1과 똑같이, 3(링크 아님)은 효과 없음
        var p = new SpriteProject(4, 4);
        for (int i = 0; i < 3; i++) p.AddFrame();
        p.SetLayerProperties(0, name: "ground");
        for (int f = 0; f < 2; f++) Draw(p, 0, 0, Green, layer: 0, frame: f);
        p.AddLayer(name: "ball");
        Draw(p, 2, 2, Red, layer: 1, frame: 0);
        p.LinkCels([(1, 0), (1, 1), (1, 2)]);
        p.PlaceCel(1, 3, p.GetCel(1, 0)!.Clone());
        p.SetLayerEffects(1, [Probe(new FrameRange(0, 1), holdEnd: true)]);

        PixelImage Cel(int f) => p.PlanCel(1, f)!.Render(new RenderPlanOptions());
        Assert.Equal(new Color32(10, 200, 0), Cel(1)[0, 0]);
        Assert.True(Cel(1).ContentEquals(Cel(2))); // 프레임 번호·다른 레이어도 끝 프레임 그대로
        Assert.True(p.Flatten(1).ContentEquals(p.Flatten(2)));
        Assert.Equal(Red, Cel(3)[2, 2]);
        Assert.Equal(default, Cel(3)[0, 0]);

        // 저장해도 남고, 끄면 원래 그림
        using var stream = new MemoryStream();
        ProjectIO.Save(p, stream);
        stream.Position = 0;
        var loaded = ProjectIO.Load(stream);
        Assert.True(((AnimatedEffect)loaded.Layers[1].Effects[0]).HoldEnd);
        Assert.True(loaded.Flatten(1).ContentEquals(loaded.Flatten(2)));
        p.SetLayerEffects(1, [Probe(new FrameRange(0, 1), holdEnd: false)]);
        Assert.Equal(default, Cel(2)[0, 0]);
    }

    /// <summary>
    /// 프레임 5개, 레이어 0의 프레임 <paramref name="linked"/>를 가로 막대(빨강 (0,0)·(1,0)·(2,0)) 그림 하나로 링크하고 끝 모습을 이어 가는 90° 돌리기 효과(범위 <paramref name="range"/>).
    /// </summary>
    private static SpriteProject HeldRotate(FrameRange range, bool enabled, params int[] linked)
    {
        var p = Frames(5);
        var bar = p.GetCel(0, 0)!.Clone();
        bar.Set(1, 0, Red);
        bar.Set(2, 0, Red);
        p.SetCel(0, linked[0], bar);
        Link(p, linked);
        p.SetLayerEffects(0, [new RotateEffect { Angle = 90, Anchor = SquashAnchor.Middle, Frames = range, HoldEnd = true, Enabled = enabled }]);
        return p;
    }

    private static PixelImage Shown(SpriteProject p, int frame) => p.PlanCel(0, frame)!.Render(new RenderPlanOptions());

    [Fact]
    public void UnlinkCels_HeldEndLook_IsBakedAsShown()
    {
        // 0~2가 한 그림, 효과 0~1 → 2는 링크라 1의 끝 모습. 2를 풀면 그 모습이 그림에 들어가 화면이 그대로
        var p = HeldRotate(new FrameRange(0, 1), enabled: true, 0, 1, 2);
        var raw = p.GetCel(0, 2)!.Clone();
        var shown = Shown(p, 2);
        var flat = p.Flatten(2);
        Assert.False(shown.ContentEquals(raw));

        Assert.Equal(new UnlinkResult(1, 1), p.UnlinkCels([(0, 2)]));
        Assert.True(p.GetCel(0, 2)!.ContentEquals(shown));
        Assert.True(p.Flatten(2).ContentEquals(flat));
        Assert.True(p.AreCelsLinked(0, 0, 1));
        Assert.True(p.GetCel(0, 1)!.ContentEquals(raw));

        p.Undo();
        Assert.True(p.AreCelsLinked(0, 1, 2));
        Assert.True(p.GetCel(0, 2)!.ContentEquals(raw));
    }

    [Fact]
    public void UnlinkCels_RangeEndCel_RemainingCelsKeepHeldLook()
    {
        // 1~3이 한 그림, 효과 0~1: 범위 끝 1을 떼면 2·3이 끝 모습을 잃으므로 남은 묶음 그림을 그 모습으로(링크는 남음)
        var p = HeldRotate(new FrameRange(0, 1), enabled: true, 1, 2, 3);
        var flats = Enumerable.Range(0, 5).Select(f => p.Flatten(f)).ToList();
        Assert.Equal(new UnlinkResult(1, 2), p.UnlinkCels([(0, 1)]));
        Assert.True(p.AreCelsLinked(0, 2, 3));
        Assert.False(p.IsCelLinked(0, 1));
        Assert.All(Enumerable.Range(0, 5), f => Assert.True(p.Flatten(f).ContentEquals(flats[f])));

        // 남은 칸 중 범위 안(0)과 밖(2)이 섞이면 밖의 칸만 떼어 그 모습으로
        var q = HeldRotate(new FrameRange(0, 1), enabled: true, 0, 1, 2);
        flats = Enumerable.Range(0, 5).Select(f => q.Flatten(f)).ToList();
        Assert.Equal(new UnlinkResult(2, 1), q.UnlinkCels([(0, 1)]));
        Assert.All([0, 1, 2], f => Assert.False(q.IsCelLinked(0, f)));
        Assert.All(Enumerable.Range(0, 5), f => Assert.True(q.Flatten(f).ContentEquals(flats[f])));
    }

    [Fact]
    public void UnlinkCels_DisabledHoldingEffect_IsRawCopy()
    {
        var p = HeldRotate(new FrameRange(0, 1), enabled: false, 0, 1, 2);
        var raw = p.GetCel(0, 2)!.Clone();
        Assert.Equal(new UnlinkResult(1, 0), p.UnlinkCels([(0, 2)]));
        Assert.True(p.GetCel(0, 2)!.ContentEquals(raw));
    }

    [Fact]
    public void UnlinkCels_CelInsideEffectRange_IsRawCopy()
    {
        // 2가 범위 안: 효과가 계속 입혀지므로 그림에 넣으면 두 번 입혀진다
        var p = HeldRotate(new FrameRange(0, 2), enabled: true, 0, 1, 2);
        var raw = p.GetCel(0, 2)!.Clone();
        var flat = p.Flatten(2);
        Assert.Equal(new UnlinkResult(1, 0), p.UnlinkCels([(0, 2)]));
        Assert.True(p.GetCel(0, 2)!.ContentEquals(raw));
        Assert.True(p.Flatten(2).ContentEquals(flat));
    }

    [Fact]
    public void LinkCels_EmptyNonBaseSkin_HasNothingToLink_BaseUntouched()
    {
        var p = Frames(3);
        Draw(p, 1, 1, Green, frame: 1);
        var layer = p.Layers[0];
        var g = p.AddSkinToLayer(layer, copy: false); // 새 빈 스킨이 지금 스킨: 모든 칸이 빈 칸(첫 스킨 그림을 빌리지 않음)
        int li = p.IndexOf(layer);
        var baseCels = Enumerable.Range(0, 3).Select(f => p.GetOwnCel(layer, p.Frames[f], g.BaseSkin!)).ToList();
        Assert.True(p.IsCelEmpty(li, 1));

        // 이 스킨 자신의 그림이 없으므로 링크할 것이 없다(기록 없음)
        int undo = p.History.UndoCount;
        Assert.Equal(0, p.LinkCels([(li, 0), (li, 1), (li, 2)], mainFrame: 1));
        Assert.Equal(undo, p.History.UndoCount);
        Assert.All([0, 1, 2], f => Assert.False(p.IsCelLinked(li, f)));
        // 첫 스킨 그림은 그대로(링크도 없음)
        for (int f = 0; f < 3; f++) Assert.Same(baseCels[f], p.GetOwnCel(layer, p.Frames[f], g.BaseSkin!));
    }

    [Fact]
    public void AddSkin_NewSkinStartsWithSourceLinks_DrawingFollowsLinks_BaseUntouched()
    {
        var p = Frames(4);
        Link(p, 0, 1, 2);
        int color = p.LinkMarks(0)[0].Color;
        var layer = p.Layers[0];
        var g = p.AddSkinToLayer(layer, copy: false);
        int li = p.IndexOf(layer);
        var baseSkin = g.BaseSkin!;

        // 빈 새 스킨: 첫 스킨과 같은 프레임끼리 같은 색으로 링크, 그림은 빈 칸
        Assert.All([0, 1, 2], f => Assert.True(p.IsCelLinked(li, f)));
        Assert.False(p.IsCelLinked(li, 3));
        Assert.Equal(color, p.LinkMarks(li)[1].Color);
        Assert.All([0, 1, 2], f => Assert.True(p.IsCelEmpty(li, f)));

        // 링크된 칸에 그리면 이 스킨의 묶음이 함께 바뀌고 첫 스킨은 그대로
        Draw(p, 0, 0, Green, layer: li, frame: 1); // 부위는 빨간 점 하나 크기
        Assert.All([0, 2], f => Assert.Equal(Green, p.GetCel(layer, p.Frames[f])![0, 0]));
        Assert.All([0, 1, 2], f => Assert.Equal(Red, p.GetOwnCel(layer, p.Frames[f], baseSkin)![0, 0]));

        // 복제한 스킨·부위 전용 빈 스킨도 원본 링크(색 포함)를 그대로
        var copy = p.AddSkin(g, null, g.ActiveSkin);
        Assert.Same(p.GetOwnCel(layer, p.Frames[0], copy), p.GetOwnCel(layer, p.Frames[2], copy));
        Assert.NotSame(p.GetOwnCel(layer, p.Frames[0], copy), p.GetOwnCel(layer, p.Frames[0], g.Skins[1]));
        Assert.Equal(color, p.LinkMarks(li)[0].Color);
        p.AddPartSkin(layer, "hat");
        Assert.True(p.AreCelsLinked(li, 0, 2));
        Assert.False(p.IsCelLinked(li, 3));
    }

    [Fact]
    public void LinkCels_GroupRow_LinksEveryPartFreshly_GroupWinsOverPartLinks_MarksAndUnlink()
    {
        var p = new SpriteProject(8, 8);
        for (int i = 1; i < 4; i++) p.AddFrame();
        var g = p.AddGroup("G");
        var a = p.AddPart(g, "A", 2, 2, new Point(0, 0));
        var b = p.AddPart(g, "B", 2, 2, new Point(4, 4));
        int ai = p.IndexOf(a), bi = p.IndexOf(b), gi = p.IndexOf(g);
        Draw(p, 0, 0, Red, layer: ai, frame: 1);
        Draw(p, 1, 1, Blue, layer: bi, frame: 1);
        // B만 따로 1·3을 링크해 둠(프레임 1 그림을 같이 씀)
        p.LinkCels([(bi, 1), (bi, 3)], mainFrame: 1);
        Assert.False(p.IsCelLinked(gi, 1));

        // 그룹 행 0~2: 부위마다 누른 칸(1)의 그림으로 새 묶음, B의 프레임 1은 3과의 링크에서 빠짐(그룹이 먼저)
        Assert.Equal(6, p.LinkCels([(gi, 0), (gi, 1), (gi, 2)], mainFrame: 1));
        Assert.All([0, 1, 2], f => Assert.True(p.AreCelsLinked(ai, 1, f) || f == 1));
        Assert.All([0, 2], f => Assert.True(p.AreCelsLinked(bi, 1, f)));
        Assert.False(p.AreCelsLinked(bi, 1, 3));
        Assert.False(p.IsCelLinked(bi, 3)); // 혼자 남은 프레임 3
        Assert.Equal(Red, At(p, 2, 0, 0, ai));
        Assert.Equal(Blue, At(p, 0, 1, 1, bi));
        Assert.Equal(Blue, At(p, 3, 1, 1, bi)); // 프레임 3은 원래 그림 그대로
        // 그룹 행 표시: 0~2가 한 묶음(부위들이 같은 색)
        var marks = p.LinkMarks(gi);
        Assert.All([0, 1, 2], f => Assert.True(marks[f].IsLinked));
        Assert.False(marks[3].IsLinked);
        Assert.Equal(p.LinkMarks(ai)[0].Color, p.LinkMarks(bi)[0].Color);
        Assert.Equal(marks[0].Color, p.LinkMarks(ai)[0].Color);

        // 부위 하나만 따로 고치면 그 프레임은 그룹 묶음이 아님
        p.UnlinkCels([(ai, 2)]);
        Assert.False(p.LinkMarks(gi)[2].IsLinked);
        Assert.True(p.LinkMarks(gi)[0].IsLinked);

        // 그룹 행 풀기: 모든 부위의 그 프레임
        Assert.True(p.UnlinkCels([(gi, 0)]).Unlinked >= 2);
        Assert.False(p.IsCelLinked(ai, 0));
        Assert.False(p.IsCelLinked(bi, 0));
    }

    /// <summary>프레임 <paramref name="frames"/>개: 0·1 = 파랑 묶음(빨간 점), 2·3 = 주황 묶음(초록 점).</summary>
    private static SpriteProject BlueOrange(int frames)
    {
        var p = Frames(frames);
        Draw(p, 1, 1, Green, frame: 2);
        Assert.Equal(1, Link(p, 0, 1));
        Assert.Equal(1, Link(p, 2, 3));
        Assert.Equal(0, p.LinkMarks(0)[0].Color); // 파랑
        Assert.Equal(1, p.LinkMarks(0)[2].Color); // 주황
        return p;
    }

    [Fact]
    public void PasteCels_SameRow_JoinsSourceGroups_UndoOneStep()
    {
        var p = BlueOrange(6);
        var copies = p.CopyCels(p.Layers[0], [0, 2]);
        Assert.True(copies[0]!.IsLinked);
        Assert.Equal(1, copies[1]!.LinkColor);
        int steps = p.History.UndoCount;
        p.PasteCels([(0, 4, copies[0]), (0, 5, copies[1])]);
        Assert.Equal(steps + 1, p.History.UndoCount);
        Assert.Equal([0, 1, 4], p.LinkedFrames(0, 4));
        Assert.Equal([2, 3, 5], p.LinkedFrames(0, 5));
        var marks = p.LinkMarks(0);
        Assert.Equal(0, marks[4].Color);
        Assert.Equal(1, marks[5].Color);

        // 붙인 칸을 고치면 원래 묶음도 함께 바뀐다
        Draw(p, 3, 3, Blue, frame: 4);
        Assert.Equal(Blue, At(p, 0, 3, 3));
        p.Undo(); // 그리기
        p.Undo(); // 붙여넣기
        Assert.True(p.IsCelEmpty(0, 4));
        Assert.True(p.IsCelEmpty(0, 5));
        Assert.Equal([0, 1], p.LinkedFrames(0, 0));
    }

    [Fact]
    public void PasteCels_IntoLinkedCel_LeavesItsGroupAndJoinsSource()
    {
        var p = BlueOrange(4);
        var copies = p.CopyCels(p.Layers[0], [0]);
        p.PasteCels([(0, 3, copies[0])]);
        Assert.Equal([0, 1, 3], p.LinkedFrames(0, 3));
        Assert.False(p.IsCelLinked(0, 2)); // 주황 묶음은 혼자 남음
        Assert.Equal(Green, At(p, 2, 1, 1));
    }

    [Fact]
    public void PasteCels_OtherDocumentOrRow_RecreatesGroupsAndColors()
    {
        var p = BlueOrange(5);
        var copies = p.CopyCels(p.Layers[0], [0, 1, 2, 3]);

        // 다른 문서: 같은 묶음에서 온 칸끼리 새 묶음(색 그대로), 원래 문서와는 따로
        var q = new SpriteProject(4, 4);
        for (int i = 1; i < 4; i++) q.AddFrame();
        q.PasteCels([.. Enumerable.Range(0, 4).Select(f => (0, f, copies[f]))]);
        Assert.Equal([0, 1], q.LinkedFrames(0, 0));
        Assert.Equal([2, 3], q.LinkedFrames(0, 3));
        Assert.Equal(0, q.LinkMarks(0)[1].Color);
        Assert.Equal(1, q.LinkMarks(0)[2].Color);
        Assert.Equal(Red, At(q, 1, 0, 0));
        Assert.Equal(Green, At(q, 3, 1, 1));
        Draw(q, 3, 3, Blue, frame: 0);
        Assert.Equal(Blue, At(q, 1, 3, 3));
        Assert.Equal(default, At(p, 0, 3, 3));

        // 다른 행(같은 문서): 새 묶음, 그 행에서 이미 쓰는 색이면 쓰지 않은 첫 색. 한 칸만 오면 링크 없음
        p.AddLayer();
        Draw(p, 2, 2, Blue, layer: 1, frame: 2);
        Assert.Equal(1, p.LinkCels([(1, 2), (1, 3)])); // 이 행의 파랑
        p.PasteCels([(1, 0, copies[0]), (1, 1, copies[1]), (1, 4, copies[2])]);
        Assert.Equal([0, 1], p.LinkedFrames(1, 0));
        Assert.Equal(0, p.LinkMarks(1)[3].Color);
        Assert.Equal(1, p.LinkMarks(1)[0].Color); // 파랑은 이 행에서 쓰므로 주황
        Assert.False(p.IsCelLinked(1, 4));
        Assert.Equal(Green, At(p, 4, 1, 1, layer: 1));
        Assert.Equal([0, 1], p.LinkedFrames(0, 0)); // 원래 행 그대로
    }

    [Fact]
    public void PasteCels_ImagesOnly_PastesUnlinkedCopies()
    {
        var p = BlueOrange(6);
        var copies = p.CopyCels(p.Layers[0], [0, 1], links: false);
        Assert.All(copies, c => Assert.False(c!.IsLinked));
        p.PasteCels([(0, 4, copies[0]), (0, 5, copies[1])]);
        Assert.False(p.IsCelLinked(0, 4));
        Assert.False(p.IsCelLinked(0, 5));
        Assert.Equal(Red, At(p, 4, 0, 0));
        Draw(p, 3, 3, Blue, frame: 4);
        Assert.Equal(default, At(p, 0, 3, 3));
        Assert.Equal(default, At(p, 5, 3, 3));
    }
}
