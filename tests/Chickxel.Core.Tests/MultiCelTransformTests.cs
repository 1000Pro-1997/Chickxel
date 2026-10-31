using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Model;

namespace Chickxel.Core.Tests;

/// <summary>여러 셀(타임라인 셀 묶음)에 같은 자유 변형을 한 번에 입히기(<see cref="MultiCelTransform"/>).</summary>
public class MultiCelTransformTests
{
    private static readonly Color32 Red = new(255, 0, 0);
    private static readonly Color32 Blue = new(0, 0, 255);

    private static void Paint(SpriteProject p, int layer, int frame, int x, int y, Color32 c)
    {
        p.BeginCelStroke(layer, frame).Set(x, y, c);
        p.CommitCelStroke("칠");
    }

    /// <summary>프레임 f마다 (f,1)부터 빨간 3칸 줄 + 그 아래 (f,2)에 파란 점(방향을 알 수 있게).</summary>
    private static SpriteProject Walk(int frames)
    {
        var p = new SpriteProject(16, 16);
        p.History.Capacity = 100;
        for (int f = 1; f < frames; f++) p.AddFrame();
        for (int f = 0; f < frames; f++)
        {
            for (int x = 0; x < 3; x++) Paint(p, 0, f, f + x, 1, Red);
            Paint(p, 0, f, f, 2, Blue);
        }
        return p;
    }

    /// <summary>앱처럼 모든 원본을 감싸는 사각형을 손잡이 기준으로 쓴다.</summary>
    private static FreeTransform Box(Rectangle union) => new(new PixelImage(union.Width, union.Height), new PixelMask(union));

    /// <summary>
    /// 기대 결과: 셀의 칠한 픽셀마다 중심을 기준 공간으로 옮겨(+offset) 기준 변형으로 보낸 뒤 셀 공간으로 되돌린 칸(90° 배수·픽셀 격자에 맞는 값에서만 정확).
    /// </summary>
    private static PixelImage Expected(PixelImage cel, FreeTransform box, TransformParams p, Point offset)
    {
        var result = new PixelImage(cel.Width, cel.Height);
        for (int y = 0; y < cel.Height; y++)
            for (int x = 0; x < cel.Width; x++)
            {
                var c = cel.Get(x, y);
                if (c.A == 0) continue;
                var (mx, my) = box.Map(p, x + offset.X + 0.5, y + offset.Y + 0.5);
                result.Set((int)Math.Floor(mx) - offset.X, (int)Math.Floor(my) - offset.Y, c);
            }
        return result;
    }

    [Fact]
    public void SameMatrix_AppliedToEveryCel_AsOneUndoStep()
    {
        var p = Walk(6);
        var originals = Enumerable.Range(0, 6).Select(f => p.GetCel(0, f)!.Clone()).ToList();
        int historyBefore = p.History.UndoCount;
        var multi = MultiCelTransform.Create(p, 0, Enumerable.Range(0, 6).Select(f => (0, f)));
        Assert.Equal(6, multi.Targets.Count);
        Assert.Equal(0, multi.LockedSkipped);
        Assert.Equal(new Rectangle(0, 1, 8, 2), multi.Bounds);
        // 셀은 적용 전까지 그대로
        for (int f = 0; f < 6; f++) Assert.True(p.GetCel(0, f)!.ContentEquals(originals[f]));

        var box = Box(multi.Bounds);
        var tp = box.Identity with { CenterX = box.Identity.CenterX + 3, CenterY = box.Identity.CenterY + 4, Angle = 90 };
        Assert.Equal(6, multi.Commit("변형", box, tp, ResampleMode.RotSprite));

        for (int f = 0; f < 6; f++)
        {
            var got = p.GetCel(0, f)!;
            Assert.True(got.ContentEquals(Expected(originals[f], box, tp, Point.Empty)), $"frame {f}");
            Assert.Equal(4, got.Pixels.Count(c => c.A != 0));
        }
        // 같은 행렬: 프레임마다 x가 1씩 다른 그림은 돌린 뒤 y가 1씩 다르다(세로 줄로 섬).
        for (int f = 1; f < 6; f++)
        {
            var a = p.GetCel(0, f - 1)!.GetOpaqueBounds();
            var b = p.GetCel(0, f)!.GetOpaqueBounds();
            Assert.Equal(new Size(2, 3), b.Size);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y + 1, b.Y);
        }

