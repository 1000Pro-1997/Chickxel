using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;
using Chickxel.Core.Tiles;

namespace Chickxel.Core.Model;

// 캔버스 밖 그림: 일반 레이어 셀은 캔버스 크기지만, 캔버스 밖으로 옮기거나 캔버스보다 큰 그림을 내려놓은 픽셀을 셀마다 따로 남겨 둔다.
// 다시 안으로 옮기거나 캔버스를 키우면 그대로 보인다. 합성·내보내기·효과는 캔버스 안(셀)만 쓴다.
public sealed partial class SpriteProject
{
    /// <summary>
    /// 셀의 캔버스 밖 그림: <paramref name="Image"/>를 캔버스 좌표 <paramref name="Origin"/>(왼쪽 위)에 놓는다. 캔버스와 겹치는 칸은 늘 투명이고
    /// 내용 있는 픽셀을 꼭 맞게 감싼다. 이미지는 만든 뒤 고치지 않는다(실행취소 사본이 참조만 담음).
    /// </summary>
    internal readonly record struct CelOverflow(PixelImage Image, Point Origin)
    {
        public Rectangle Rect => new(Origin, Image.Size);
    }

    /// <summary>캔버스 밖 그림을 남기는 범위: 캔버스 둘레로 이만큼(px, 전체는 <see cref="PixelImage.MaxSize"/> 이하). 더 멀리 나간 픽셀은 버린다.</summary>
    public const int OverflowMargin = 4096;

    private readonly Dictionary<CelKey, CelOverflow> _overflow = new();

    /// <summary>열린 스트로크가 시작될 때의 캔버스 밖 그림(<see cref="AddStrokeOverflow"/>로 바뀌면 확정·취소에 씀).</summary>
    private CelOverflow? _strokeOverflowBefore;
    private bool _strokeOverflowChanged;

    /// <summary>
    /// 열린 스트로크에서 캔버스 밖 칸을 칠하는 작업 그림(<see cref="SetStrokeOutside"/> — 처음 칠할 때 지금 캔버스 밖 그림을 복사해 만들고 칠하는 대로 넓힘)과
    /// 왼쪽 위(캔버스 좌표). 확정·<see cref="AddStrokeOverflow"/>·<see cref="ClearStrokeOverflow"/> 전에 캔버스 밖 그림으로 넘기고, 취소하면 버린다.
    /// </summary>
    private PixelImage? _strokeOutside;
    private Point _strokeOutsideOrigin;

    /// <summary>캔버스 밖 작업 그림이 바뀔 때마다 늘어나는 번호(화면 비트맵을 다시 채울지 정할 때).</summary>
    public int StrokeOutsideVersion { get; private set; }

    /// <summary>일반 레이어 셀이 캔버스 밖 그림을 남길 수 있는 범위(캔버스 좌표, 캔버스 포함).</summary>
    public Rectangle OverflowBounds => OverflowArea(Size);

    /// <summary>캔버스 밖 그림을 남기는 범위(캔버스 좌표).</summary>
    private static Rectangle OverflowArea(Size canvas)
    {
        int w = Math.Min(PixelImage.MaxSize, canvas.Width + 2 * OverflowMargin);
        int h = Math.Min(PixelImage.MaxSize, canvas.Height + 2 * OverflowMargin);
        return new Rectangle(-(w - canvas.Width) / 2, -(h - canvas.Height) / 2, w, h);
    }

    /// <summary>
    /// 일반 레이어 셀의 캔버스 밖 그림(이미지와 그 왼쪽 위의 캔버스 좌표 — 이미지의 캔버스와 겹치는 칸은 투명). 없거나 부위·그룹이면 null. 돌려준 그림은 고치지 않는다.
    /// </summary>
    public (PixelImage Image, Point Origin)? GetCelOverflow(int layerIndex, int frameIndex)
    {
        var layer = _layers[layerIndex];
        if (layer.IsTilemap) return TilemapOutside(layer, frameIndex) is { } t ? (t.Image, t.Origin) : null;
        if (layer.Kind != LayerKind.Normal || !_overflow.TryGetValue(new(layer, _frames[frameIndex], null), out var o)) return null;
        return (o.Image, o.Origin);
    }

