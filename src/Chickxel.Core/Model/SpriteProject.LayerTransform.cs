using System.Drawing;
using Chickxel.Core.Edit;

namespace Chickxel.Core.Model;

// 변형 모드(지금 셀 / 레이어 전체)의 픽셀 엔진: 셀마다 뗄 원본을 갖고 있다가 값마다 미리보기를 그리고, 적용하면 한 단계로 바꾼다.
public sealed partial class SpriteProject
{
    /// <summary>
    /// 여러 셀에 같은 자유 변형(회전·크기·옮기기)과 여백을 입힌다. 셀 자체는 <see cref="Commit"/> 때까지 건드리지 않는다.
    /// <list type="bullet">
    /// <item>좌표는 기준 레이어 좌표(부위면 부위 안 좌표)다. 다른 레이어의 셀은 그 프레임에서 기준 레이어와의 자리 차이(<see cref="Target.Offset"/>)만큼 맞춘다.</item>
    /// <item>선택 영역이 있으면 셀마다 그 모양 안 픽셀만 떼어 같은 대응으로 옮기고, 피벗은 그대로다. 여백을 넓히면 가장자리 픽셀을 복사해 늘린다.</item>
    /// <item>선택이 없으면 칠한 픽셀 전체를 옮기고 피벗도 그림을 따라간다. 상자(<see cref="Box"/>) 밖 픽셀은 잘린다(여백을 좁힘).
    /// 레이어 전체(<see cref="WholeLayer"/>)면 프레임마다 그 프레임의 피벗을 중심으로 돈다.</item>
    /// <item>부위는 결과가 영역 밖으로 나가면 영역을 넓힌다(모든 프레임·스킨). 선택 없이 레이어 전체면 영역이 상자를 따라가고(돌리면 감싸는 크기),
    /// 돌리면 9-slice 테두리는 지운다. 선택 없이 옮기기만 하면 그림 대신 부위 위치를 옮긴다(지금 셀이면 그 프레임만).</item>
    /// </list>
    /// </summary>
    public sealed class LayerTransform
    {
        /// <summary>변형할 셀 하나: 레이어·프레임, 기준 레이어와의 자리 차이와 떼어 낸 원본.</summary>
        public sealed class Target
        {
            internal Target(Layer layer, Frame frame, CelKey key, PixelImage? cel, Point offset, FreeTransform raw, PixelImage? rest, Point restOrigin, PixelImage full, Point fullOrigin)
            {
                Layer = layer;
                Frame = frame;
                Key = key;
                Cel = cel;
                Offset = offset;
                Raw = raw;
                Rest = rest;
                RestOrigin = restOrigin;
                Full = full;
                FullOrigin = fullOrigin;
            }

            public Layer Layer { get; }
            public Frame Frame { get; }

            /// <summary>이 셀 좌표 + <see cref="Offset"/> = 기준 레이어 좌표.</summary>
            public Point Offset { get; }

            internal CelKey Key { get; }
            /// <summary>원래 셀 그림(링크된 셀은 같은 그림 — 한 번만 바꿈). 캔버스 밖 그림만 있는 셀이면 null.</summary>
            internal PixelImage? Cel { get; }
            /// <summary>떼어 낸 원본(이 셀 좌표).</summary>
            internal FreeTransform Raw { get; }
            /// <summary>선택 영역을 떼어 낸 뒤 남은 그림과 왼쪽 위(이 셀 좌표). 선택이 없으면 null(상자 밖은 잘림).</summary>
            internal PixelImage? Rest { get; }
            internal Point RestOrigin { get; }
            /// <summary>원래 셀 전체(일반 레이어는 캔버스 밖 그림 포함)와 왼쪽 위(이 셀 좌표) — 잘릴 픽셀 표시용.</summary>
            internal PixelImage Full { get; }
            internal Point FullOrigin { get; }
            /// <summary>지금 상자에 맞춘 원본(<see cref="Raw"/>를 여백만큼 자르거나 늘림)과 그때의 상자.</summary>
            internal FreeTransform? Source { get; set; }
            internal Rectangle SourceBox { get; set; }
        }

        private readonly SpriteProject _project;
        private readonly List<Target> _targets;
        private readonly PixelMask? _selection;

