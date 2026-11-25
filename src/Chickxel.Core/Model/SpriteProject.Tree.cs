using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 레이어 나무: 부모·자식 읽기, 숨기기·잠그기 물려받기, 넣기·꺼내기·순서·복제·삭제(자손째), 자리 옮기기(자손이 따라감), 자식 자동 배치.
// 목록 규칙: 레이어의 자손은 그 레이어 바로 아래 번호들에 이어 붙어 있고 형제끼리는 목록 순서다(맨 아래 = 0). 나무에 드는 레이어는 자리 있는 레이어뿐이라
// 일반 레이어를 넣거나 자식을 받게 하면 같은 편집 안에서 먼저 자리 있는 레이어로 바꾼다(그림은 제자리 — NormalToPart).
// 레이어 객체는 그대로 두고 종류·부모·셀만 바꾸므로 선택·현재 레이어가 따라가고, 실행취소 사본(LayerState)이 모두 되돌린다.
public sealed partial class SpriteProject
{
    // ───────────── 나무 읽기 ─────────────

    /// <summary>바로 아래 자식들(아래 → 위, 목록 순서). 없거나 프로젝트에 없는 레이어면 빈 목록.</summary>
    public IReadOnlyList<Layer> ChildrenOf(Layer layer)
    {
        int i = IndexOfReference(_layers, layer);
        if (i <= 0) return [];
        int n = DescendantCount(_layers, i);
        if (n == 0) return [];
        var list = new List<Layer>();
        for (int j = i - n; j < i; j++)
            if (ReferenceEquals(_layers[j].Parent, layer)) list.Add(_layers[j]);
        return list;
    }

    /// <summary>자식이 있는지(자식이 있으면 바로 아래 번호가 맨 위 자식이다).</summary>
    public bool HasChildren(Layer layer)
    {
        int i = IndexOfReference(_layers, layer);
        return i > 0 && ReferenceEquals(_layers[i - 1].Parent, layer);
    }

    /// <summary>모든 자손(목록 순서 — 아래 → 위, 레이어 바로 아래 번호들). 없으면 빈 목록.</summary>
    public IReadOnlyList<Layer> DescendantsOf(Layer layer)
    {
        int i = IndexOfReference(_layers, layer);
        if (i <= 0) return [];
        int n = DescendantCount(_layers, i);
        return n == 0 ? [] : _layers.GetRange(i - n, n);
    }

    /// <summary>자손 수.</summary>
    public int DescendantCount(Layer layer)
    {
        int i = IndexOfReference(_layers, layer);
        return i <= 0 ? 0 : DescendantCount(_layers, i);
    }

    /// <summary>레이어 덩어리(자손 + 자신)의 맨 아래 번호: <paramref name="index"/> − 자손 수.</summary>
    public int SubtreeStart(int index) => index - DescendantCount(_layers, index);

    /// <summary>나무 깊이(최상위 = 0).</summary>
    public int DepthOf(Layer layer)
    {
        int depth = 0;
        for (var p = layer.Parent; p is not null; p = p.Parent) depth++;
        return depth;
    }

    /// <summary>조상들(가까운 부모부터 최상위까지). 최상위면 빈 목록.</summary>
    public IReadOnlyList<Layer> AncestorsOf(Layer layer)
    {
        var list = new List<Layer>();
        for (var p = layer.Parent; p is not null; p = p.Parent) list.Add(p);
        return list;
    }

    /// <summary><paramref name="ancestor"/>가 <paramref name="layer"/>의 조상(부모, 부모의 부모 …)인지. 자기 자신은 false.</summary>
    public bool IsAncestorOf(Layer ancestor, Layer layer) => IsAncestor(ancestor, layer);