    /// <summary>
    /// 셀 전체(일반 레이어면 캔버스 안 셀 + 캔버스 밖 그림)와 그 왼쪽 위의 캔버스 좌표. 캔버스 밖 그림이 없으면 셀 그대로(없으면 빈 그림)와 (0,0).
    /// 부위는 부위 셀 그대로와 (0,0)(부위 기준). 그룹이면 <see cref="InvalidOperationException"/>. 돌려준 그림은 고치지 않는다.
    /// </summary>
    public (PixelImage Image, Point Origin) GetFullCel(int layerIndex, int frameIndex)
    {
        var layer = _layers[layerIndex];
        if (layer.IsGroup) throw new InvalidOperationException(GroupHasNoCelsMessage);
        if (layer.IsTilemap)
        {
            var bounds = Rectangle.Union(Bounds, Rectangle.Intersect(TilemapBounds(layer), OverflowBounds));
            var data = layer.Tiles!;
            return TilesetOf(layer) is { } ts ? (TileMapRenderer.RenderArea(ts, data.Map, data.Origin, bounds), bounds.Location) : (new PixelImage(_width, _height), Point.Empty);
        }
        if (layer.IsPart) return (GetCel(layerIndex, frameIndex) ?? new PixelImage(layer.PartSize.Width, layer.PartSize.Height), Point.Empty);
        var key = new CelKey(layer, _frames[frameIndex], null);
        return FullOf(key, _cels.GetValueOrDefault(key), Size);
    }

    /// <summary>셀 + 캔버스 밖 그림을 합친 그림과 왼쪽 위(캔버스 좌표).</summary>
    private (PixelImage Image, Point Origin) FullOf(CelKey key, PixelImage? cel, Size canvas)
    {
        if (!_overflow.TryGetValue(key, out var o)) return (cel ?? new PixelImage(canvas.Width, canvas.Height), Point.Empty);
        var rect = Rectangle.Union(new Rectangle(Point.Empty, canvas), o.Rect);
        var full = new PixelImage(rect.Width, rect.Height);
        if (cel is not null) full.Blit(cel, -rect.X, -rect.Y);
        full.Blit(o.Image, o.Origin.X - rect.X, o.Origin.Y - rect.Y, alphaBlend: true);
        return (full, rect.Location);
    }

    /// <summary>
    /// 캔버스 좌표 <paramref name="origin"/>에 놓인 그림을 캔버스 안 셀(캔버스 크기)과 캔버스 밖 그림(없으면 null)으로 나눈다.
    /// 남기는 범위(<see cref="OverflowArea"/>) 밖 픽셀은 버린다.
    /// </summary>
    private static (PixelImage Cel, CelOverflow? Overflow) Split(PixelImage full, Point origin, Size canvas)
    {
        var cel = new PixelImage(canvas.Width, canvas.Height);
        cel.Blit(full, origin.X, origin.Y);
        return (cel, OutsideOf(full, origin, canvas));
    }

    /// <summary><see cref="Split"/>의 캔버스 밖 그림만(셀은 만들지 않음).</summary>
    private static CelOverflow? OutsideOf(PixelImage full, Point origin, Size canvas)
    {
        var canvasRect = new Rectangle(Point.Empty, canvas);
        var placed = new Rectangle(origin, full.Size);
        if (canvasRect.Contains(placed)) return null;
        var keep = Rectangle.Intersect(placed, OverflowArea(canvas));
        if (keep.IsEmpty) return null;
        var outside = full.Crop(new Rectangle(keep.X - origin.X, keep.Y - origin.Y, keep.Width, keep.Height));
        outside.FillRect(new Rectangle(-keep.X, -keep.Y, canvas.Width, canvas.Height), default);
        var opaque = outside.GetOpaqueBounds();
        if (opaque.IsEmpty) return null;
        var image = opaque.Size == outside.Size ? outside : outside.Crop(opaque);
        return new CelOverflow(image, new Point(keep.X + opaque.X, keep.Y + opaque.Y));
    }