        private LayerTransform(SpriteProject project, Layer layer, Frame frame, List<Target> targets, PixelMask? selection, bool wholeLayer, Rectangle start, int locked)
        {
            _project = project;
            ReferenceLayer = layer;
            ReferenceFrame = frame;
            _targets = targets;
            _selection = selection;
            WholeLayer = wholeLayer;
            StartBox = start;
            Box = new TransformBox(start);
            LockedSkipped = locked;
        }

        /// <summary>좌표 기준 레이어(캔버스에 보이는 레이어).</summary>
        public Layer ReferenceLayer { get; }

        /// <summary>값의 기준 프레임: 레이어 전체면 프레임마다 피벗 차이만큼 옮긴 값을 쓴다(<see cref="ParamsAt"/>).</summary>
        public Frame ReferenceFrame { get; private set; }

        /// <summary>레이어 전체(모든 프레임·모든 스킨, 프레임마다 그 피벗을 중심으로)인지. 아니면 고른 셀만.</summary>
        public bool WholeLayer { get; }

        /// <summary>선택 영역 안 픽셀만 옮기는지(피벗·부위 영역은 그대로 — 영역 밖으로 나가면 넓힘).</summary>
        public bool HasSelection => _selection is not null;

        /// <summary>처음 상자(기준 레이어 좌표): 선택 영역, 레이어 전체 부위면 부위 영역, 그 밖에는 셀들의 칠한 픽셀을 감싸는 사각형.</summary>
        public Rectangle StartBox { get; }

        /// <summary>지금 상자(여백을 바꾸면 달라짐) — 손잡이와 값(<see cref="TransformParams"/>)의 기준.</summary>
        public TransformBox Box { get; private set; }

        public IReadOnlyList<Target> Targets => _targets;

        /// <summary>기준 레이어의 프레임 <paramref name="frame"/>에서 지금 보이는 셀(부위면 보이는 스킨)의 대상. 그 셀을 바꾸지 않으면 null.</summary>
        public Target? ShownTarget(Frame frame)
        {
            var key = new CelKey(ReferenceLayer, frame, ActiveKey(ReferenceLayer));
            return _targets.FirstOrDefault(t => t.Key == key);
        }

        /// <summary>그림이 있지만 레이어가 잠겨 건너뛴 셀 수.</summary>
        public int LockedSkipped { get; }

        /// <summary>
        /// 셀들에서 원본을 뗀다. <paramref name="layer"/>·<paramref name="frame"/>은 기준(캔버스에 보이는 셀), <paramref name="selection"/>은 기준 레이어 좌표의 선택 영역
        /// (null = 셀마다 칠한 픽셀 전체). <paramref name="wholeLayer"/>면 <paramref name="cels"/> 대신 그 레이어의 모든 프레임(부위는 모든 스킨)을 쓴다.
        /// 그룹·빈 셀·잠긴 레이어는 건너뛰고, 뗄 것이 하나도 없으면 null.
        /// </summary>
        public static LayerTransform? Create(SpriteProject project, int layer, int frame, IEnumerable<(int Layer, int Frame)> cels, PixelMask? selection, bool wholeLayer)
        {
            var reference = project._layers[layer];
            var refFrame = project._frames[frame];
            if (selection is { IsEmpty: true }) selection = null;
            var list = wholeLayer ? Enumerable.Range(0, project._frames.Count).Select(f => (layer, f)) : cels.Prepend((layer, frame));
            var targets = new List<Target>();
            var seen = new HashSet<(int, int)>();
            int locked = 0;
            foreach (var (l, f) in list)
            {
                if (!seen.Add((l, f)) || l < 0 || l >= project._layers.Count || f < 0 || f >= project._frames.Count) continue;
                var target = project._layers[l];
                if (target.IsGroup) continue;
                var keys = target.IsPart && wholeLayer
                    ? project.CelsOf(target).Where(c => ReferenceEquals(c.Key.Frame, project._frames[f])).Select(c => c.Key).ToList()
                    : [new CelKey(target, project._frames[f], ActiveKey(target))];
                foreach (var key in keys)
                {
                    var cel = project._cels.GetValueOrDefault(key);
                    bool overflow = !target.IsPart && project._overflow.ContainsKey(key);
                    if (!HasContent(cel) && !overflow) continue;
                    if (project.IsLayerLocked(l))
                    {
                        locked++;
                        continue;
                    }
                    var at = project.LayerOrigin(target, f);
                    var refAt = project.LayerOrigin(reference, f);
                    var offset = new Point(at.X - refAt.X, at.Y - refAt.Y);
                    if (Lift(project, key, cel, offset, selection) is { } t) targets.Add(t);
                }
            }
            if (targets.Count == 0) return null;
            Rectangle start;
            if (selection is { } s) start = s.Bounds;
            else if (wholeLayer && reference.IsPart) start = new Rectangle(Point.Empty, reference.PartSize);
            else
            {
                start = Rectangle.Empty;
                foreach (var t in targets)
                {
                    var r = t.Raw.SourceBounds;
                    r.Offset(t.Offset);
                    start = start.IsEmpty ? r : Rectangle.Union(start, r);
                }
            }
            return new LayerTransform(project, reference, refFrame, targets, selection, wholeLayer, start, locked);
        }