    private static bool IsAncestor(Layer ancestor, Layer layer)
    {
        for (var p = layer.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// 그리는 차례(아래 → 위): 최상위 덩어리를 목록 순서대로, 덩어리 안에서는 부모 자신의 그림 다음에 자식 덩어리들(아래 → 위). 숨긴 레이어도 넣는다.
    /// Unity 프리팹의 그리는 순서(<c>sortingOrder</c>)가 이 차례의 번호다.
    /// </summary>
    public IReadOnlyList<Layer> DrawOrder()
    {
        var children = ChildMap();
        var order = new List<Layer>(_layers.Count);
        void Visit(Layer layer)
        {
            order.Add(layer);
            if (children.TryGetValue(layer, out var kids))
                foreach (var k in kids) Visit(k);
        }
        foreach (var layer in _layers)
            if (layer.Parent is null) Visit(layer);
        return order;
    }

    /// <summary>레이어 → 자식들(아래 → 위). 자식이 없는 레이어는 없음.</summary>
    private Dictionary<Layer, List<Layer>> ChildMap()
    {
        var map = new Dictionary<Layer, List<Layer>>();
        foreach (var layer in _layers)
            if (layer.Parent is { } parent)
            {
                if (!map.TryGetValue(parent, out var list)) map[parent] = list = [];
                list.Add(layer);
            }
        return map;
    }

    /// <summary>레이어 덩어리(자손 + 자신, 목록 순서). 프로젝트에 없으면 빈 목록.</summary>
    private List<Layer> SubtreeOf(Layer layer)
    {
        int i = IndexOfReference(_layers, layer);
        if (i < 0) return [];
        int start = i - DescendantCount(_layers, i);
        return _layers.GetRange(start, i - start + 1);
    }

    /// <summary><paramref name="list"/>의 <paramref name="index"/> 레이어의 자손 수(바로 아래로 이어 붙은 자손).</summary>
    private static int DescendantCount(List<Layer> list, int index)
    {
        var layer = list[index];
        int j = index - 1;
        while (j >= 0 && IsAncestor(layer, list[j])) j--;
        return index - 1 - j;
    }

    /// <summary>실제로 보이는지: 자신과 모든 조상이 보일 때.</summary>
    public bool IsLayerVisible(int index) => IsLayerVisible(_layers[index]);

    /// <inheritdoc cref="IsLayerVisible(int)"/>
    public bool IsLayerVisible(Layer layer) => IsVisibleInTree(layer);

    private static bool IsVisibleInTree(Layer layer)
    {
        for (Layer? l = layer; l is not null; l = l.Parent)
            if (!l.Visible) return false;
        return true;
    }

    /// <summary>실제로 잠겼는지: 자신이나 조상 중 하나라도 잠겼을 때.</summary>
    public bool IsLayerLocked(int index) => IsLayerLocked(_layers[index]);

    /// <inheritdoc cref="IsLayerLocked(int)"/>
    public bool IsLayerLocked(Layer layer) => IsLockedInTree(layer);

    private static bool IsLockedInTree(Layer layer)
    {
        for (Layer? l = layer; l is not null; l = l.Parent)
            if (l.Locked) return true;
        return false;
    }

    /// <summary>프레임에 그림(어느 스킨이든 내용 있는 셀)이 있는 잠긴 레이어(<see cref="IsLayerLocked(Layer)"/>)들, 아래 → 위. 그 프레임을 지우면 이 그림이 사라진다.</summary>
    public IReadOnlyList<Layer> LockedLayersWithContent(int frameIndex)
    {
        var frame = _frames[frameIndex];
        var found = new HashSet<Layer>();
        foreach (var (key, cel) in _cels)
            if (ReferenceEquals(key.Frame, frame) && IsLayerLocked(key.Layer) && HasContent(cel))
                found.Add(key.Layer);
        return found.Count == 0 ? [] : _layers.Where(found.Contains).ToList();
    }

    /// <summary>자기 셀에 입히는 효과: 자식이 있으면 <see cref="LayerEffect.IncludeChildren"/>가 아닌 것, 없으면 모든 효과(같은 목록이면 같은 객체).</summary>
    public IReadOnlyList<LayerEffect> OwnEffectsOf(Layer layer) => HasChildren(layer) ? layer.SplitEffects().Own : layer.Effects;

    /// <summary>자손까지 합친 그림에 입히는 효과: 자식이 있을 때 <see cref="LayerEffect.IncludeChildren"/>인 것(목록 순서), 없으면 빈 목록.</summary>
    public IReadOnlyList<LayerEffect> SubtreeEffectsOf(Layer layer) => HasChildren(layer) ? layer.SplitEffects().Subtree : [];

    // ───────────── 넣을 자리 ─────────────

    private static Layer RootOf(Layer layer)
    {
        while (layer.Parent is { } p) layer = p;
        return layer;
    }

    /// <summary>최상위 레이어를 넣을 자리를 덩어리를 쪼개지 않는 경계로 맞춘다(덩어리 안이면 그 최상위 레이어 바로 위).</summary>
    private int TopLevelInsertIndex(int index)
    {
        int at = Math.Clamp(index, 0, _layers.Count);
        if (at > 0 && _layers[at - 1].Parent is not null) at = IndexOfReference(_layers, RootOf(_layers[at - 1])) + 1;
        return at;
    }

    /// <summary>
    /// <paramref name="parent"/>(null = 최상위)의 형제 자리 <paramref name="slot"/>(아래에 둘 형제 수, 범위로 자름)에 덩어리를 넣을 목록 번호.
    /// </summary>
    private int SlotIndex(Layer? parent, int slot)
    {
        var siblings = parent is null ? _layers.Where(l => l.Parent is null).ToList() : ChildrenOf(parent);
        int k = Math.Clamp(slot, 0, siblings.Count);
        if (k > 0) return IndexOfReference(_layers, siblings[k - 1]) + 1;
        return parent is null ? 0 : SubtreeStart(IndexOfReference(_layers, parent));
    }

    private Layer RequirePart(int layerIndex)
    {
        var layer = _layers[layerIndex];
        return layer.IsPart ? layer : throw new ArgumentException(DomainStrings.Project_NotPartLayer, nameof(layerIndex));
    }

    /// <summary>자식을 받을 수 있는 레이어인지 확인(프로젝트에 있고 타일맵이 아님 — 일반 레이어는 받을 때 바뀜).</summary>
    private void RequireTreeParent(Layer parent)
    {
        if (IndexOfReference(_layers, parent) < 0) throw new ArgumentException(DomainStrings.Project_LayerNotInProject, nameof(parent));
        if (parent.IsTilemap) throw new InvalidOperationException(DomainStrings.Project_TilemapCannotNest);
    }

    // ───────────── 레이어 넣기 ─────────────

    /// <summary>빈 일반 레이어를 <paramref name="index"/>(기본: 맨 위, 덩어리 안이면 그 최상위 레이어 위)에 최상위로 넣는다. 피벗은 캔버스 기본 피벗.</summary>
    public Layer AddLayer(int? index = null, string? name = null)
    {
        int at = TopLevelInsertIndex(index ?? _layers.Count);
        var layer = new Layer(string.IsNullOrWhiteSpace(name) ? NextLayerName() : UniqueLayerName(name.Trim())) { Pivot = Sprite.DefaultPivot };
        Edit(DomainStrings.Project_AddLayer, ProjectChange.Layers, () => _layers.Insert(at, layer));
        return layer;
    }

    /// <summary>
    /// 빈 자리 있는 레이어(셀 없음)를 넣는다: <paramref name="parent"/>의 자식(null = 최상위)으로 형제 자리 <paramref name="slot"/>(아래에 둘 형제 수, 기본 = 맨 위)에.
    /// 위치를 안 주면 캔버스 가운데. 이름이 비면 새 레이어 이름, 있으면 겹치지 않게 바뀐다. 부모가 일반 레이어면 같은 단계에서 자리 있는 레이어로 바꾼다(그림은 제자리).
    /// 예전 "빈 그룹"은 <c>AddPart(null, name, Width, Height, Point.Empty)</c>, 예전 "부위 추가"는 부모를 주고 부른다.
    /// 크기가 잘못되면 <see cref="ArgumentException"/>, 부모가 타일맵이면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public Layer AddPart(Layer? parent, string? name, int width, int height, Point? offset = null, int? slot = null)
    {
        if (parent is not null) RequireTreeParent(parent);
        ValidatePartSize(width, height);
        var part = new Layer(string.IsNullOrWhiteSpace(name) ? NextLayerName() : UniqueLayerName(name.Trim()))
        {
            Kind = LayerKind.Part,
            PartSize = new Size(width, height),
            PartOffset = offset ?? new Point((_width - width) / 2, (_height - height) / 2),
        };
        Edit(DomainStrings.Project_AddLayer, ProjectChange.Layers, () =>
        {
            if (parent is { Kind: LayerKind.Normal }) NormalToPart(parent);
            part.Parent = parent;
            _layers.Insert(SlotIndex(parent, slot ?? int.MaxValue), part);
        });
        return part;
    }

    // ───────────── 넣기·꺼내기 ─────────────

    /// <summary>
    /// 레이어들을 <paramref name="parent"/>의 자식으로(null = 최상위로) 옮긴다(실행취소 한 단계, 자손째). <paramref name="slot"/> = 넣을 형제 자리
    /// (옮기지 않는 나머지 형제 중 아래에 둘 수, 0 = 맨 아래, 범위로 자름). 옮기는 레이어끼리의 순서는 그대로이고, 고른 것 중 다른 고른 레이어의 자손은 그 조상과 함께 옮긴다.
    /// 캔버스의 자리·그림·스킨은 그대로다: 부모에 넣는 일반 레이어와 자식을 받는 일반 레이어는 같은 단계에서 자리 있는 레이어로 바뀐다(모든 프레임·스킨 그림을 감싸는 영역 —
    /// 빈 레이어나 원본 없이 그리는 효과가 있으면 캔버스까지). 최상위로 꺼낸 자리 있는 레이어는 그대로 자리 있는 레이어다.
    /// 자기 자신이나 자손 밑으로 넣으면 <see cref="ArgumentException"/>, 타일맵이 끼거나 잠긴 레이어(조상 포함)·잠긴 부모면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void MoveLayersTo(IReadOnlyList<Layer> layers, Layer? parent, int slot)
    {
        var moving = TreeSources(layers);
        if (parent is not null)
        {
            RequireTreeParent(parent);
            if (moving.Any(m => ReferenceEquals(m, parent) || IsAncestorOf(m, parent)))
                throw new ArgumentException(DomainStrings.Project_CannotNestIntoItself, nameof(parent));
            if (moving.Any(m => m.IsTilemap)) throw new InvalidOperationException(DomainStrings.Project_TilemapCannotNest);
            if (IsLayerLocked(parent)) throw new InvalidOperationException(DomainStrings.FormatProject_NestLocked(name: parent.Name));
        }
        var label = parent is null ? DomainStrings.Project_MoveOutOfParent : DomainStrings.Project_MoveIntoParent;
        Edit(label, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            if (parent is { Kind: LayerKind.Normal }) NormalToPart(parent);
            var blocks = moving.Select(SubtreeOf).ToList();
            foreach (var m in moving)
            {
                if (parent is not null && m.Kind == LayerKind.Normal) NormalToPart(m);
                m.Parent = parent;
            }
            foreach (var block in blocks)
                foreach (var l in block) _layers.Remove(l);
            _layers.InsertRange(SlotIndex(parent, slot), blocks.SelectMany(b => b));
        });
    }

    /// <summary>
    /// 레이어들을 부모에서 한 단계 바깥으로 꺼낸다(실행취소 한 단계, 자손째): 저마다 부모의 부모 아래(없으면 최상위), 원래 부모 바로 위 자리로.
    /// 부모가 없는 레이어는 그대로. 꺼낼 것이 없으면 기록 없음. 잠긴 레이어(조상 포함)면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void MoveOutOfParent(IReadOnlyList<Layer> layers)
    {
        var moving = TreeSources(layers).Where(l => l.Parent is not null).ToList();
        if (moving.Count == 0) return;
        Edit(DomainStrings.Project_MoveOutOfParent, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            // 위에 있는 것부터 꺼내면 같은 부모에서 꺼낸 것끼리 원래 순서(아래 → 위)가 지켜진다
            foreach (var layer in Enumerable.Reverse(moving))
            {
                var old = layer.Parent!;
                var grand = old.Parent;
                var siblings = grand is null ? _layers.Where(l => l.Parent is null).ToList() : ChildrenOf(grand).ToList();
                MoveLayersTo([layer], grand, siblings.IndexOf(old) + 1);
            }
        });
    }