    /// <summary>두 캔버스 밖 그림이 같은 자리·같은 픽셀인지(둘 다 없음 포함).</summary>
    private static bool SameOverflow(CelOverflow? a, CelOverflow? b)
        => a is { } x ? b is { } y && x.Origin == y.Origin && (ReferenceEquals(x.Image, y.Image) || x.Image.ContentEquals(y.Image)) : b is null;

    private void SetOverflow(CelKey key, CelOverflow? overflow)
    {
        if (overflow is { } o) _overflow[key] = o;
        else _overflow.Remove(key);
    }

    /// <summary>
    /// 일반 레이어 셀 내용을 캔버스 좌표 <paramref name="origin"/>에 놓인 <paramref name="image"/>로 바꾼다(실행취소 한 단계): 캔버스 안은 셀,
    /// 밖으로 나간 픽셀은 캔버스 밖 그림(남기는 범위 안)으로 남아 캔버스를 키우면 보인다. 캔버스 안이 비면 셀은 비우고 캔버스 밖 그림만 둔다.
    /// 일반 레이어가 아니면 <see cref="ArgumentException"/>.
    /// </summary>
    public void SetFullCel(int layerIndex, int frameIndex, PixelImage image, Point origin, string? label = null)
    {
        var layer = _layers[layerIndex];
        if (layer.Kind != LayerKind.Normal) throw new ArgumentException(DomainStrings.Project_NotNormalLayer, nameof(layerIndex));
        var key = new CelKey(layer, _frames[frameIndex], null);
        var (cel, overflow) = Split(image, origin, Size);
        Edit(label ?? DomainStrings.Project_ChangeCel, ProjectChange.Pixels, () =>
        {
            if (HasContent(cel)) SetLinked(key, cel, overflow);
            else
            {
                _cels.Remove(key);
                SetOverflow(key, overflow);
            }
        });
    }

    /// <summary>
    /// 열린 스트로크(일반 레이어) 셀의 캔버스 밖 그림을 지운다(스트로크를 확정하면 함께 기록, 취소하면 돌아옴). 넣은 그림 전체를 떠 있는 상태로
    /// 들 때(<c>BeginPlacedTransform</c>) 쓴다 — 내려놓으면 밖으로 나간 픽셀이 다시 캔버스 밖 그림이 된다. 지울 것이 없으면 false.
    /// </summary>
    public bool ClearStrokeOverflow()
    {
        if (_stroke is not { } s) return false;
        FlushStrokeOutside(s.Key);
        if (!_overflow.TryGetValue(s.Key, out var existing)) return false;
        if (!_strokeOverflowChanged)
        {
            _strokeOverflowBefore = existing;
            _strokeOverflowChanged = true;
        }
        SetOverflow(s.Key, null);
        return true;
    }