        /// <summary>셀 하나에서 원본을 뗀다(선택 모양 안 또는 칠한 픽셀 전체). 뗄 픽셀이 없으면 null.</summary>
        private static Target? Lift(SpriteProject project, CelKey key, PixelImage? cel, Point offset, PixelMask? selection)
        {
            var layer = key.Layer;
            var (full, origin) = layer.IsPart
                ? (cel ?? new PixelImage(layer.PartSize.Width, layer.PartSize.Height), Point.Empty)
                : project.FullOf(key, cel, project.Size);
            var fullRect = new Rectangle(origin, full.Size);
            if (selection is not { } sel)
            {
                var opaque = full.GetOpaqueBounds();
                if (opaque.IsEmpty) return null;
                opaque.Offset(origin);
                var lifted = full.Crop(new Rectangle(opaque.X - origin.X, opaque.Y - origin.Y, opaque.Width, opaque.Height));
                return new Target(layer, key.Frame, key, cel, offset, FreeTransform.Own(lifted, new PixelMask(opaque)), null, origin, full, origin);
            }
            // 선택 모양(이 셀 좌표)을 셀 전체 안으로 자르고 그 안 픽셀을 뗀다.
            var region = sel.Offset(-offset.X, -offset.Y).Trim(fullRect);
            if (region.IsEmpty) return null;
            var b = region.Bounds;
            var take = new PixelImage(b.Width, b.Height);
            var rest = full.Clone();
            bool any = false;
            for (int y = b.Top; y < b.Bottom; y++)
                for (int x = b.Left; x < b.Right; x++)
                {
                    if (!region.Contains(x, y)) continue;
                    int fx = x - origin.X, fy = y - origin.Y;
                    var c = full.Pixels[fy * full.Width + fx];
                    if (c.A == 0) continue;
                    take.Pixels[(y - b.Y) * b.Width + (x - b.X)] = c;
                    rest.Pixels[fy * full.Width + fx] = Color32.Transparent;
                    any = true;
                }
            if (!any) return null;
            return new Target(layer, key.Frame, key, cel, offset, FreeTransform.Own(take, region), rest, origin, full, origin);
        }

        // ───────────── 상자·값 ─────────────

        /// <summary>
        /// 상자를 <paramref name="bounds"/>(기준 레이어 좌표)로 바꾼다(여백): 그림은 제자리에 있고, 좁힌 쪽 밖은 잘리며 넓힌 쪽은 선택 영역이면 가장자리 픽셀로 채운다.
        /// <paramref name="p"/>(지금 값)를 같은 그림이 그대로 보이는 새 상자의 값으로 바꿔 돌려준다. 크기는 1px 이상.
        /// </summary>
        public TransformParams SetBox(Rectangle bounds, TransformParams p)
        {
            bounds.Width = Math.Clamp(bounds.Width, 1, PixelImage.MaxSize);
            bounds.Height = Math.Clamp(bounds.Height, 1, PixelImage.MaxSize);
            if (bounds == Box.Bounds) return p;
            var next = new TransformBox(bounds);
            var q = Box.ParamsFor(next, p);
            Box = next;
            return q;
        }

        /// <summary>프레임 <paramref name="frame"/>에서 쓸 값: 레이어 전체이고 선택이 없으면 기준 프레임과의 피벗 차이만큼 옮겨 그 프레임의 피벗을 중심으로 돈다.</summary>
        public TransformParams ParamsAt(Frame frame, TransformParams p)
        {
            if (!WholeLayer || HasSelection || ReferenceEquals(frame, ReferenceFrame)) return p;
            var (dx, dy) = PivotShift(frame, p);
            return p with { CenterX = p.CenterX + dx, CenterY = p.CenterY + dy };
        }

