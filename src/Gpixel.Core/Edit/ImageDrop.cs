using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.Edit;

/// <summary>캔버스에 끌어 넣은 그림 한 장: 레이어 이름, 프레임(모두 같은 크기 — 움직이는 GIF면 여러 장)과 프레임마다의 길이(ms).</summary>
public sealed record DroppedImage(string Name, IReadOnlyList<PixelImage> Frames, IReadOnlyList<int> DurationsMs)
{
    public Size Size => Frames[0].Size;
}

/// <summary>끌어 넣기 결과: 새 레이어들(넣은 순서 — 마지막이 맨 위), 맨 끝에 더한 프레임 수, 캔버스보다 커서 가운데 놓고 잘린 그림이 있었는지.</summary>
public sealed record ImageDropResult(IReadOnlyList<Layer> Layers, int AddedFrames, bool Oversized);

/// <summary>
/// 파일 끌어 넣기: 그림마다 새 일반 레이어를 현재 레이어(부위면 그 그룹, 그룹이면 그 그룹) 위에 넣고, 현재 프레임부터 그림 프레임을 차례로 넣는다.
/// 위치는 놓은 픽셀이 그림 가운데가 되게(정수 픽셀, 캔버스 밖은 잘림), 그림이 캔버스보다 크면(한 변이라도) 캔버스 가운데.
/// 프레임이 모자라면 맨 끝에 더하고 길이는 그림 프레임 길이를 쓴다. 모두 실행취소 한 단계.
/// </summary>
public static class ImageDrop
{
    /// <summary>
    /// 그림 왼쪽 위 위치(프레임 좌표). 그림이 캔버스보다 크면(한 변이라도) 캔버스 가운데에 놓고 <c>Oversized</c> = true,
    /// 아니면 <paramref name="drop"/> 픽셀(캔버스 밖이면 가장 가까운 가장자리 픽셀)이 가운데(짝수 변이면 가운데 둘 중 오른쪽·아래)가 되게 놓는다
    /// (캔버스 밖으로 나간 부분은 넣을 때 잘림).
    /// </summary>
    public static (Point Location, bool Oversized) Place(Size canvas, Size image, Point drop)
    {
        if (image.Width > canvas.Width || image.Height > canvas.Height)
            return (new Point(FloorHalf(canvas.Width - image.Width), FloorHalf(canvas.Height - image.Height)), true);
        int x = Math.Clamp(drop.X, 0, canvas.Width - 1), y = Math.Clamp(drop.Y, 0, canvas.Height - 1);
        return (new Point(x - image.Width / 2, y - image.Height / 2), false);
    }

    private static int FloorHalf(int v) => (int)Math.Floor(v / 2.0);

    /// <summary>
    /// 그림들을 새 레이어로 넣는다(실행취소 한 단계 <paramref name="label"/>). <paramref name="currentLayer"/> 위 자리는 레이어 추가와 같다
    /// (부위면 그 그룹 위 — 그룹 안에는 넣지 않음). 그림이 없으면 아무것도 하지 않는다.
    /// </summary>
    public static ImageDropResult Apply(SpriteProject p, IReadOnlyList<DroppedImage> images, int currentLayer, int currentFrame, Point drop, string label)
    {
        if (images.Count == 0) return new ImageDropResult([], 0, false);
        var layers = new List<Layer>(images.Count);
        int added = 0;
        bool oversized = false;
        p.Edit(label, ProjectChange.Layers | ProjectChange.Frames | ProjectChange.Tags | ProjectChange.Pixels, () =>
        {
            var current = p.Layers[Math.Clamp(currentLayer, 0, p.Layers.Count - 1)];
            int at = p.IndexOf(current.Parent ?? current) + 1;
            foreach (var image in images)
            {
                var layer = p.AddLayer(at, image.Name);
                layers.Add(layer);
                int index = p.IndexOf(layer);
                at = index + 1;
                var (loc, over) = Place(p.Size, image.Size, drop);
                oversized |= over;
                for (int i = 0; i < image.Frames.Count; i++)
                {
                    int f = currentFrame + i;
                    if (f >= p.Frames.Count)
                    {
                        p.AddFrame();
                        f = p.Frames.Count - 1;
                        p.SetFrameDuration(f, Math.Max(1, image.DurationsMs[i]));
                        added++;
                    }
                    var cel = new PixelImage(p.Width, p.Height);
                    cel.Blit(image.Frames[i], loc.X, loc.Y);
                    if (!cel.IsRegionEmpty(cel.Bounds)) p.SetCel(index, f, cel);
                }
            }
        });
        return new ImageDropResult(layers, added, oversized);
    }
}