    /// <summary>
    /// 일반 레이어 셀들의 내용을 캔버스 밖 그림까지 통째로 (<paramref name="dx"/>, <paramref name="dy"/>)만큼 옮긴다(실행취소 한 단계, 밖으로 나간 픽셀은 캔버스 밖 그림으로 남음).
    /// 링크된 셀은 함께 옮겨지고(한 번만), 부위·그룹·잠긴 레이어·빈 셀은 건너뛴다. <paramref name="movePivots"/>면 그림이 옮겨진 프레임(링크된 셀의 프레임 포함)의
    /// 레이어 피벗도 같이 옮기고, 아니면 피벗은 제자리다. 옮긴 셀 수(고른 셀 기준)를 돌려준다(0이면 기록 없음).
    /// </summary>
    public int ShiftCels(string label, IEnumerable<(int Layer, int Frame)> cels, int dx, int dy, bool movePivots = true)
    {
        CommitOpenStrokes();
        var changes = new List<(CelKey Key, PixelImage Cel, CelOverflow? Overflow)>();
        var linked = new HashSet<PixelImage>(ReferenceEqualityComparer.Instance);
        int count = 0;
        if (dx != 0 || dy != 0)
            foreach (var (li, fi) in cels.Distinct())
            {
                if (li < 0 || li >= _layers.Count || fi < 0 || fi >= _frames.Count) continue;
                var layer = _layers[li];
                if (layer.Kind != LayerKind.Normal || IsLayerLocked(li)) continue;
                var key = new CelKey(layer, _frames[fi], null);
                var cel = _cels.GetValueOrDefault(key);
                if (!HasContent(cel) && !_overflow.ContainsKey(key)) continue;
                count++;
                if (cel is not null && !linked.Add(cel)) continue; // 링크된 셀은 이미 옮김
                var (full, origin) = FullOf(key, cel, Size);
                var (moved, overflow) = Split(full, new Point(origin.X + dx, origin.Y + dy), Size);
                changes.Add((key, moved, overflow));
            }
        if (changes.Count == 0) return 0;
        // 그림이 옮겨질 프레임(링크된 셀 포함): 레이어마다
        var movedFrames = new Dictionary<Layer, HashSet<Frame>>();
        foreach (var (key, _, _) in changes)
        {
            if (!movedFrames.TryGetValue(key.Layer, out var frames)) movedFrames[key.Layer] = frames = [];
            foreach (var k in LinkedKeys(key)) frames.Add(k.Frame);
        }
        bool pivotsChange = movePivots && PivotLayer is { } rep && movedFrames.ContainsKey(rep);
        Edit(label, ProjectChange.Pixels | ProjectChange.Layers | (pivotsChange ? ProjectChange.Frames : ProjectChange.None), () =>
        {
            foreach (var (key, cel, overflow) in changes) SetLinked(key, cel, overflow);
            if (movePivots)
                foreach (var (layer, frames) in movedFrames) ShiftLayerPivots(layer, frames, dx, dy);
        });
        return count;
    }