        /// <summary>
        /// 기준 프레임을 <paramref name="frame"/>로 옮긴다(보이는 프레임이 바뀜): 그 프레임에서 쓸 값과 회전 중심(기준 레이어 좌표, 피벗 차이만큼 옮김)을 돌려준다.
        /// 모든 프레임의 결과는 그대로다.
        /// </summary>
        public (TransformParams Params, (double X, double Y) Center) Rebase(Frame frame, TransformParams p, (double X, double Y) center)
        {
            if (ReferenceEquals(frame, ReferenceFrame)) return (p, center);
            var q = ParamsAt(frame, p);
            if (WholeLayer && !HasSelection)
            {
                var (ax, ay) = PivotPixel(ReferenceLayer, ReferenceFrame);
                var (bx, by) = PivotPixel(ReferenceLayer, frame);
                center = (center.X + bx - ax, center.Y + by - ay);
            }
            ReferenceFrame = frame;
            return (q, center);
        }

        /// <summary>레이어 전체 변형에서 프레임 <paramref name="frame"/>의 중심 이동: (I − A)(그 프레임 피벗 − 기준 프레임 피벗).</summary>
        private (double X, double Y) PivotShift(Frame frame, TransformParams p)
        {
            var (ax, ay) = PivotPixel(ReferenceLayer, ReferenceFrame);
            var (bx, by) = PivotPixel(ReferenceLayer, frame);
            double vx = bx - ax, vy = by - ay;
            var (lx, ly) = Box.Linear(p, vx, vy);
            return (vx - lx, vy - ly);
        }

        /// <summary>레이어의 그 프레임 피벗(그 레이어 좌표 px, 연속).</summary>
        private (double X, double Y) PivotPixel(Layer layer, Frame frame)
        {
            var (x, y) = PivotOf(layer, frame).ToLocalPixel(layer.IsPart ? layer.PartSize.Width : _project._width, layer.IsPart ? layer.PartSize.Height : _project._height);
            return (x, y);
        }

        /// <summary>셀 <paramref name="t"/>의 점(그 셀 좌표)을 값 <paramref name="p"/>(기준 프레임 값)로 옮긴 곳(그 셀 좌표).</summary>
        private (double X, double Y) MapPoint(Target t, TransformParams p, double x, double y)
        {
            var (mx, my) = Box.Map(ParamsAt(t.Frame, p), x + t.Offset.X, y + t.Offset.Y);
            return (mx - t.Offset.X, my - t.Offset.Y);
        }

        /// <summary>옮기기만 했는지(각도 0, 배율 1, 여백 그대로, 정수 픽셀만큼).</summary>
        private bool IsShiftOnly(TransformParams p, out int dx, out int dy)
        {
            var id = Box.Identity;
            double fx = p.CenterX - id.CenterX, fy = p.CenterY - id.CenterY;
            dx = (int)Math.Round(fx);
            dy = (int)Math.Round(fy);
            return p.Angle == 0 && p.ScaleX == 1 && p.ScaleY == 1 && Box.Bounds == StartBox
                   && Math.Abs(fx - dx) < 1e-9 && Math.Abs(fy - dy) < 1e-9;
        }

        // ───────────── 그리기 ─────────────

        /// <summary>
        /// 셀 하나의 결과(그 셀 좌표의 그림과 왼쪽 위 — 남은 그림 위에 옮긴 픽셀). 아무것도 남지 않으면 null.
        /// <paramref name="slice"/>는 레이어 전체 부위를 돌리지 않고 늘릴 때 9-slice로 늘리는 가운데 채우기(null = 9-slice 안 씀).
        /// </summary>
        public (PixelImage Image, Point Origin)? Render(Target t, TransformParams p, ResampleMode mode, NineSliceFill? slice)
        {
            var moved = RenderMoved(t, p, mode, slice);
            Rectangle rect = moved is { } m ? m.Region.Bounds : Rectangle.Empty;
            if (t.Rest is { } rest && !rest.GetOpaqueBounds().IsEmpty)
            {
                var restRect = new Rectangle(t.RestOrigin, rest.Size);
                rect = rect.IsEmpty ? restRect : Rectangle.Union(rect, restRect);
            }
            if (rect.IsEmpty) return null;
            var image = new PixelImage(rect.Width, rect.Height);
            if (t.Rest is { } r) image.Blit(r, t.RestOrigin.X - rect.X, t.RestOrigin.Y - rect.Y);
            if (moved is { } mv)
            {
                // 내려놓기와 같이: 투명 픽셀은 아래를 덮지 않는다.
                var b = mv.Region.Bounds;
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                    {
                        var c = mv.Image.Pixels[y * b.Width + x];
                        if (c.A != 0) image.Pixels[(b.Y + y - rect.Y) * rect.Width + b.X + x - rect.X] = c;
                    }
            }
            return (image, rect.Location);
        }

