using Chickxel.Core.Community;
using Chickxel.Core.IO;
using Chickxel.Core.Model;
using Xunit;

namespace Chickxel.Core.Tests;

public class GalleryFramesTests
{
    /// <summary>1×1 프레임 <paramref name="frames"/>개(프레임마다 다른 색, 길이 100 + 10 × 번호).</summary>
    private static SpriteProject Project(int frames)
    {
        var p = new SpriteProject(1, 1);
        for (int i = 1; i < frames; i++) p.AddFrame();
        for (int f = 0; f < frames; f++)
        {
            p.BeginCelStroke(0, f).Set(0, 0, new Color32((byte)(f * 20), 0, 0));
            Assert.True(p.CommitCelStroke("점"));
            p.SetFrameDuration(f, 100 + 10 * f);
        }
        return p;
    }

    [Fact]
    public void All_IsEveryFrameInOrder()
    {
        Assert.Equal([0, 1, 2, 3], GalleryFrames.Select(Project(4), GalleryFrameMode.All));
        Assert.Equal([0], GalleryFrames.Select(Project(1), GalleryFrameMode.All));
    }

    [Theory]
    [InlineData(TagDirection.Forward, new[] { 1, 2, 3 })]
    [InlineData(TagDirection.Reverse, new[] { 3, 2, 1 })]
    [InlineData(TagDirection.PingPong, new[] { 1, 2, 3, 2 })]
    public void Tag_FollowsThePlaybackOrder(TagDirection direction, int[] expected)
    {
        var p = Project(5);
        var tag = p.AddTag("walk", 1, 3, direction);
        Assert.Equal(expected, GalleryFrames.Select(p, GalleryFrameMode.Tag, tag));
    }

    [Fact]
    public void Tag_MissingTagGivesNothing_AndFramesPastTheEndAreDropped()
    {
        var p = Project(3);
        // 1..5 왕복(1 2 3 4 5 4 3 2)에서 없는 3·4번(0부터)은 빠진다
        var stale = new AnimationTag("old", 1, 5) { Direction = TagDirection.PingPong };
        Assert.Equal([1, 2, 2], GalleryFrames.Select(p, GalleryFrameMode.Tag, stale));
        Assert.Empty(GalleryFrames.Select(p, GalleryFrameMode.Tag, tag: null));
    }

    [Fact]
    public void Range_IsOneBased_ReversedPlaysBackwards_AndIsClampedToTheProject()
    {
        var p = Project(6);
        Assert.Equal([1, 2, 3], GalleryFrames.Select(p, GalleryFrameMode.Range, from: 2, to: 4));
        Assert.Equal([4, 3, 2, 1], GalleryFrames.Select(p, GalleryFrameMode.Range, from: 5, to: 2));
        Assert.Equal([2], GalleryFrames.Select(p, GalleryFrameMode.Range, from: 3, to: 3));
        Assert.Equal([0, 1, 2, 3, 4, 5], GalleryFrames.Select(p, GalleryFrameMode.Range, from: -4, to: 99));
        Assert.Equal([5, 4], GalleryFrames.Select(p, GalleryFrameMode.Range, from: 50, to: 5));
    }

    [Fact]
    public void Selected_IsSortedDistinctAndInRange()
    {
        var p = Project(5);
        Assert.Equal([0, 2, 4], GalleryFrames.Select(p, GalleryFrameMode.Selected, selected: [4, 2, 2, 0, 9, -1]));
        Assert.Empty(GalleryFrames.Select(p, GalleryFrameMode.Selected));
    }

    [Fact]
    public void ProjectFramesAt_ComposesTheChosenOrderWithDurations()
    {
        var p = Project(4);
        var order = GalleryFrames.Select(p, GalleryFrameMode.Range, from: 4, to: 2);
        var frames = GifWriter.ProjectFramesAt(p, order);
        Assert.Equal([60, 40, 20], frames.Select(f => (int)f.Image[0, 0].R));
        Assert.Equal([130, 120, 110], frames.Select(f => f.DurationMs));
    }
}
