using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 캔버스 원근 변형(캔버스 크기의 꼭짓점 자유 이동): 원근 대응 하나(원래 캔버스 좌표 → 새 캔버스 좌표)로 프로젝트 전체 그림을 옮긴다.
public sealed partial class SpriteProject
{
    /// <summary>
    /// 캔버스를 <paramref name="width"/>×<paramref name="height"/>로 바꾸고 원래 캔버스 전체를 <paramref name="corners"/>(새 캔버스 좌표, 왼쪽 위·오른쪽 위·오른쪽 아래·왼쪽 아래)로
    /// 원근 변형해 놓는다(실행취소 한 단계 = <see cref="DomainStrings.Project_CanvasSize"/>). 남는 자리는 투명(여백).
    /// <para>
    /// 대응은 프로젝트 전체에 하나(<see cref="ImageTransform.CanvasWarp"/>)다. 일반 레이어 셀은 캔버스 밖 그림과 합쳐 옮기고(<see cref="ImageTransform.WarpWithCanvas"/>)
    /// 새 캔버스 밖으로 나간 픽셀은 캔버스 밖 그림으로 남긴다(링크된 셀은 한 번만 계산해 같이 씀). 부위는 기준 영역 네 꼭짓점을 옮긴 사각형을 감싸는 정수 사각형(최소 1px)이
    /// 새 영역이 되고 모든 스킨의 셀을 같은 대응으로 그 안에 다시 그리며, 프레임마다 옮긴 거리는 옮긴 왼쪽 위끼리의 차이로, 9-slice 테두리는 없앤다.
    /// 캔버스 기준 피벗은 옮긴 점으로, 부위 피벗은 같은 그림 지점을 새 영역 기준으로 가리킨다. 프레임 칸 테두리(<see cref="SpriteSettings.Border"/>)도 없앤다.
    /// </para>
    /// 크기가 잘못되면 <see cref="ArgumentOutOfRangeException"/>, 꼭짓점이 넷이 아니거나 볼록한 사각형이 아니면 <see cref="ArgumentException"/>.
    /// 크기가 같고 꼭짓점이 원래 캔버스 그대로면 아무것도 하지 않는다.
    /// </summary>
    public void WarpCanvas(int width, int height, IReadOnlyList<(double X, double Y)> corners, ResampleMode mode)
    {
        ValidateSize(width, height);
        var quad = corners.ToArray();
        int oldW = _width, oldH = _height;
        var oldCanvas = new Size(oldW, oldH);
        if (ImageTransform.CanvasWarp(oldCanvas, quad) is not { } warp)
            throw new ArgumentException(DomainStrings.Canvas_NonConvexCorners, nameof(corners));
        if (width == oldW && height == oldH && quad.SequenceEqual(new (double X, double Y)[] { (0, 0), (oldW, 0), (oldW, oldH), (0, oldH) })) return;
        var (box, h) = warp;
        var newCanvas = new Size(width, height);
        var area = OverflowArea(newCanvas);

        // 원래 캔버스 좌표(연속) → 새 캔버스 좌표. 원근의 지평선 너머거나 유한하지 않으면 null.
        (double X, double Y)? Map(double x, double y)
        {
            if (!box.InFront(h, x, y)) return null;
            var m = box.Map(h, x, y);
            return double.IsFinite(m.X) && double.IsFinite(m.Y) ? m : null;
        }
        Point? MapRounded(double x, double y)
            => Map(x, y) is { } m && Math.Abs(m.X) < int.MaxValue / 2 && Math.Abs(m.Y) < int.MaxValue / 2
                ? new Point((int)Math.Floor(m.X + 0.5), (int)Math.Floor(m.Y + 0.5)) : null;
        // 원래 캔버스 좌표 사각형의 네 꼭짓점을 옮긴 사각형을 감싸는 정수 사각형(소수 오차 1e-6은 버림, 최소 1px, 캔버스 밖 그림을 남기는 범위 안).
        // 꼭짓점이 지평선 너머거나 범위 밖이면 null.
        Rectangle? Enclosing(Rectangle r)
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            foreach (var (cx, cy) in new[] { (r.Left, r.Top), (r.Right, r.Top), (r.Right, r.Bottom), (r.Left, r.Bottom) })
            {
                if (Map(cx, cy) is not { } m) return null;
                minX = Math.Min(minX, m.X);
                minY = Math.Min(minY, m.Y);
                maxX = Math.Max(maxX, m.X);
                maxY = Math.Max(maxY, m.Y);
            }
            const double eps = 1e-6;
            double left = Math.Floor(minX + eps), top = Math.Floor(minY + eps);
            double right = Math.Max(Math.Ceiling(maxX - eps), left + 1), bottom = Math.Max(Math.Ceiling(maxY - eps), top + 1);
            left = Math.Max(left, area.Left);
            top = Math.Max(top, area.Top);
            right = Math.Min(right, area.Right);
            bottom = Math.Min(bottom, area.Bottom);
            if (right <= left || bottom <= top) return null;
            return Rectangle.FromLTRB((int)left, (int)top, (int)right, (int)bottom);
        }