    /// <summary>넣거나 꺼낼 레이어를 확인해 목록 순서(아래 → 위)로 돌려준다(고른 다른 레이어의 자손은 빼고 — 조상과 함께 옮겨짐).</summary>
    private List<Layer> TreeSources(IReadOnlyList<Layer> layers)
    {
        var list = layers.Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException(DomainStrings.Project_NoLayersToNest, nameof(layers));
        foreach (var layer in list)
        {
            if (IndexOfReference(_layers, layer) < 0) throw new ArgumentException(DomainStrings.Project_LayerNotInProject, nameof(layers));
            if (IsLayerLocked(layer)) throw new InvalidOperationException(DomainStrings.FormatProject_NestLocked(name: layer.Name));
        }
        return list.Where(l => !list.Any(o => IsAncestorOf(o, l))).OrderBy(l => IndexOfReference(_layers, l)).ToList();
    }

    /// <summary>
    /// 일반 레이어를 모든 프레임·스킨 그림(캔버스 밖 그림 포함)을 감싸는 영역의 자리 있는 레이어로 바꾼다(그림은 캔버스의 같은 자리에 남음, 부모는 부르는 쪽이 정함).
    /// 빈 레이어이거나 원본 없이 그리는 효과(글씨 등)가 있으면 캔버스까지 감싸고, 그 효과가 켜진 셀 없는 프레임에는 빈 셀을 둔다.
    /// 레이어 피벗(기본·프레임마다)은 자기 기준으로 바꿔 같은 그림 지점을 가리킨다. <see cref="Edit(string, ProjectChange, Action)"/> 안에서.
    /// </summary>
    private void NormalToPart(Layer layer)
    {
        var keys = CelsOf(layer).Select(c => c.Key).Union(_overflow.Keys.Where(k => ReferenceEquals(k.Layer, layer))).ToList();
        var fulls = new List<(CelKey Key, PixelImage? Cel, PixelImage Full, Point Origin)>(keys.Count);
        var area = Rectangle.Empty;
        foreach (var key in keys)
        {
            var cel = _cels.GetValueOrDefault(key);
            var (full, origin) = FullOf(key, cel, Size);
            var opaque = full.GetOpaqueBounds();
            if (!opaque.IsEmpty)
            {
                opaque.Offset(origin);
                area = area.IsEmpty ? opaque : Rectangle.Union(area, opaque);
            }
            fulls.Add((key, cel, full, origin));
        }
        bool generates = Enumerable.Range(0, _frames.Count).Any(f => LayerEffects.GeneratesContent(layer.Effects, f));
        if (area.IsEmpty) area = Bounds;
        else if (generates) area = Rectangle.Union(area, Bounds);
        ValidatePartSize(area.Width, area.Height);

        var cropped = new Dictionary<PixelImage, PixelImage>(ReferenceEqualityComparer.Instance); // 링크된 셀은 같은 새 그림을 같이 씀
        foreach (var (key, cel, full, origin) in fulls)
        {
            if (cel is null || !cropped.TryGetValue(cel, out var image))
            {
                image = new PixelImage(area.Width, area.Height);
                image.Blit(full, origin.X - area.X, origin.Y - area.Y);
                if (cel is not null)
                {
                    cropped[cel] = image;
                    InheritLinkTag(cel, image);
                }
            }
            _cels[key] = image;
            _overflow.Remove(key);
        }
        if (generates)
            for (int f = 0; f < _frames.Count; f++)
            {
                var key = new CelKey(layer, _frames[f], ActiveKey(layer));
                if (!_cels.ContainsKey(key) && LayerEffects.GeneratesContent(layer.Effects, f)) _cels[key] = new PixelImage(area.Width, area.Height);
            }
        layer.Kind = LayerKind.Part;
        layer.PartSize = area.Size;
        layer.PartOffset = area.Location;
        MapLayerPivots(layer, p => CanvasPivotToPart(p, area));
        foreach (var f in _frames) f.PartShifts.Remove(layer);
        layer.PartBorder = default;
        layer.PartSourceRect = null;
    }