        /// <summary>
        /// 잘릴 픽셀(선택 없이 상자를 좁혀 상자 밖으로 남는 원래 픽셀)과 왼쪽 위(그 셀 좌표) — 화면에 흐리게 보인다. 없으면 null.
        /// </summary>
        public (PixelImage Image, Point Origin)? CutPixels(Target t)
        {
            if (HasSelection) return null;
            var box = Box.Bounds;
            box.Offset(-t.Offset.X, -t.Offset.Y);
            var full = new Rectangle(t.FullOrigin, t.Full.Size);
            var opaque = t.Full.GetOpaqueBounds();
            opaque.Offset(t.FullOrigin);
            if (opaque.IsEmpty || box.Contains(opaque)) return null;
            var cut = t.Full.Crop(new Rectangle(opaque.X - full.X, opaque.Y - full.Y, opaque.Width, opaque.Height));
            var keep = Rectangle.Intersect(box, opaque);
            if (keep.Width > 0 && keep.Height > 0) cut.FillRect(new Rectangle(keep.X - opaque.X, keep.Y - opaque.Y, keep.Width, keep.Height), Color32.Transparent);
            return cut.GetOpaqueBounds().IsEmpty ? null : (cut, opaque.Location);
        }

        /// <summary>셀 하나의 옮긴 픽셀(그 셀 좌표). 상자 밖으로 모두 잘렸으면 null.</summary>
        private TransformResult? RenderMoved(Target t, TransformParams p, ResampleMode mode, NineSliceFill? slice)
        {
            if (SourceOf(t) is not { } src) return null;
            var at = ParamsAt(t.Frame, p);
            if (SliceBorder(t.Layer, at, slice) is { } border)
            {
                // 9-slice: 상자(= 부위 영역) 그대로 잘라 테두리는 그대로, 가운데만 늘려 변형한 상자 자리에 놓는다(각도 0).
                var to = Box.MappedBounds(at);
                var box = Box.Bounds;
                var crop = new PixelImage(box.Width, box.Height);
                crop.Blit(t.Full, t.FullOrigin.X - box.X, t.FullOrigin.Y - box.Y);
                var scaled = NineSlice.Scale(crop, to.Width, to.Height, border, slice!.Value);
                if (at.ScaleX < 0) scaled = ImageTransform.FlipHorizontal(scaled);
                if (at.ScaleY < 0) scaled = ImageTransform.FlipVertical(scaled);
                return new TransformResult(scaled, new PixelMask(to));
            }
            var r = src.Render(Box.ParamsFor(src.Box, at, t.Offset.X, t.Offset.Y), mode);
            return r.Region.IsEmpty ? null : r;
        }

        /// <summary>
        /// 9-slice로 늘릴 테두리(상자 기준): 레이어 전체 부위를 선택 없이 각도 0으로 늘리고 테두리가 상자에 맞을 때만. 아니면 null.
        /// </summary>
        private SpriteBorder? SliceBorder(Layer layer, TransformParams p, NineSliceFill? slice)
        {
            if (slice is null || !WholeLayer || HasSelection || !layer.IsPart || p.Angle != 0 || layer.PartBorder.IsNone) return null;
            var b = BorderInBox(layer);
            return NineSlice.Applies(b, Box.Bounds.Width, Box.Bounds.Height) ? b : null;
        }

        /// <summary>부위 테두리를 상자 가장자리 기준으로(여백만큼 옮김).</summary>
        private SpriteBorder BorderInBox(Layer part)
        {
            var b = part.PartBorder;
            var box = Box.Bounds;
            var size = part.PartSize;
            return new SpriteBorder(b.Left - box.X, b.Top - box.Y, b.Right - (size.Width - box.Right), b.Bottom - (size.Height - box.Bottom));
        }

