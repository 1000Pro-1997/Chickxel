using Gpixel.Core.Model;

namespace Gpixel.Core.Tests;

public class FrameExtendTests
{
    /// <summary>4프레임, 두 레이어: 프레임 f의 (0,0) = 색 f+1(레이어 0)·f+11(레이어 1), 길이 = (f+1)×10ms.</summary>
    private static SpriteProject FourFrames()
    {
        var p = new SpriteProject(2, 2);
        p.AddLayer();
        for (int i = 0; i < 3; i++) p.AddFrame();
        for (int f = 0; f < 4; f++)
        {
            for (int layer = 0; layer < 2; layer++)
            {
                var cel = p.BeginCelStroke(layer, f);
                cel.Set(0, 0, Mark(layer, f));
                p.CommitCelStroke("점");
            }
            p.SetFrameDuration(f, (f + 1) * 10);
        }
        return p;
    }

    private static Color32 Mark(int layer, int frame) => new((byte)(layer * 10 + frame + 1), 0, 0);

    /// <summary>프레임마다 어느 원래 프레임의 그림인지(레이어 0 표시 색 기준, 두 레이어·길이가 같은 원래 프레임인지도 확인).</summary>
    private static int[] Sources(SpriteProject p) => Enumerable.Range(0, p.Frames.Count).Select(f =>
    {
        int s = p.GetCel(0, f)!.Get(0, 0).R - 1;
        Assert.Equal(Mark(1, s), p.GetCel(1, f)!.Get(0, 0));
        Assert.Equal((s + 1) * 10, p.Frames[f].DurationMs);
        return s;
    }).ToArray();

    [Theory]
    [InlineData(FrameExtendMode.Stretch, new[] { 0, 0, 0, 1, 1, 2, 2, 2, 3, 3 })]
    [InlineData(FrameExtendMode.HoldFirst, new[] { 0, 0, 0, 0, 0, 0, 0, 1, 2, 3 })]
    [InlineData(FrameExtendMode.HoldLast, new[] { 0, 1, 2, 3, 3, 3, 3, 3, 3, 3 })]
    [InlineData(FrameExtendMode.Loop, new[] { 0, 1, 2, 3, 0, 1, 2, 3, 0, 1 })]
    public void ExtendFrames_FillsByMode(FrameExtendMode mode, int[] expected)
    {
        var p = FourFrames();
        var originals = p.Frames.ToArray();
        p.ExtendFrames(0, 3, 10, mode);
        Assert.Equal(expected, Sources(p));
        // 원래 프레임 객체는 남고(처음 오는 자리), 사본은 새 프레임
        Assert.Equal(10, p.Frames.Distinct().Count());
        Assert.All(originals, f => Assert.Contains(f, p.Frames));
        Assert.Equal(expected, new FrameExtension(0, 3, 10, mode).Sequence());
    }

    [Fact]
    public void ExtendFrames_StretchDoubling_RepeatsEachFrameTwice()
    {
        var p = FourFrames();
        p.ExtendFrames(0, 3, 8, FrameExtendMode.Stretch);
        Assert.Equal([0, 0, 1, 1, 2, 2, 3, 3], Sources(p));
    }

    [Fact]
    public void ExtendFrames_CopiesAreDeep()
    {
        var p = FourFrames();
        p.ExtendFrames(1, 1, 3, FrameExtendMode.HoldLast); // 프레임 2를 세 번
        Assert.Equal([0, 1, 1, 1, 2, 3], Sources(p));
        Assert.NotSame(p.GetCel(0, 1), p.GetCel(0, 2));

        var cel = p.BeginCelStroke(0, 2);
        cel.Set(1, 1, new Color32(0, 255, 0));
        p.CommitCelStroke("점");
        Assert.Equal(Color32.Transparent, p.GetCel(0, 1)!.Get(1, 1));
        Assert.Equal(Color32.Transparent, p.GetCel(0, 3)!.Get(1, 1));
    }

    [Fact]
    public void ExtendFrames_CopiesPivotNotName()
    {
        var p = FourFrames();
        p.SetFramePivot(3, new Pivot(0.25f, 0.75f));
        p.SetFrameName(3, "last");
        p.ExtendFrames(2, 3, 4, FrameExtendMode.HoldLast);
        Assert.Equal([0, 1, 2, 3, 3, 3], Sources(p));
        Assert.Equal("last", p.Frames[3].Name);
        Assert.Null(p.Frames[4].Name); // 사본은 이름 규칙(내보낼 이름이 겹치지 않게)
        Assert.Equal(p.Frames[3].Pivot, p.Frames[5].Pivot);
    }