    // ───────────── 복제·삭제·순서 ─────────────

    /// <summary>
    /// 레이어를 자손·셀·스킨까지 복사해 그 덩어리 바로 위(같은 부모의 형제)에 넣는다. 복사본의 스킨은 새 스킨 객체이고(이름·순서·지금 스킨 같음),
    /// 스킨 프리셋은 원래 레이어만 가리킨다. 복사한 맨 위 레이어를 돌려준다.
    /// </summary>
    public Layer DuplicateLayer(int index)
    {
        var src = _layers[index];
        return Edit(DomainStrings.Project_DuplicateLayer, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var block = SubtreeOf(src);
            var map = new Dictionary<Layer, Layer>();
            var copies = block.Select(l => map[l] = CopyLayer(l)).ToList();
            foreach (var (from, to) in map) to.Parent = from.Parent is { } p && map.TryGetValue(p, out var mp) ? mp : from.Parent;
            _layers.InsertRange(index + 1, copies);
            return map[src];
        });
    }

    /// <summary>
    /// 레이어 하나를 셀·스킨까지 복사한 새 레이어(목록에는 넣지 않음, 부모는 원래 부모 — 부르는 쪽이 바꿈, 원래 칸 기억은 복사하지 않음).
    /// 스킨은 새 객체로 복사하고 셀 키를 그 스킨으로 바꾼다.
    /// </summary>
    private Layer CopyLayer(Layer src)
    {
        var copy = new Layer(NextNumberedName(src.Name, n => _layers.Any(l => l.Name == n)))
        {
            Visible = src.Visible,
            Locked = false,
            Opacity = src.Opacity,
            Kind = src.Kind,
            Parent = src.Parent,
            Collapsed = src.Collapsed,
            EffectsCollapsed = src.EffectsCollapsed,
            AutoLayout = src.AutoLayout,
            PartSize = src.PartSize,
            PartOffset = src.PartOffset,
            Pivot = src.Pivot,
            PartBorder = src.PartBorder,
            Effects = src.Effects,
            Tiles = src.Tiles,
        };
        var skinMap = new Dictionary<Skin, Skin>();
        if (!src.IsTilemap)
        {
            foreach (var s in src.SkinList) skinMap[s] = new Skin(s.Name);
            copy.ReplaceSkinsRaw(src.SkinList.Select(s => skinMap[s]));
            copy.SetActiveSkinRaw(skinMap[src.ActiveSkin!]);
        }
        Skin? MapSkin(Skin? s) => s is not null && skinMap.TryGetValue(s, out var mapped) ? mapped : s;
        var clone = OncePerImage(cel => cel.Clone()); // 링크된 셀은 사본에서도 링크
        foreach (var (key, cel) in CelsOf(src)) _cels[new(copy, key.Frame, MapSkin(key.Skin))] = clone(cel);
        foreach (var f in _frames)
        {
            if (f.LayerPivots.TryGetValue(src, out var own)) f.LayerPivots[copy] = own;
            if (f.PartShifts.TryGetValue(src, out var shift)) f.PartShifts[copy] = shift;
        }
        foreach (var (key, overflow) in _overflow.Where(kv => ReferenceEquals(kv.Key.Layer, src)).ToList())
            _overflow[new CelKey(copy, key.Frame, MapSkin(key.Skin))] = overflow;
        return copy;
    }

    /// <summary>레이어 삭제(자손째, 셀·캔버스 밖 그림까지). 남는 레이어가 없으면 <see cref="InvalidOperationException"/>. 스킨 프리셋의 그 레이어 항목은 쓰이지 않게 된다.</summary>
    public void DeleteLayer(int index)
    {
        int start = SubtreeStart(index);
        int count = index - start + 1;
        if (_layers.Count - count < 1) throw new InvalidOperationException(DomainStrings.Project_CannotDeleteLastLayer);
        Edit(DomainStrings.Project_DeleteLayer, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            foreach (var l in _layers.GetRange(start, count))
            {
                foreach (var (key, _) in CelsOf(l)) _cels.Remove(key);
                RemoveOverflowOf(l);
            }
            if (PivotLayer is { } rep && IndexOfReference(_layers, rep) is var ri && ri >= start && ri < start + count) PivotLayer = null;
            _layers.RemoveRange(start, count);
        });
    }

    /// <summary>
    /// 레이어 순서 바꾸기: 레이어 덩어리(자손째)를 같은 부모의 형제 사이에서 옮긴다. <paramref name="to"/>는 덩어리를 뺀 목록에서 덩어리 맨 아래가 들어갈 번호
    /// (같은 부모의 범위로 자르고, 다른 형제 덩어리 안이면 가까운 경계로 맞춤). 자손이 없는 레이어면 옮긴 뒤 번호와 같다.
    /// 부모를 바꾸려면 <see cref="MoveLayersTo"/>.
    /// </summary>
    public void MoveLayer(int from, int to)
    {
        var layer = _layers[from];
        int start = SubtreeStart(from);
        int count = from - start + 1;
        var rest = new List<Layer>(_layers);
        rest.RemoveRange(start, count);
        var parent = layer.Parent;
        int lo = 0, hi = rest.Count;
        if (parent is not null)
        {
            int pi = IndexOfReference(rest, parent);
            lo = pi - DescendantCount(rest, pi);
            hi = pi;
        }
        int at = Math.Clamp(to, lo, hi);
        if (at > lo && !ReferenceEquals(rest[at - 1].Parent, parent))
        {
            // 다른 형제 덩어리 안: 그 형제 덩어리의 가까운 경계로
            var sibling = rest[at - 1];
            while (!ReferenceEquals(sibling.Parent, parent)) sibling = sibling.Parent!;
            int top = IndexOfReference(rest, sibling);
            int bottom = top - DescendantCount(rest, top);
            int below = at - bottom, above = top + 1 - at;
            at = below < above || (below == above && at < start) ? bottom : top + 1;
        }
        if (at == start) return;
        var block = _layers.GetRange(start, count);
        Edit(DomainStrings.Project_MoveLayer, ProjectChange.Layers, () =>
        {
            _layers.RemoveRange(start, count);
            _layers.InsertRange(at, block);
        });
    }

    /// <summary>한 칸 위로(▲): 바로 위 형제 덩어리와 자리를 바꾼다(자손째). 형제 중 맨 위면 그대로.</summary>
    public void MoveLayerUp(int index)
    {
        var layer = _layers[index];
        if (index == _layers.Count - 1 || ReferenceEquals(_layers[index + 1], layer.Parent)) return;
        var above = _layers[index + 1];
        while (!ReferenceEquals(above.Parent, layer.Parent)) above = above.Parent!;
        MoveLayer(index, SubtreeStart(index) + (IndexOfReference(_layers, above) - index));
    }

    /// <summary>한 칸 아래로(▼): 바로 아래 형제 덩어리와 자리를 바꾼다(자손째). 형제 중 맨 아래면 그대로.</summary>
    public void MoveLayerDown(int index)
    {
        var layer = _layers[index];
        int start = SubtreeStart(index);
        if (start == 0 || !ReferenceEquals(_layers[start - 1].Parent, layer.Parent)) return;
        MoveLayer(index, SubtreeStart(start - 1));
    }

    /// <summary>자식 줄 접기/펼치기(타임라인 표시용, 실행취소·수정됨 표시 없음). 아무 레이어나(자식이 없으면 보이는 차이 없음).</summary>
    public void SetLayerCollapsed(int layerIndex, bool collapsed)
    {
        var layer = _layers[layerIndex];
        if (layer.Collapsed == collapsed) return;
        layer.Collapsed = collapsed;
        NotifyChanged(ProjectChange.Layers, markDirty: false);
    }

    // ───────────── 자리 옮기기(자손이 따라감) ─────────────

    /// <summary>
    /// 자리 있는 레이어의 기준 자리(캔버스 안 왼쪽 위 — 프레임마다 옮긴 거리는 그대로 두므로 모든 프레임이 같이 옮겨 감). 자손도 같은 거리만큼 옮긴다.
    /// 부모가 자동 배치 중이면 <see cref="InvalidOperationException"/>(먼저 <see cref="SetChildLayout"/>로 끄기). 한 프레임부터만 옮기려면 <see cref="MovePartFrom"/>.
    /// </summary>
    public void SetPartOffset(int layerIndex, Point offset)
    {
        var part = RequirePart(layerIndex);
        if (part.PartOffset == offset) return;
        if (part.IsAutoLaidOut)
            throw new InvalidOperationException(DomainStrings.FormatProject_AutoLayoutBlocksMove(name: part.Parent!.Name));
        var delta = new Point(offset.X - part.PartOffset.X, offset.Y - part.PartOffset.Y);
        Edit(DomainStrings.Project_MovePart, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            part.PartOffset = offset;
            MoveDescendants(part, delta, null);
        });
    }

    /// <summary>
    /// 자리 있는 레이어를 프레임 <paramref name="fromFrame"/>부터 끝까지 (<paramref name="dx"/>, <paramref name="dy"/>)px 옮긴다(실행취소 한 단계, 자손째). 피벗은 자기 기준 비율이라 같이 옮겨 간다.
    /// 첫 프레임부터면 기준 자리를 옮기고(부모가 자동 배치 중인 레이어는 빼고 — 그것은 모든 프레임의 옮긴 거리로), 아니면 그 프레임들에 옮긴 거리를 더한다(앞 프레임은 그대로).
    /// </summary>
    public void MovePartFrom(int layerIndex, int fromFrame, int dx, int dy)
    {
        var part = RequirePart(layerIndex);
        var frames = FramesFrom(fromFrame);
        if (dx == 0 && dy == 0) return;
        Edit(DomainStrings.Project_MovePart, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var delta = new Point(dx, dy);
            foreach (var l in SubtreeOf(part)) MoveOne(l, delta, fromFrame == 0 ? null : frames);
        });
    }

    /// <summary>
    /// 레이어 하나를 옮긴다(<see cref="Edit(string, ProjectChange, Action)"/> 안에서): <paramref name="frames"/>가 null이면 모든 프레임 — 기준 자리를 옮기되,
    /// 부모가 자동 배치 중이면(다시 배치가 기준 자리를 덮으므로) 모든 프레임의 옮긴 거리로. 아니면 그 프레임들의 옮긴 거리에 더한다.
    /// </summary>
    private void MoveOne(Layer layer, Point delta, IReadOnlyList<Frame>? frames)
    {
        if (!layer.IsPart || delta.IsEmpty) return;
        if (frames is null && !layer.IsAutoLaidOut)
        {
            layer.PartOffset = new Point(layer.PartOffset.X + delta.X, layer.PartOffset.Y + delta.Y);
            return;
        }
        foreach (var f in frames ?? _frames)
        {
            var s = f.PartShifts.GetValueOrDefault(layer);
            SetShift(f, layer, new Point(s.X + delta.X, s.Y + delta.Y));
        }
    }

    /// <summary>
    /// 자손을 모두 <paramref name="delta"/>만큼 옮긴다(<paramref name="frames"/> null = 모든 프레임, <see cref="MoveOne"/>). 부모를 옮기거나 프레임 자리를 바꾸는 편집이 부른다.
    /// </summary>
    private void MoveDescendants(Layer layer, Point delta, IReadOnlyList<Frame>? frames)
    {
        if (delta.IsEmpty) return;
        foreach (var d in DescendantsOf(layer)) MoveOne(d, delta, frames);
    }

    // ───────────── 자식 자동 배치 ─────────────

    /// <summary>
    /// 자식 자동 배치 켜기·설정 바꾸기(<paramref name="layout"/>, 범위 밖 값은 맞춤) 또는 끄기(null — 자식은 지금 자리에 그대로). 실행취소 한 단계, 같으면 기록 없음.
    /// 켜면 바로 아래 자식들의 자리를 바로 다시 계산하고(자식은 자손째 옮겨 감), 그 뒤 편집마다 다시 맞춘다. 자리 있는 레이어가 아니면 <see cref="ArgumentException"/>
    /// (일반 레이어는 자식이 없으므로 — 자식을 넣으면 자리 있는 레이어가 됨).
    /// </summary>
    public void SetChildLayout(Layer parent, GroupLayout? layout)
    {
        if (IndexOfReference(_layers, parent) < 0) throw new ArgumentException(DomainStrings.Project_LayerNotInProject, nameof(parent));
        if (!parent.IsPart) throw new ArgumentException(DomainStrings.Project_NotPartLayer, nameof(parent));
        layout = layout?.Normalized();
        if (Equals(parent.AutoLayout, layout)) return;
        var label = layout is null ? DomainStrings.Project_DisableAutoLayout : parent.AutoLayout is null ? DomainStrings.Project_AutoLayout : DomainStrings.Project_AutoLayoutSettings;
        Edit(label, ProjectChange.Layers | ProjectChange.Pixels, () => parent.AutoLayout = layout);
    }

    /// <summary>
    /// <paramref name="layout"/>대로 놓았을 때 바로 아래 자식들의 기준 자리(넣는 순서, 숨긴 자식을 빼면 그 자식은 없음). 프로젝트는 바꾸지 않는다(미리 보기·다시 배치용).
    /// </summary>
    public IReadOnlyList<(Layer Child, Point Offset)> ComputeChildLayout(Layer parent, GroupLayout layout)
    {
        var children = ChildrenOf(parent).Where(l => l.IsPart && (layout.IncludeHidden || l.Visible)).ToList();
        if (layout.Order == GroupLayoutOrder.TopFirst) children.Reverse();
        var offsets = layout.Arrange(children.Select(l => l.PartSize).ToList(), Bounds);
        return children.Select((l, i) => (l, offsets[i])).ToList();
    }

    /// <summary>
    /// 자동 배치가 켜진 레이어마다 자식 자리를 다시 맞춘다(편집 안에서만 — <see cref="Edit(string, ProjectChange, Action)"/> 끝). 자식을 옮기면 그 자손도 같은 거리만큼.
    /// 위 레이어(부모)부터 맞추므로 안쪽 배치가 마지막이다. 옮긴 것이 있으면 바뀐 부분.
    /// </summary>
    private ProjectChange RelayoutChildren()
    {
        bool moved = false;
        for (int i = _layers.Count - 1; i >= 0; i--)
        {
            if (_layers[i] is not { IsPart: true, AutoLayout: { } layout } parent) continue;
            foreach (var (child, offset) in ComputeChildLayout(parent, layout))
            {
                if (child.PartOffset == offset) continue;
                var delta = new Point(offset.X - child.PartOffset.X, offset.Y - child.PartOffset.Y);
                child.PartOffset = offset;
                MoveDescendants(child, delta, null);
                moved = true;
            }
        }
        return moved ? ProjectChange.Layers | ProjectChange.Pixels : ProjectChange.None;
    }

    // ───────────── 파일 읽기용 ─────────────

    /// <summary>
    /// 파일 읽기용: 나무를 바로잡는다 — 자리 있는 레이어가 아니거나 프로젝트에 없는 부모, 자리 있는 레이어가 아닌 자식(일반·타일맵), 고리를 끊어 최상위로 두고,
    /// 자손을 그 레이어 바로 아래로 모은다(형제·최상위 순서는 원래 목록 순서). 바꾼 것이 있으면 true.
    /// </summary>
    internal bool NormalizeHierarchy()
    {
        bool changed = false;
        var set = new HashSet<Layer>(_layers, ReferenceEqualityComparer.Instance);
        foreach (var layer in _layers)
        {
            if (layer.Parent is not { } parent) continue;
            if (!layer.IsPart || !parent.IsPart || !set.Contains(parent))
            {
                layer.Parent = null;
                changed = true;
                continue;
            }
            var seen = new HashSet<Layer>(ReferenceEqualityComparer.Instance) { layer };
            for (var p = layer.Parent; p is not null; p = p.Parent)
                if (!seen.Add(p))
                {
                    layer.Parent = null;
                    changed = true;
                    break;
                }
        }
        var children = ChildMap();
        var ordered = new List<Layer>(_layers.Count);
        void Emit(Layer layer)
        {
            if (children.TryGetValue(layer, out var kids))
                foreach (var k in kids) Emit(k);
            ordered.Add(layer);
        }
        foreach (var layer in _layers.ToList())
            if (layer.Parent is null) Emit(layer);
        if (!ordered.SequenceEqual(_layers))
        {
            _layers.Clear();
            _layers.AddRange(ordered);
            changed = true;
        }
        return changed;
    }
}