        /// <summary>지금 상자에 맞춘 셀 원본(여백만큼 자르거나, 선택 영역이면 가장자리 픽셀로 늘림). 모두 잘렸으면 null.</summary>
        private FreeTransform? SourceOf(Target t)
        {
            var box = Box.Bounds;
            box.Offset(-t.Offset.X, -t.Offset.Y);
            if (t.Source is not null && t.SourceBox == box) return t.Source;
            var raw = t.Raw.SourceBounds;
            Rectangle bounds;
            if (HasSelection)
            {
                // 처음 상자 가장자리에 닿아 있던 쪽만 여백만큼 늘리고, 좁히면 그 안만 남긴다.
                var start = StartBox;
                start.Offset(-t.Offset.X, -t.Offset.Y);
                int l = box.Left < start.Left ? (raw.Left == start.Left ? box.Left : raw.Left) : Math.Max(raw.Left, box.Left);
                int top = box.Top < start.Top ? (raw.Top == start.Top ? box.Top : raw.Top) : Math.Max(raw.Top, box.Top);
                int r = box.Right > start.Right ? (raw.Right == start.Right ? box.Right : raw.Right) : Math.Min(raw.Right, box.Right);
                int bottom = box.Bottom > start.Bottom ? (raw.Bottom == start.Bottom ? box.Bottom : raw.Bottom) : Math.Min(raw.Bottom, box.Bottom);
                bounds = l < r && top < bottom ? Rectangle.FromLTRB(l, top, r, bottom) : Rectangle.Empty;
            }
            else bounds = Rectangle.Intersect(raw, box);
            t.SourceBox = box;
            t.Source = bounds.Width > 0 && bounds.Height > 0 ? t.Raw.Reframe(bounds, extendEdges: HasSelection) : null;
            return t.Source;
        }

        /// <summary>
        /// 선택 영역을 같은 대응으로 옮긴 모양(기준 레이어 좌표, 기준 프레임 값) — 적용 뒤 선택으로 남긴다. 선택이 없으면 null.
        /// </summary>
        public PixelMask? SelectionAfter(TransformParams p, ResampleMode mode)
        {
            if (_selection is not { } sel) return null;
            var shape = FreeTransform.Own(new PixelImage(sel.Bounds.Width, sel.Bounds.Height), sel).Reframe(Box.Bounds, extendEdges: true);
            var r = shape.Render(Box.ParamsFor(shape.Box, p), mode);
            return r.Region.IsEmpty ? null : r.Region;
        }

        // ───────────── 적용 ─────────────

        /// <summary>
        /// 모든 셀에 적용한다(실행취소 한 단계 <paramref name="label"/>). 그동안 없어졌거나 잠긴 레이어·프레임의 셀은 건너뛴다. 바꾼 셀이 있으면 true.
        /// </summary>
        public bool Commit(string label, TransformParams p, ResampleMode mode, NineSliceFill? slice)
        {
            var groups = _targets.Where(t => _project._layers.Contains(t.Layer) && _project._frames.Contains(t.Frame) && !_project.IsLayerLocked(_project.IndexOf(t.Layer)))
                .GroupBy(t => t.Layer).ToList();
            if (groups.Count == 0) return false;
            if (Box.Bounds == StartBox && p == Box.Identity) return false;
            int dx = 0, dy = 0;
            bool shift = !HasSelection && IsShiftOnly(p, out dx, out dy);
            // 픽셀은 기록 밖에서 미리 그려 둔다(Edit 안에서는 바꾸기만).
            var results = new Dictionary<Target, (PixelImage Image, Point Origin)?>();
            foreach (var group in groups)
                if (!(shift && group.Key.IsPart))
                    foreach (var t in group) results[t] = Render(t, p, mode, slice);
            _project.Edit(label, ProjectChange.Pixels | ProjectChange.Layers | ProjectChange.Frames, () =>
            {
                foreach (var group in groups)
                {
                    var targets = group.ToList();
                    if (group.Key.IsPart)
                    {
                        if (shift) ShiftPart(group.Key, targets, dx, dy);
                        else CommitPart(group.Key, targets, results, p, slice);
                    }
                    else CommitNormal(group.Key, targets, results, p);
                }
            });
            return true;
        }

