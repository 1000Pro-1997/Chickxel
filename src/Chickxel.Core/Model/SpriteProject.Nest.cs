using System.Drawing;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 그룹에 넣기·꺼내기: 일반 레이어(또는 다른 그룹의 부위)를 그룹의 부위로 바꾸고, 부위를 그룹 밖 일반 레이어로 바꾼다.
// 레이어 객체는 그대로 두고 종류·부모·셀만 바꾸므로 선택·현재 레이어가 따라가고, 실행취소 사본(LayerState)이 모두 되돌린다.
public sealed partial class SpriteProject
{
    /// <summary>
    /// 일반 레이어에 스킨을 단다(실행취소 한 단계): 레이어 자리에 그 이름의 새 그룹을 만들어 레이어를 부위로 넣고(그림·효과는 제자리 — <see cref="MoveIntoGroup"/>,
    /// 지금 그림은 첫 스킨 "기본"), 그룹에 새 스킨을 더해 지금 스킨으로 고른다(<paramref name="copy"/>면 지금 그림을 복제, 아니면 빈 스킨).
    /// 만든 그룹을 돌려준다. 일반 레이어가 아니면 <see cref="ArgumentException"/>, 잠겼으면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public Layer AddSkinToLayer(Layer layer, bool copy)
    {
        if (!_layers.Contains(layer) || layer.Kind != LayerKind.Normal) throw new ArgumentException(DomainStrings.Project_AddSkinRequiresGroup, nameof(layer));
        if (layer.Locked) throw new InvalidOperationException(DomainStrings.FormatProject_NestLocked(name: layer.Name));
        Layer group = null!;
        Edit(DomainStrings.Project_AddSkin, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            group = AddGroup(layer.Name, _layers.IndexOf(layer) + 1);
            MoveIntoGroup([layer], group, 0);
            AddSkin(group, null, copy ? group.BaseSkin : null);
        });
        return group;
    }

    /// <summary>
    /// 레이어들(일반 레이어, 또는 다른 그룹의 부위)을 <paramref name="group"/>의 부위로 넣는다(실행취소 한 단계). <paramref name="slot"/> = 넣을 자리
    /// (그룹의 나머지 부위 중 아래에 둘 부위 수, 0 = 맨 아래). 넣는 레이어끼리의 순서는 그대로다.
    /// 일반 레이어는 모든 프레임 그림(캔버스 밖 그림 포함)을 감싸는 영역이 부위가 되어 그림이 제자리에 남는다(빈 레이어나 원본 없이 그리는 효과가 있으면
    /// 캔버스까지 감쌈). 그림은 그룹의 첫 스킨 셀이 된다. 다른 그룹의 부위는 지금 보이는 스킨 그림만 첫 스킨 셀로 가져가고 원래 그룹의 다른 스킨 셀과
    /// 그 부위 전용 스킨은 없어진다(<see cref="HasHiddenSkinContent"/>). 그룹·잠긴 레이어가 끼거나 그룹이 잠겼으면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void MoveIntoGroup(IReadOnlyList<Layer> layers, Layer group, int slot)
    {
        if (!group.IsGroup || !_layers.Contains(group)) throw new ArgumentException(DomainStrings.Project_GroupNotInProject, nameof(group));
        var moving = NestSources(layers);
        if (moving.Any(l => ReferenceEquals(l.Parent, group))) throw new ArgumentException(DomainStrings.Project_AlreadyInGroup, nameof(layers));
        if (group.Locked) throw new InvalidOperationException(DomainStrings.FormatProject_NestLocked(name: group.Name));
        Edit(DomainStrings.Project_MoveIntoGroup, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            foreach (var layer in moving)
            {
                if (layer.IsPart)
                    foreach (var (frame, cel) in TakeShownCels(layer)) _cels[new(layer, frame, null)] = cel;
                else NormalToPart(layer);
                DetachFromOutfits(layer);
                layer.Parent = group;
            }
            foreach (var layer in moving) _layers.Remove(layer);
            int n = ChildCount(_layers.IndexOf(group));
            _layers.InsertRange(_layers.IndexOf(group) - n + Math.Clamp(slot, 0, n), moving);
        });
    }

    /// <summary>
    /// 부위들을 그룹 밖 일반 레이어로 꺼낸다(실행취소 한 단계). <paramref name="slot"/> = 넣을 자리(나머지 최상위 레이어·그룹 중 아래에 둘 수, 0 = 맨 아래).
    /// 지금 보이는 스킨 그림이 캔버스의 같은 자리에 남고(캔버스 밖으로 나간 부분은 캔버스 밖 그림), 다른 스킨 셀·그 부위 전용 스킨·피벗·9-slice 테두리는
    /// 없어진다(<see cref="HasHiddenSkinContent"/>). 부위가 아니면 <see cref="ArgumentException"/>, 잠겼으면 <see cref="InvalidOperationException"/>.
    /// </summary>
    public void MoveOutOfGroup(IReadOnlyList<Layer> parts, int slot)
    {
        var moving = NestSources(parts);
        if (moving.Any(l => !l.IsPart)) throw new ArgumentException(DomainStrings.Project_NotPartLayer, nameof(parts));
        Edit(DomainStrings.Project_MoveOutOfGroup, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            foreach (var part in moving)
            {
                var origin = part.PartOffset;
                var split = new Dictionary<PixelImage, (PixelImage Cel, CelOverflow? Overflow)>(ReferenceEqualityComparer.Instance);
                foreach (var (frame, cel) in TakeShownCels(part))
                {
                    if (!split.TryGetValue(cel, out var s))
                    {
                        split[cel] = s = Split(cel, origin, Size);
                        InheritLinkTag(cel, s.Cel); // 링크된 셀은 꺼낸 뒤에도 같은 그림을 같이 씀
                    }
                    var key = new CelKey(part, frame, null);
                    _cels[key] = s.Cel;
                    SetOverflow(key, s.Overflow);
                }
                DetachFromOutfits(part);
                part.Kind = LayerKind.Normal;
                part.Parent = null;
                part.PartSize = default;
                part.PartOffset = default;
                part.PartPivot = Pivot.Center;
                foreach (var f in _frames) f.PartPivots.Remove(part);
                part.PartBorder = default;
                part.PartSourceRect = null;
            }
            foreach (var part in moving) _layers.Remove(part);
            var top = _layers.Where(l => l.Parent is null).ToList();
            int k = Math.Clamp(slot, 0, top.Count);
            _layers.InsertRange(k == 0 ? 0 : _layers.IndexOf(top[k - 1]) + 1, moving);
        });
    }

    /// <summary>
    /// 부위를 그룹에서 꺼내거나 다른 그룹으로 옮기면 없어질 그림이 있는지: 지금 보이는 스킨이 아닌 스킨 셀에 그림이 있으면 true.
    /// 부위가 아니면 false.
    /// </summary>
    public bool HasHiddenSkinContent(Layer part)
    {
        if (!part.IsPart) return false;
        var shown = ActiveKey(part);
        foreach (var (key, cel) in CelsOf(part))
            if (HasContent(cel) && !ReferenceEquals(key.Skin, shown)) return true;
        return false;
    }

    /// <summary>넣거나 꺼낼 레이어를 확인해 목록 순서(아래 → 위)로 돌려준다.</summary>
    private List<Layer> NestSources(IReadOnlyList<Layer> layers)
    {
        var list = layers.Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException(DomainStrings.Project_NoLayersToNest, nameof(layers));
        foreach (var layer in list)
        {
            int i = _layers.IndexOf(layer);
            if (i < 0) throw new ArgumentException(DomainStrings.Project_LayerNotInProject, nameof(layers));
            if (layer.IsGroup) throw new InvalidOperationException(DomainStrings.Project_GroupCannotNest);
            if (IsLayerLocked(i)) throw new InvalidOperationException(DomainStrings.FormatProject_NestLocked(name: layer.Name));
        }
        return list.OrderBy(_layers.IndexOf).ToList();
    }

    /// <summary>
    /// 부위의 지금 보이는 스킨 자신의 그림을 프레임마다 돌려주고, 그 부위의 셀을 모두 빼고 그 부위 전용 스킨을 그룹에서 지운다
    /// (<see cref="Edit(string, ProjectChange, Action)"/> 안에서, 부모를 바꾸기 전에).
    /// </summary>
    private List<(Frame Frame, PixelImage Cel)> TakeShownCels(Layer part)
    {
        var shown = ActiveKey(part);
        var cels = new List<(Frame, PixelImage)>();
        foreach (var frame in _frames)
            if (_cels.GetValueOrDefault(new(part, frame, shown)) is { } cel) cels.Add((frame, cel));
        foreach (var (key, _) in CelsOf(part)) _cels.Remove(key);
        if (part.Parent is { IsGroup: true } g) g.ReplaceSkinsRaw(g.SkinList.Where(s => !ReferenceEquals(s.Owner, part)).ToList());
        return cels;
    }

    /// <summary>
    /// 일반 레이어를 모든 프레임 그림(캔버스 밖 그림 포함)을 감싸는 영역의 부위로 바꾼다(그림은 캔버스의 같은 자리에 남음, 그룹은 부르는 쪽이 정함).
    /// 빈 레이어이거나 원본 없이 그리는 효과(글씨 등)가 있으면 캔버스까지 감싸고, 그 효과가 켜진 셀 없는 프레임에는 빈 셀을 둔다.
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
                var key = new CelKey(layer, _frames[f], null);
                if (!_cels.ContainsKey(key) && LayerEffects.GeneratesContent(layer.Effects, f)) _cels[key] = new PixelImage(area.Width, area.Height);
            }
        layer.Kind = LayerKind.Part;
        layer.PartSize = area.Size;
        layer.PartOffset = area.Location;
        layer.PartPivot = Pivot.Center;
        foreach (var f in _frames) f.PartPivots.Remove(layer);
        layer.PartBorder = default;
        layer.PartSourceRect = null;
    }

    /// <summary>착장의 부위 재정의에서 <paramref name="part"/>를 뺀다(그룹을 떠나는 부위).</summary>
    private void DetachFromOutfits(Layer part)
    {
        foreach (var outfit in _outfits)
            if (outfit.Parts.Any(e => ReferenceEquals(e.Part, part)))
                outfit.Parts = outfit.Parts.Where(e => !ReferenceEquals(e.Part, part)).ToArray();
    }
}