        Edit(DomainStrings.Project_CanvasSize, ProjectChange.Canvas | ProjectChange.Pixels | ProjectChange.Frames | ProjectChange.Settings | ProjectChange.Layers, () =>
        {
            // 일반 레이어: 셀 + 캔버스 밖 그림을 옮겨 새 캔버스 안·밖으로 다시 나눈다. 링크된 셀(같은 그림·같은 캔버스 밖 그림)은 한 번만 계산한다.
            var done = new Dictionary<PixelImage, (PixelImage Cel, CelOverflow? Overflow)>(ReferenceEqualityComparer.Instance);
            foreach (var key in _cels.Keys.Where(k => !k.Layer.IsPart).Concat(_overflow.Keys).Distinct().ToList())
            {
                var cel = _cels.GetValueOrDefault(key);
                if (cel is null || !done.TryGetValue(cel, out var result))
                {
                    var (full, origin) = FullOf(key, cel, oldCanvas);
                    result = ImageTransform.WarpWithCanvas(full, origin, oldCanvas, quad, area, mode) is { } moved
                        ? Split(moved.Image, moved.Origin, newCanvas)
                        : (new PixelImage(width, height), null);
                    if (cel is not null)
                    {
                        done[cel] = result;
                        InheritLinkTag(cel, result.Cel);
                    }
                }
                _cels[key] = result.Cel;
                SetOverflow(key, result.Overflow);
            }

            foreach (var part in _layers.Where(l => l.IsPart).ToList())
            {
                var before = part.PartRect;
                var cels = CelsOf(part);
                var rect = Enclosing(before);
                // 그림마다 한 번: 기준 영역에 놓인 셀을 옮긴 그림(새 영역을 아직 모르면 남기는 범위 전체에서)
                var moved = new Dictionary<PixelImage, (PixelImage Image, Point Origin)?>(ReferenceEqualityComparer.Instance);
                foreach (var (_, cel) in cels)
                    if (!moved.ContainsKey(cel))
                        moved[cel] = ImageTransform.WarpWithCanvas(cel, before.Location, oldCanvas, quad, rect ?? area, mode);
                // 꼭짓점이 지평선 너머면 옮긴 그림을 감싸는 사각형(그것도 없으면 1×1)
                var target = rect ?? moved.Values.OfType<(PixelImage Image, Point Origin)>().Select(m => new Rectangle(m.Origin, m.Image.Size))
                    .DefaultIfEmpty(new Rectangle(0, 0, 1, 1)).Aggregate(Rectangle.Union);
                var place = OncePerImage(cel =>
                {
                    var image = new PixelImage(target.Width, target.Height);
                    if (moved[cel] is { } m) image.Blit(m.Image, m.Origin.X - target.X, m.Origin.Y - target.Y);
                    return image;
                });
                foreach (var (key, cel) in cels) _cels[key] = place(cel);

                // 프레임마다 옮긴 거리: 옮긴 왼쪽 위 − 기준 위치를 옮긴 곳(옮길 수 없으면 그대로)
                var from = before.Location;
                var baseAt = MapRounded(from.X, from.Y);
                MapPartShifts(part, s => baseAt is { } b && MapRounded(from.X + s.X, from.Y + s.Y) is { } m ? new Point(m.X - b.X, m.Y - b.Y) : s);
                // 부위 피벗: 같은 그림 지점을 새 영역 기준으로
                MapLayerPivots(part, p =>
                {
                    var (px, py) = p.ToLocalPixel(before.Width, before.Height);
                    return Map(before.X + px, before.Y + py) is { } q
                        ? new Pivot((float)((q.X - target.X) / target.Width), 1f - (float)((q.Y - target.Y) / target.Height))
                        : p;
                });
                part.PartOffset = target.Location;
                part.PartSize = target.Size;
                part.PartBorder = SpriteBorder.None;
            }

            _width = width;
            _height = height;
            MapCanvasPivots(p =>
            {
                var (px, py) = p.ToLocalPixel(oldW, oldH);
                return Map(px, py) is { } q ? new Pivot((float)(q.X / width), 1f - (float)(q.Y / height)) : p;
            });
            Sprite.Border = SpriteBorder.None;
        });
    }
}