        /// <summary>부위를 옮기기만 함: 그림 대신 부위 위치를 옮긴다(레이어 전체면 모든 프레임, 아니면 그 셀들의 프레임). 피벗은 부위 기준이라 같이 따라간다.</summary>
        private void ShiftPart(Layer part, List<Target> targets, int dx, int dy)
        {
            var frames = WholeLayer ? _project._frames.ToList() : targets.Select(t => t.Frame).Distinct().ToList();
            if (frames.Count == _project._frames.Count && !part.IsAutoLaidOut)
            {
                part.PartOffset = new Point(part.PartOffset.X + dx, part.PartOffset.Y + dy);
                return;
            }
            foreach (var f in frames)
            {
                var s = f.PartShifts.GetValueOrDefault(part);
                SetShift(f, part, new Point(s.X + dx, s.Y + dy));
            }
        }

        /// <summary>일반 레이어: 셀과 캔버스 밖 그림을 바꾸고(링크된 셀은 한 번), 선택이 없으면 피벗도 그림을 따라가게 한다.</summary>
        private void CommitNormal(Layer layer, List<Target> targets, Dictionary<Target, (PixelImage Image, Point Origin)?> results, TransformParams p)
        {
            var size = _project.Size;
            var done = new HashSet<PixelImage>(ReferenceEqualityComparer.Instance);
            foreach (var t in targets)
            {
                if (t.Cel is not null && !done.Add(t.Cel)) continue;
                var (cel, overflow) = results[t] is { } r ? Split(r.Image, r.Origin, size) : (new PixelImage(size.Width, size.Height), null);
                if (HasContent(cel)) _project.SetLinked(t.Key, cel, overflow);
                else
                    foreach (var k in _project.LinkedKeys(t.Key))
                    {
                        _project._cels.Remove(k);
                        _project.SetOverflow(k, overflow);
                    }
            }
            if (HasSelection) return;
            var (w, h) = (_project._width, _project._height);
            Pivot ToPivot(double x, double y) => new((float)(x / w), 1f - (float)(y / h));
            if (WholeLayer)
            {
                var (sx, sy) = PivotFollow(p);
                _project.MapLayerPivots(layer, q =>
                {
                    var (x, y) = q.ToLocalPixel(w, h);
                    return ToPivot(x + sx, y + sy);
                });
                return;
            }
            foreach (var t in targets.GroupBy(t => t.Frame).Select(g => g.First()))
            {
                var (px, py) = PivotPixel(layer, t.Frame);
                var (x, y) = MapPoint(t, p, px, py);
                t.Frame.LayerPivots[layer] = ToPivot(x, y);
            }
        }

        /// <summary>레이어 전체 변형에서 모든 피벗이 옮겨 가는 거리: 기준 프레임 피벗이 옮겨 간 곳 − 원래 자리(프레임마다 그 피벗을 중심으로 돌아 모두 같음).</summary>
        private (double X, double Y) PivotFollow(TransformParams p)
        {
            var (px, py) = PivotPixel(ReferenceLayer, ReferenceFrame);
            var (x, y) = Box.Map(p, px, py);
            return (x - px, y - py);
        }