        Assert.Equal(historyBefore + 1, p.History.UndoCount);
        Assert.True(p.Undo());
        for (int f = 0; f < 6; f++) Assert.True(p.GetCel(0, f)!.ContentEquals(originals[f]), $"undo frame {f}");
    }

    [Fact]
    public void LockedLayer_IsSkipped_AndEmptyCelsIgnored()
    {
        var p = Walk(3);
        p.AddLayer(name: "잠김");
        Paint(p, 1, 0, 5, 5, Blue);
        Paint(p, 1, 2, 6, 6, Blue);
        p.SetLayerProperties(1, locked: true);
        var lockedBefore = new[] { p.GetCel(1, 0)!.Clone(), p.GetCel(1, 2)!.Clone() };
        var cels = new List<(int, int)>();
        for (int l = 0; l < 2; l++)
            for (int f = 0; f < 3; f++) cels.Add((l, f));
        var multi = MultiCelTransform.Create(p, 0, cels);
        Assert.Equal(3, multi.Targets.Count);
        Assert.Equal(2, multi.LockedSkipped);
        Assert.All(multi.Targets, t => Assert.Same(p.Layers[0], t.Layer));

        var box = Box(multi.Bounds);
        Assert.Equal(3, multi.Commit("옮기기", box, box.Identity with { CenterX = box.Identity.CenterX + 2 }, ResampleMode.Nearest));
        Assert.True(p.GetCel(1, 0)!.ContentEquals(lockedBefore[0]));
        Assert.True(p.GetCel(1, 2)!.ContentEquals(lockedBefore[1]));
        for (int f = 0; f < 3; f++) Assert.Equal(new Rectangle(f + 2, 1, 3, 2), p.GetCel(0, f)!.GetOpaqueBounds());
    }

    [Fact]
    public void PartCels_TurnAboutTheSameCanvasPoint()
    {
        var p = new SpriteProject(16, 16);
        Paint(p, 0, 0, 5, 5, Red);
        var g = p.AddGroup();
        var part = p.AddPart(g, "Head", 6, 6, new Point(4, 4));
        int pi = p.IndexOf(part);
        Paint(p, pi, 0, 3, 2, Blue);   // 캔버스 (7,6)
        var before = (Normal: p.GetCel(0, 0)!.Clone(), Part: p.GetCel(pi, 0)!.Clone());

        var multi = MultiCelTransform.Create(p, 0, [(0, 0), (pi, 0)]);
        Assert.Equal(new Point(4, 4), multi.Targets.Single(t => ReferenceEquals(t.Layer, part)).Offset);
        Assert.Equal(new Rectangle(5, 5, 3, 2), multi.Bounds);
        var box = Box(multi.Bounds);
        var tp = box.Identity with { Angle = 180 };
        multi.Commit("돌리기", box, tp, ResampleMode.RotSprite);

        Assert.True(p.GetCel(0, 0)!.ContentEquals(Expected(before.Normal, box, tp, Point.Empty)));
        Assert.True(p.GetCel(pi, 0)!.ContentEquals(Expected(before.Part, box, tp, new Point(4, 4))));
        // 180° 회전: 캔버스 (5,5)와 (7,6)이 사각형 (5,5)-(8,7)의 반대쪽 끝으로 서로 자리를 바꾼다(부위는 부위 좌표 (1,1)).
        Assert.Equal(Red, p.GetCel(0, 0)!.Get(7, 6));
        Assert.Equal(Blue, p.GetCel(pi, 0)!.Get(1, 1));
    }

    [Fact]
    public void ParamsFor_MatchesReferenceMap()
    {
        var a = new FreeTransform(new PixelImage(4, 2), new PixelMask(new Rectangle(1, 1, 4, 2)));
        var b = new FreeTransform(new PixelImage(3, 3), new PixelMask(new Rectangle(7, 5, 3, 3)));
        var p = a.Identity with { CenterX = 10.5, CenterY = -2, Angle = 37, ScaleX = 1.5, ScaleY = -0.75 };
        var q = a.ParamsFor(b, p, 2, -1);
        foreach (var (x, y) in new[] { (7.0, 5.0), (10.0, 8.0), (8.5, 6.25) })
        {
            var viaA = a.Map(p, x + 2, y - 1);
            var viaB = b.Map(q, x, y);
            Assert.Equal(viaA.X - 2, viaB.X, 9);
            Assert.Equal(viaA.Y + 1, viaB.Y, 9);
        }
        Assert.Equal(p, a.ParamsFor(a, p));
    }
}