    [Fact]
    public void ExtendFrames_MovesTagsAndEffectRanges()
    {
        var p = FourFrames();
        p.AddFrame(); // 5프레임: 범위 1~2 뒤에 프레임 3·4
        var cover = p.AddTag("cover", 0, 3);
        var after = p.AddTag("after", 3, 4);
        var before = p.AddTag("before", 0, 0);
        var inside = p.AddTag("inside", 1, 1);
        p.SetLayerEffects(1,
        [
            new OutlineEffect { Frames = new FrameRange(1, 2) }, // 범위 그대로 → 늘어남
            new OutlineEffect { Frames = new FrameRange(3, 4) }, // 뒤 → 밀림
            new OutlineEffect { Frames = new FrameRange(0, 0) }, // 앞 → 그대로
            new OutlineEffect(), // 모든 프레임 → 그대로
        ]);

        p.ExtendFrames(1, 2, 6, FrameExtendMode.Stretch); // +4: 1,1,1,2,2,2

        Assert.Equal(9, p.Frames.Count);
        Assert.Equal((0, 7), (cover.From, cover.To));
        Assert.Equal((7, 8), (after.From, after.To));
        Assert.Equal((0, 0), (before.From, before.To));
        Assert.Equal((1, 3), (inside.From, inside.To)); // 프레임 2가 차지한 자리
        Assert.Equal<FrameRange?[]>([new FrameRange(1, 6), new FrameRange(7, 8), new FrameRange(0, 0), null],
            p.Layers[1].Effects.Select(e => e.Frames).ToArray());
    }

    [Fact]
    public void ExtendFrames_IsOneUndoStep()
    {
        var p = FourFrames();
        var frames = p.Frames.ToArray();
        var tag = p.AddTag("all", 0, 3);
        p.SetLayerEffects(0, [new OutlineEffect { Frames = new FrameRange(2, 3) }]);

        p.ExtendFrames(0, 3, 12, FrameExtendMode.Loop);
        Assert.Equal(12, p.Frames.Count);
        Assert.Equal((0, 11), (tag.From, tag.To));

        p.Undo();
        Assert.Equal(frames, p.Frames);
        Assert.Equal([0, 1, 2, 3], Sources(p));
        Assert.Equal((0, 3), (tag.From, tag.To));
        Assert.Equal(new FrameRange(2, 3), p.Layers[0].Effects[0].Frames);
        p.Redo();
        Assert.Equal([0, 1, 2, 3, 0, 1, 2, 3, 0, 1, 2, 3], Sources(p));
    }

    [Fact]
    public void ExtendFrames_RejectsShrinkAndBadRange_SameCountIsNoOp()
    {
        var p = FourFrames();
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ExtendFrames(0, 3, 3, FrameExtendMode.Stretch));
        Assert.Throws<ArgumentOutOfRangeException>(() => p.ExtendFrames(2, 4, 6, FrameExtendMode.Stretch));
        int before = p.History.UndoCount;
        p.ExtendFrames(1, 2, 2, FrameExtendMode.Loop);
        Assert.Equal(4, p.Frames.Count);
        Assert.Equal(before, p.History.UndoCount);
    }

    [Theory]
    [InlineData(FrameExtendMode.Stretch)]
    [InlineData(FrameExtendMode.HoldFirst)]
    [InlineData(FrameExtendMode.HoldLast)]
    [InlineData(FrameExtendMode.Loop)]
    public void FrameExtension_MapsFramesBothWays(FrameExtendMode mode)
    {
        var x = new FrameExtension(2, 4, 7, mode); // 앞 2, 범위 3 → 7, 뒤 1
        var seq = x.Sequence();
        for (int f = 0; f < 10; f++)
        {
            int orig = x.OriginalFrame(f);
            Assert.Equal(f < 2 ? f : f >= 9 ? f - 4 : 2 + seq[f - 2], orig);
            Assert.Equal(x.ExtendedFrame(orig) != f, x.IsCopy(f));
        }
        Assert.Equal(new FrameRange(2, 8), x.MapRange(new FrameRange(2, 4)));
        Assert.Equal(new FrameRange(9, 9), x.MapRange(new FrameRange(5, 5)));
        Assert.Equal(4, Enumerable.Range(0, 10).Count(x.IsCopy));
    }
}