        /// <summary>
        /// 부위: 새 영역(결과가 들어가게 넓히거나 상자를 따라감)으로 모든 스킨 셀을 맞추고 결과를 넣는다. 테두리와 피벗도 같은 그림 지점을 가리키게 한다.
        /// </summary>
        private void CommitPart(Layer part, List<Target> targets, Dictionary<Target, (PixelImage Image, Point Origin)?> results, TransformParams p, NineSliceFill? slice)
        {
            var size = part.PartSize;
            var old = new Rectangle(Point.Empty, size);
            // 새 영역(부위 좌표)
            Rectangle area;
            if (!HasSelection && WholeLayer)
            {
                area = Rectangle.Empty;
                foreach (var f in targets.Select(t => t.Frame).Distinct())
                {
                    var r = Box.MappedBounds(ParamsAt(f, p));
                    area = area.IsEmpty ? r : Rectangle.Union(area, r);
                }
            }
            else
            {
                area = HasSelection ? old : AreaBox(size);
                foreach (var t in targets)
                    if (results[t] is { } r && r.Image.GetOpaqueBounds() is { IsEmpty: false } content)
                    {
                        content.Offset(r.Origin);
                        area = Rectangle.Union(area, content);
                    }
            }
            area.Width = Math.Clamp(area.Width, 1, PixelImage.MaxSize);
            area.Height = Math.Clamp(area.Height, 1, PixelImage.MaxSize);

            // 셀: 결과는 새 영역에 넣고, 나머지(다른 스킨·프레임)는 같은 자리에 두고 영역만 맞춘다. 같은 그림(링크)은 한 번만.
            var done = new Dictionary<PixelImage, PixelImage>(ReferenceEqualityComparer.Instance);
            foreach (var t in targets)
            {
                if (t.Cel is null || done.ContainsKey(t.Cel)) continue;
                var next = new PixelImage(area.Width, area.Height);
                if (results[t] is { } r) next.Blit(r.Image, r.Origin.X - area.X, r.Origin.Y - area.Y);
                done[t.Cel] = next;
            }
            foreach (var (key, cel) in _project.CelsOf(part))
            {
                if (!done.TryGetValue(cel, out var next))
                {
                    if (area == old) continue;
                    next = new PixelImage(area.Width, area.Height);
                    next.Blit(cel, -area.X, -area.Y);
                    done[cel] = next;
                }
                _project.InheritLinkTag(cel, next);
                _project._cels[key] = next;
            }

            // 9-slice 테두리
            var b = part.PartBorder;
            if (!b.IsNone)
            {
                SpriteBorder next;
                if (!HasSelection && p.Angle != 0) next = SpriteBorder.None;
                else if (!HasSelection && WholeLayer)
                {
                    var inBox = BorderInBox(part);
                    var box = Box.Bounds;
                    if (!NineSlice.Applies(inBox, box.Width, box.Height)) next = SpriteBorder.None;
                    else
                    {
                        next = SliceBorder(part, p, slice) is not null ? inBox : ScaleBorder(inBox, box.Size, area.Size);
                        if (p.ScaleX < 0) next = new SpriteBorder(next.Right, next.Top, next.Left, next.Bottom);
                        if (p.ScaleY < 0) next = new SpriteBorder(next.Left, next.Bottom, next.Right, next.Top);
                    }
                }
                else next = new SpriteBorder(b.Left - area.X, b.Top - area.Y, b.Right + area.Right - size.Width, b.Bottom + area.Bottom - size.Height);
                part.PartBorder = NineSlice.Applies(next, area.Width, area.Height) ? next : SpriteBorder.None;
            }

            // 피벗: 같은 그림 지점(선택이 없으면 그림을 따라감)을 새 영역 기준으로.
            Pivot ToPivot(double x, double y) => new((float)((x - area.X) / area.Width), 1f - (float)((y - area.Y) / area.Height));
            var own = new List<(Frame Frame, Pivot Pivot)>();
            if (!HasSelection && !WholeLayer)
                foreach (var t in targets.GroupBy(t => t.Frame).Select(g => g.First()))
                {
                    var (px, py) = PivotPixel(part, t.Frame);
                    var (x, y) = MapPoint(t, p, px, py);
                    own.Add((t.Frame, ToPivot(x, y)));
                }
            var (sx, sy) = !HasSelection && WholeLayer ? PivotFollow(p) : (0, 0);
            _project.MapLayerPivots(part, q =>
            {
                var (x, y) = q.ToLocalPixel(size.Width, size.Height);
                return ToPivot(x + sx, y + sy);
            });
            foreach (var (f, pivot) in own) f.LayerPivots[part] = pivot;
            part.PartOffset = new Point(part.PartOffset.X + area.X, part.PartOffset.Y + area.Y);
            part.PartSize = area.Size;
        }

        /// <summary>선택 없이 지금 셀을 바꿀 때 부위 영역의 기준: 원래 영역을 여백(상자 가장자리를 옮긴 만큼)대로 옮긴 사각형(부위 좌표).</summary>
        private Rectangle AreaBox(Size size)
        {
            var box = Box.Bounds;
            return Rectangle.FromLTRB(box.Left - StartBox.Left, box.Top - StartBox.Top, size.Width + box.Right - StartBox.Right, size.Height + box.Bottom - StartBox.Bottom) is { Width: > 0, Height: > 0 } r
                ? r
                : new Rectangle(box.Left - StartBox.Left, box.Top - StartBox.Top, 1, 1);
        }
    }

    /// <summary>레이어 좌표 → 캔버스 좌표 차이(부위면 그 프레임의 부위 위치).</summary>
    internal Point LayerOrigin(Layer layer, int frameIndex) => layer.IsPart ? GetPartOffset(layer, frameIndex) : Point.Empty;
}