    /// <summary>
    /// 열린 스트로크(일반 레이어)의 셀에 <paramref name="source"/>를 캔버스 좌표 <paramref name="at"/>에 내려놓을 때 캔버스 밖으로 나간 픽셀을
    /// 캔버스 밖 그림에 남긴다(투명 픽셀은 덮지 않음 — 캔버스 안 부분은 부르는 쪽이 셀에 내려놓음). 스트로크를 확정하면 함께 기록된다.
    /// 일반 레이어 스트로크가 아니거나 캔버스 밖으로 나간 부분이 없으면 false.
    /// </summary>
    public bool AddStrokeOverflow(PixelImage source, Point at)
    {
        if (_stroke is not { } s || s.Key.Layer.Kind != LayerKind.Normal) return false;
        FlushStrokeOutside(s.Key);
        var canvas = Size;
        var placed = new Rectangle(at, source.Size);
        if (Bounds.Contains(placed)) return false;
        var area = OverflowArea(canvas);
        var existing = _overflow.TryGetValue(s.Key, out var o) ? o : (CelOverflow?)null;
        var rect = Rectangle.Intersect(existing is { } e ? Rectangle.Union(e.Rect, placed) : placed, area);
        if (rect.IsEmpty) return false;
        var image = new PixelImage(rect.Width, rect.Height);
        if (existing is { } ex) image.Blit(ex.Image, ex.Origin.X - rect.X, ex.Origin.Y - rect.Y);
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                var c = source.Pixels[y * source.Width + x];
                if (c.A != 0) image.Set(at.X + x - rect.X, at.Y + y - rect.Y, c);
            }
        var overflow = OutsideOf(image, rect.Location, canvas);
        if (!_strokeOverflowChanged)
        {
            _strokeOverflowBefore = existing;
            _strokeOverflowChanged = true;
        }
        SetOverflow(s.Key, overflow);
        return true;
    }

    /// <summary>열린 스트로크가 일반 레이어 셀이면 그 키(캔버스 밖 칸을 칠할 수 있음).</summary>
    private CelKey? OutsideStrokeKey => _stroke is { } s && s.Key.Layer.Kind == LayerKind.Normal ? s.Key : null;

    /// <summary>
    /// 열린 스트로크(일반 레이어) 셀의 캔버스 밖 칸 (x, y) 색: 이 스트로크에서 칠한 것까지(<paramref name="before"/>면 스트로크 시작 때 색).
    /// 캔버스 밖 그림이 없는 칸·일반 레이어 스트로크가 아니면 투명(캔버스 안 칸은 셀에서 읽는다).
    /// </summary>
    public Color32 GetStrokeOutside(int x, int y, bool before = false)
    {
        if (OutsideStrokeKey is not { } key) return default;
        if (!before && _strokeOutside is { } work) return work.Get(x - _strokeOutsideOrigin.X, y - _strokeOutsideOrigin.Y);
        var o = before && _strokeOverflowChanged ? _strokeOverflowBefore : _overflow.TryGetValue(key, out var current) ? current : (CelOverflow?)null;
        return o is { } v ? v.Image.Get(x - v.Origin.X, y - v.Origin.Y) : default;
    }

    /// <summary>
    /// 열린 스트로크(일반 레이어) 셀의 캔버스 밖 칸 (x, y)를 칠한다(남기는 범위 안). 스트로크를 확정하면 셀과 함께 실행취소 한 단계로 기록되고
    /// (밖을 다 지우면 캔버스 밖 그림이 없어짐), 취소하면 돌아온다. 캔버스 안·범위 밖·일반 레이어 스트로크가 아니면 무시. 색이 바뀌었으면 true.
    /// </summary>
    public bool SetStrokeOutside(int x, int y, Color32 color)
    {
        if (OutsideStrokeKey is not { } key || Bounds.Contains(x, y)) return false;
        var area = OverflowArea(Size);
        if (!area.Contains(x, y)) return false;
        if (_strokeOutside is null)
        {
            if (GetStrokeOutside(x, y) == color) return false;
            CelOverflow? current = _overflow.TryGetValue(key, out var o) ? o : null;
            if (!_strokeOverflowChanged)
            {
                _strokeOverflowBefore = current;
                _strokeOverflowChanged = true;
            }
            if (current is { } c)
            {
                _strokeOutside = c.Image.Clone();
                _strokeOutsideOrigin = c.Origin;
            }
        }
        if (_strokeOutside is not { } work || !new Rectangle(_strokeOutsideOrigin, work.Size).Contains(x, y)) work = GrowStrokeOutside(x, y, area);
        if (!work.Set(x - _strokeOutsideOrigin.X, y - _strokeOutsideOrigin.Y, color)) return false;
        StrokeOutsideVersion++;
        return true;
    }

    /// <summary>작업 그림을 (x, y)가 들어가게 넓힌다 — 칠할 때마다 다시 만들지 않게 둘레에 여유를 두고, 남기는 범위로 자른다.</summary>
    private PixelImage GrowStrokeOutside(int x, int y, Rectangle area)
    {
        const int pad = 32;
        var want = new Rectangle(x - pad, y - pad, 2 * pad + 1, 2 * pad + 1);
        var old = _strokeOutside;
        var oldRect = old is null ? Rectangle.Empty : new Rectangle(_strokeOutsideOrigin, old.Size);
        var rect = Rectangle.Intersect(old is null ? want : Rectangle.Union(oldRect, want), area);
        var image = new PixelImage(rect.Width, rect.Height);
        if (old is not null) image.Blit(old, oldRect.X - rect.X, oldRect.Y - rect.Y);
        _strokeOutside = image;
        _strokeOutsideOrigin = rect.Location;
        return image;
    }

    /// <summary>열린 스트로크에서 칠한 캔버스 밖 칸을 스트로크 시작 때로 되돌린다(도형처럼 끌 때마다 처음 그림 위에 다시 그리는 도구).</summary>
    public void ResetStrokeOutside()
    {
        if (_strokeOutside is not { } work) return;
        work.Clear();
        if (_strokeOverflowBefore is { } b) work.Blit(b.Image, b.Origin.X - _strokeOutsideOrigin.X, b.Origin.Y - _strokeOutsideOrigin.Y);
        StrokeOutsideVersion++;
    }

    /// <summary>
    /// 화면용 셀의 캔버스 밖 그림: 그 셀에 열린 스트로크가 캔버스 밖을 칠하는 중이면 칠한 것까지 보이는 작업 그림(스트로크 동안 바뀜 —
    /// <see cref="StrokeOutsideVersion"/>으로 알아챔, 고치지 않는다), 아니면 <see cref="GetCelOverflow"/>.
    /// </summary>
    public (PixelImage Image, Point Origin)? GetCelOverflowLive(int layerIndex, int frameIndex)
    {
        if (_strokeOutside is { } work && _stroke is { } s && ReferenceEquals(s.Key.Layer, _layers[layerIndex]) && ReferenceEquals(s.Key.Frame, _frames[frameIndex]))
            return (work, _strokeOutsideOrigin);
        return GetCelOverflow(layerIndex, frameIndex);
    }

    /// <summary>작업 그림을 캔버스 밖 그림으로 넘긴다(꼭 맞게 자르고 비면 없앰, 시작 때와 같으면 시작 때 그림 그대로).</summary>
    private void FlushStrokeOutside(CelKey key)
    {
        if (_strokeOutside is not { } work) return;
        _strokeOutside = null;
        StrokeOutsideVersion++;
        var overflow = OutsideOf(work, _strokeOutsideOrigin, Size);
        SetOverflow(key, SameOverflow(overflow, _strokeOverflowBefore) ? _strokeOverflowBefore : overflow);
    }

    /// <summary>
    /// 캔버스 밖 그림이 바뀐 스트로크를 확정: 셀을 시작 전으로 되돌린 뒤 바뀐 셀·캔버스 밖 그림을 구조 편집 한 단계로 넣는다
    /// (스트로크 단계는 셀 픽셀만 담으므로).
    /// </summary>
    private bool CommitOverflowStroke(StrokeState s, string label)
    {
        // 셀이 그대로면(캔버스 밖만 칠함) 셀 그림도 그대로 써서 실행취소 기록이 셀 사본을 갖지 않게 한다.
        var after = s.Image.ContentEquals(s.Before) ? s.Image : s.Image.Clone();
        var overflowAfter = _overflow.TryGetValue(s.Key, out var o) ? o : (CelOverflow?)null;
        s.Image.CopyFrom(s.Before);
        if (s.Created) _cels.Remove(s.Key);
        SetOverflow(s.Key, _strokeOverflowBefore);
        _strokeOverflowChanged = false;
        _strokeOverflowBefore = null;
        Edit(label, ProjectChange.Pixels, () =>
        {
            if (s.Created && !HasContent(after))
            {
                _cels.Remove(s.Key);
                SetOverflow(s.Key, overflowAfter);
            }
            else SetLinked(s.Key, after, overflowAfter);
        });
        return true;
    }

    /// <summary>스트로크 취소: 바꾼 캔버스 밖 그림을 시작 전으로.</summary>
    private void RevertStrokeOverflow(StrokeState s)
    {
        _strokeOutside = null;
        if (_strokeOverflowChanged) SetOverflow(s.Key, _strokeOverflowBefore);
        _strokeOverflowChanged = false;
        _strokeOverflowBefore = null;
    }

    /// <summary>레이어의 캔버스 밖 그림을 모두 지운다(레이어를 지울 때).</summary>
    private void RemoveOverflowOf(Layer layer)
    {
        foreach (var key in _overflow.Keys.Where(k => ReferenceEquals(k.Layer, layer)).ToList()) _overflow.Remove(key);
    }

    /// <summary>프레임의 캔버스 밖 그림을 모두 지운다(프레임을 지울 때).</summary>
    private void RemoveOverflowOf(Frame frame)
    {
        foreach (var key in _overflow.Keys.Where(k => ReferenceEquals(k.Frame, frame)).ToList()) _overflow.Remove(key);
    }

    /// <summary>프레임을 복제할 때 그 프레임의 캔버스 밖 그림도 새 프레임에(그림은 고치지 않으므로 같이 씀).</summary>
    private void CopyOverflow(Frame from, Frame to)
    {
        foreach (var (key, overflow) in _overflow.Where(kv => ReferenceEquals(kv.Key.Frame, from)).ToList())
            _overflow[key with { Frame = to }] = overflow;
    }

    /// <summary>
    /// 캔버스 크기·그림 크기를 바꿀 때 일반 레이어 셀 하나: 셀과 캔버스 밖 그림을 합친 그림 전체를 원래 캔버스가 <paramref name="image"/>(새 캔버스 좌표)에
    /// 놓이는 대응 하나로 늘이거나 줄여 옮긴 뒤(<see cref="ImageTransform.ScaleWithCanvas"/> — 캔버스 안·밖이 같은 배율이라 이음매 없음)
    /// 새 캔버스 안·밖으로 다시 나눈다(줄어든 캔버스 밖으로 나간 부분도 남음).
    /// </summary>
    private (PixelImage Cel, CelOverflow? Overflow) ResizeWithOverflow(CelKey key, PixelImage? cel, Rectangle image, Size oldCanvas, Size newCanvas,
        SpriteBorder slice, NineSliceFill fill)
    {
        var (full, origin) = FullOf(key, cel, oldCanvas);
        if (ImageTransform.ScaleWithCanvas(full, origin, oldCanvas, image, OverflowArea(newCanvas), slice, fill) is not { } moved)
            return (new PixelImage(newCanvas.Width, newCanvas.Height), null);
        return Split(moved.Image, moved.Origin, newCanvas);
    }

    /// <summary>위 레이어의 캔버스 밖 그림을 아래 레이어 셀(이미 합친 캔버스 안 셀 <paramref name="merged"/>)에 불투명도대로 합친다(아래로 합치기).</summary>
    private (PixelImage Cel, CelOverflow? Overflow) MergeOverflow(CelKey lowerKey, PixelImage merged, CelOverflow upper, byte opacity)
    {
        var (lowerFull, lowerOrigin) = FullOf(lowerKey, merged, Size);
        var rect = Rectangle.Union(new Rectangle(lowerOrigin, lowerFull.Size), upper.Rect);
        var full = new PixelImage(rect.Width, rect.Height);
        full.Blit(lowerFull, lowerOrigin.X - rect.X, lowerOrigin.Y - rect.Y);
        full.Composite(upper.Image, upper.Origin.X - rect.X, upper.Origin.Y - rect.Y, opacity);
        return Split(full, rect.Location, Size);
    }

    // ───────────── 파일 읽기·쓰기 ─────────────

    /// <summary>파일 쓰기용: 일반 레이어 셀의 캔버스 밖 그림.</summary>
    internal (PixelImage Image, Point Origin)? GetOverflowRaw(Layer layer, Frame frame)
        => layer.Kind == LayerKind.Normal && _overflow.TryGetValue(new(layer, frame, null), out var o) ? (o.Image, o.Origin) : null;

    /// <summary>파일 읽기용: 일반 레이어 셀의 캔버스 밖 그림을 넣는다(캔버스와 겹치는 칸은 지우고 꼭 맞게 자름, 남는 것이 없으면 넣지 않음).</summary>
    internal void SetOverflowRaw(Layer layer, Frame frame, PixelImage image, Point origin)
    {
        if (layer.Kind != LayerKind.Normal) return;
        SetOverflow(new(layer, frame, null), OutsideOf(image, origin, Size));
    }
}
