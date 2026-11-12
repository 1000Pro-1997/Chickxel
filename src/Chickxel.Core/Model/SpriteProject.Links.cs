using System.Runtime.CompilerServices;
using Chickxel.Core.Edit;
using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

/// <summary>타임라인에 그릴 칸 하나의 링크 표시: <see cref="Group"/> = 행 안의 링크 묶음 번호(첫 프레임 차례, -1 = 링크 아님), <see cref="Color"/> = 묶음 색 번호.</summary>
public readonly record struct CelLinkMark(int Group, int Color)
{
    public static readonly CelLinkMark None = new(-1, -1);
    public bool IsLinked => Group >= 0;
}

/// <summary>셀 링크 풀기 결과(<see cref="SpriteProject.UnlinkCels"/>): <see cref="Unlinked"/> = 링크를 푼 셀 수, <see cref="Baked"/> = 효과 끝 모습을 보이는 그대로 그림에 넣은 셀 수(링크가 남은 칸 포함).</summary>
public readonly record struct UnlinkResult(int Unlinked, int Baked);

/// <summary>
/// 붙여넣기용 셀 사본(<see cref="SpriteProject.CopyCels"/> → <see cref="SpriteProject.PasteCels"/>): 복사할 때 그림의 사본과, 링크된 셀이었으면
/// 그 링크 묶음(같이 쓰던 그림)과 보이던 색. 그림은 고치지 않는다(붙일 때 사본을 넣음).
/// </summary>
public sealed class CelCopy
{
    internal CelCopy(PixelImage image, PixelImage? link, int linkColor)
    {
        Image = image;
        Link = link;
        LinkColor = linkColor;
    }

    /// <summary>복사할 때 그림(사본).</summary>
    public PixelImage Image { get; }

    /// <summary>링크된 셀이었으면 그 묶음 그림(복사한 문서의 셀 그림 그대로), 아니면 null.</summary>
    internal PixelImage? Link { get; }

    /// <summary>링크 묶음 색 번호(<see cref="SpriteProject.LinkColorNames"/>, 링크가 아니면 -1).</summary>
    public int LinkColor { get; }

    public bool IsLinked => Link is not null;
}

// 링크된 셀: 같은 레이어(부위면 같은 스킨)의 여러 프레임 셀이 그림 하나(같은 PixelImage)를 같이 쓴다.
// 스트로크는 그 그림을 그 자리에서 고치므로 링크된 셀이 모두 함께 바뀌고, 셀 내용을 새 그림으로 바꾸는 편집(SetCel·MapCels·옮기기·변형·
// 캔버스·레이어 크기 등)도 링크된 셀 모두에 같은 새 그림을 넣어 링크를 지킨다. 캔버스 밖 그림도 링크된 셀끼리 같다.
// 셀을 갈아 끼우거나(PlaceCel — 붙여넣기 등) 비우면 그 셀만 링크에서 빠진다. 실행취소 사본은 그림 참조를 담으므로 링크도 함께 돌아온다.
// 링크 묶음 색은 묶음 그림에 붙은 표(_linkTags)에 있다: 묶을 때 그 행에서 아직 쓰지 않은 첫 색(묶은 차례대로)을 받고, 그림을 새 그림으로 바꿀 때 따라간다.
// 색을 바꿀 때는 그림 사본에 새 색을 붙여 넣으므로(표는 고치지 않음) 실행취소로 예전 색이 돌아온다.
public sealed partial class SpriteProject
{
    /// <summary>링크 묶음 색 이름(색 번호 차례 — 타임라인이 같은 차례의 색으로 그림). 묶을 때 그 행에서 쓰지 않은 첫 색부터 차례로 받는다.</summary>
    public static readonly IReadOnlyList<string> LinkColorNames = ["blue", "orange", "green", "pink", "yellow", "purple", "teal"];

    /// <summary>링크 묶음 그림의 색(그림은 바뀌지 않는 값으로 — 같은 표가 여러 그림에 붙어도 됨).</summary>
    private sealed class LinkTag(int color)
    {
        public int Color { get; } = color;
    }

    private readonly ConditionalWeakTable<PixelImage, LinkTag> _linkTags = new();

    /// <summary>링크 묶음 그림의 색(색이 붙지 않았으면 -1).</summary>
    private int ColorOf(PixelImage image) => _linkTags.TryGetValue(image, out var tag) ? tag.Color : -1;

    /// <summary><paramref name="image"/>에 색을 붙여 돌려준다. 이미 셀에 들어간 그림에는 쓰지 않는다(실행취소가 예전 색을 되돌리지 못함) — 새 그림·사본에만.</summary>
    private PixelImage Tagged(PixelImage image, int color)
    {
        _linkTags.AddOrUpdate(image, new LinkTag(color));
        return image;
    }

    /// <summary>링크 묶음 그림을 새 그림으로 바꿀 때 색을 따라가게 한다(새 그림에 이미 색이 있으면 그대로).</summary>
    private void InheritLinkTag(PixelImage from, PixelImage to)
    {
        if (ReferenceEquals(from, to) || _linkTags.TryGetValue(to, out _)) return;
        if (_linkTags.TryGetValue(from, out var tag)) _linkTags.AddOrUpdate(to, tag);
    }

    /// <summary><paramref name="key"/>와 같은 그림을 쓰는 셀 키(자신 포함, 프레임 차례). 셀이 없으면 <paramref name="key"/> 하나.</summary>
    private List<CelKey> LinkedKeys(CelKey key)
    {
        if (!_cels.TryGetValue(key, out var image)) return [key];
        var keys = new List<CelKey>();
        foreach (var frame in _frames)
        {
            var k = key with { Frame = frame };
            if (_cels.TryGetValue(k, out var other) && ReferenceEquals(other, image)) keys.Add(k);
        }
        if (!keys.Contains(key)) keys.Add(key);
        return keys;
    }

    /// <summary>셀 내용을 새 그림으로 바꾼다: 링크된 셀 모두에(<see cref="Edit(string, ProjectChange, Action)"/> 안에서). 링크 색도 따라간다.</summary>
    private void SetLinked(CelKey key, PixelImage image)
    {
        if (_cels.TryGetValue(key, out var old)) InheritLinkTag(old, image);
        foreach (var k in LinkedKeys(key)) _cels[k] = image;
    }

    /// <summary><see cref="SetLinked(CelKey, PixelImage)"/>에 캔버스 밖 그림까지.</summary>
    private void SetLinked(CelKey key, PixelImage image, CelOverflow? overflow)
    {
        if (_cels.TryGetValue(key, out var old)) InheritLinkTag(old, image);
        foreach (var k in LinkedKeys(key))
        {
            _cels[k] = image;
            SetOverflow(k, overflow);
        }
    }

    /// <summary>그림마다 한 번만 바꾸는 함수: 같은 그림을 같이 쓰던 셀(링크)은 바꾼 뒤에도 같은 새 그림을 같이 쓰고, 링크 색도 따라간다.</summary>
    private Func<PixelImage, PixelImage> OncePerImage(Func<PixelImage, PixelImage> map)
    {
        var done = new Dictionary<PixelImage, PixelImage>(ReferenceEqualityComparer.Instance);
        return image =>
        {
            if (!done.TryGetValue(image, out var result))
            {
                done[image] = result = map(image);
                InheritLinkTag(image, result);
            }
            return result;
        };
    }

    /// <summary>
    /// 셀마다 따로 계산한 새 그림을 넣는다(<see cref="Edit(string, ProjectChange, Action)"/> 안에서, 캔버스 밖 그림은 먼저 넣어 둔다). 바꾸기 전 같은 그림을 쓰던 셀(링크)의
    /// 새 그림·캔버스 밖 그림이 모두 같으면 하나를 같이 쓰게 해 링크(와 색)를 지키고, 다르면(프레임마다 달라지는 효과를 구웠을 때 등) 셀마다 따로 둔다.
    /// </summary>
    private void PutKeepingLinks(IReadOnlyList<(CelKey Key, PixelImage? Before, PixelImage After)> changes)
    {
        var shared = new Dictionary<PixelImage, (CelKey Key, PixelImage After)?>(ReferenceEqualityComparer.Instance);
        foreach (var (key, before, after) in changes)
        {
            if (before is null) continue;
            if (!shared.TryGetValue(before, out var first)) shared[before] = (key, after);
            else if (first is { } f && !(f.After.ContentEquals(after) && SameOverflow(f.Key, key))) shared[before] = null;
        }
        foreach (var (before, same) in shared)
            if (same is { } s) InheritLinkTag(before, s.After);
        foreach (var (key, before, after) in changes)
            _cels[key] = before is not null && shared[before] is { } same ? same.After : after;
    }

    private bool SameOverflow(CelKey a, CelKey b)
    {
        bool hasA = _overflow.TryGetValue(a, out var oa), hasB = _overflow.TryGetValue(b, out var ob);
        return hasA == hasB && (!hasA || oa.Origin == ob.Origin && (ReferenceEquals(oa.Image, ob.Image) || oa.Image.ContentEquals(ob.Image)));
    }

    /// <summary>행(레이어·스킨)의 링크 묶음 그림(두 칸 이상이 같이 쓰는 그림, 묶음의 첫 프레임 차례)과 그 묶음의 프레임 번호들.</summary>
    private List<(PixelImage Image, List<int> Frames)> LinkGroupsOf(Layer layer, Skin? skin)
    {
        var groups = new List<(PixelImage Image, List<int> Frames)>();
        var index = new Dictionary<PixelImage, int>(ReferenceEqualityComparer.Instance);
        for (int f = 0; f < _frames.Count; f++)
        {
            if (!_cels.TryGetValue(new(layer, _frames[f], skin), out var image)) continue;
            if (!index.TryGetValue(image, out var i))
            {
                index[image] = i = groups.Count;
                groups.Add((image, []));
            }
            groups[i].Frames.Add(f);
        }
        groups.RemoveAll(g => g.Frames.Count < 2);
        return groups;
    }

    /// <summary>
    /// 새 링크 묶음의 색: 그 행의 다른 링크 묶음(과 <paramref name="alsoUsed"/>)이 쓰지 않는 첫 색 — 묶은 차례대로 파랑·주황·초록… —, 모두 쓰면 묶음 수 차례로 돈다.
    /// </summary>
    private int NextLinkColor(Layer layer, Skin? skin, IEnumerable<int>? alsoUsed = null)
    {
        var groups = LinkGroupsOf(layer, skin);
        var used = new HashSet<int>(alsoUsed ?? []);
        foreach (var (image, _) in groups) used.Add(ColorOf(image));
        for (int c = 0; c < LinkColorNames.Count; c++)
            if (!used.Contains(c)) return c;
        return groups.Count % LinkColorNames.Count;
    }

    /// <summary>
    /// 지금 스킨 자신의 셀이 다른 프레임 셀과 링크되어(같은 그림을 같이 써) 있는지. 셀이 없으면 false.
    /// 그룹이면 그룹 행 링크 표시(<see cref="LinkMarks"/> — 부위 모두가 같은 프레임들과 링크)로 본다.
    /// </summary>
    public bool IsCelLinked(Layer layer, Frame frame)
    {
        if (layer.IsGroup)
        {
            int f = _frames.IndexOf(frame);
            return f >= 0 && LinkMarks(_layers.IndexOf(layer))[f].IsLinked;
        }
        var key = new CelKey(layer, frame, ActiveKey(layer));
        return _cels.ContainsKey(key) && LinkedKeys(key).Count > 1;
    }

    /// <inheritdoc cref="IsCelLinked(Layer, Frame)"/>
    public bool IsCelLinked(int layerIndex, int frameIndex) => IsCelLinked(_layers[layerIndex], _frames[frameIndex]);

    /// <summary>두 프레임의 지금 스킨 자신의 셀이 서로 링크되어 있는지(같은 프레임이거나 셀이 없으면 false).</summary>
    public bool AreCelsLinked(int layerIndex, int frameA, int frameB)
    {
        if (frameA == frameB) return false;
        var layer = _layers[layerIndex];
        var skin = ActiveKey(layer);
        return _cels.TryGetValue(new(layer, _frames[frameA], skin), out var a)
               && _cels.TryGetValue(new(layer, _frames[frameB], skin), out var b) && ReferenceEquals(a, b);
    }

    /// <summary>지금 스킨 자신의 셀과 링크된 프레임 번호(자신 포함, 오름차순). 셀이 없으면 빈 목록.</summary>
    public IReadOnlyList<int> LinkedFrames(int layerIndex, int frameIndex)
    {
        var layer = _layers[layerIndex];
        var key = new CelKey(layer, _frames[frameIndex], ActiveKey(layer));
        return _cels.ContainsKey(key) ? LinkedKeys(key).Select(k => _frames.IndexOf(k.Frame)).ToList() : [];
    }

    /// <summary>두 프레임이 같은 그림을 쓰는지: 같은 프레임이거나 지금 스킨 자신의 셀이 서로 링크되어 있으면 true(효과 창 미리보기의 끝 모습 이어 가기).</summary>
    public bool CelsShareDrawing(int layerIndex, int frameA, int frameB)
        => frameA == frameB || ShareDrawing(_layers[layerIndex], ActiveKey(_layers[layerIndex]), frameA, frameB);

    private bool ShareDrawing(Layer layer, Skin? skin, int frameA, int frameB)
        => (uint)frameA < (uint)_frames.Count && (uint)frameB < (uint)_frames.Count
           && _cels.TryGetValue(new(layer, _frames[frameA], skin), out var a) && _cels.TryGetValue(new(layer, _frames[frameB], skin), out var b) && ReferenceEquals(a, b);

    /// <summary>
    /// 그릴 효과 목록에 범위 뒤 칸의 효과 끝 모습을 넣는다(<see cref="LayerEffects.HeldEnds"/>): 끝 모습 이어 가기를 켠 움직이는 효과의 범위가 이 프레임 앞에서 끝났으면
    /// 그 효과를 범위 끝 프레임의 조건 그대로(진행·피벗·다른 프레임·다른 레이어 <paramref name="cels"/>·태그) 맞춰 고정한 사본으로 바꾼다 — 이 프레임 셀(셀 키 스킨
    /// <paramref name="skin"/>)이 범위 끝 프레임 셀과 링크되어 있으면 범위 끝 프레임의 결과를 그대로 다시 그리고, 아니면(옮긴 거리를 알리는 효과만) 범위 끝에서 옮긴 자리로
    /// 이 셀을 옮긴다. 그룹은 셀이 없어 그대로.
    /// </summary>
    private IReadOnlyList<LayerEffect> WithHeldEnds(Layer layer, IReadOnlyList<LayerEffect> effects, int frameIndex, Skin? skin,
        Func<Layer, int, PixelImage?> cels, bool nested)
    {
        if (layer.IsGroup || effects.Count == 0) return effects;
        if (LayerEffects.HeldEnds(effects, frameIndex, (a, b) => ShareDrawing(layer, skin, a, b)) is not { } held) return effects;
        var list = effects.ToArray();
        foreach (var h in held)
        {
            LayerEffect[] one = [effects[h.Index]];
            Func<int, PixelImage?>? source = LayerEffects.UsesOtherFrames(one) ? EffectFrameSource(layer) : null;
            var resolved = LayerEffects.WithFrameTag(LayerEffects.WithScriptLayers(
                LayerEffects.At(one, h.End, _frames.Count, EffectPivot(layer, h.End), source, source is null ? null : f => EffectPivot(layer, f)),
                ScriptLayersOf(layer, h.End, one, cels, nested)), FrameTagFor(one, h.End));
            if (LayerEffects.Pin(resolved[0], h, h.Linked ? null : cels(layer, h.End)) is { } pinned) list[h.Index] = pinned;
        }
        return list;
    }

    /// <summary>
    /// 레이어의 프레임마다 링크 표시(지금 스킨 자신의 셀 기준): 묶음 번호(묶음의 첫 프레임 차례로 0부터)와 묶음 색. 링크되지 않은 칸은 <see cref="CelLinkMark.None"/>.
    /// 색이 붙지 않은 묶음(실행취소 기록에서 되살린 예전 그림 등)은 그 행에서 쓰지 않은 첫 색으로 보인다. 그룹 행은 <see cref="GroupLinkMarks"/>.
    /// </summary>
    public CelLinkMark[] LinkMarks(int layerIndex)
    {
        var marks = new CelLinkMark[_frames.Count];
        Array.Fill(marks, CelLinkMark.None);
        var layer = _layers[layerIndex];
        if (layer.IsGroup) return GroupLinkMarks(layer, marks);
        var groups = LinkGroupsOf(layer, ActiveKey(layer));
        var used = groups.Select(g => ColorOf(g.Image)).Where(c => c >= 0).ToHashSet();
        for (int i = 0; i < groups.Count; i++)
        {
            int color = ColorOf(groups[i].Image);
            if (color < 0)
            {
                color = Enumerable.Range(0, LinkColorNames.Count).FirstOrDefault(c => !used.Contains(c), i % LinkColorNames.Count);
                used.Add(color);
            }
            foreach (var f in groups[i].Frames) marks[f] = new CelLinkMark(i, color);
        }
        return marks;
    }

    /// <summary>
    /// 그룹 행의 링크 표시: 그룹의 부위 모두가(지금 스킨 자신의 셀 기준) 같은 프레임들끼리 같은 그림을 쓰면 그 프레임들이 한 묶음이다 —
    /// 그룹 행에서 링크한 칸(<see cref="LinkCels"/>), 또는 부위마다 같은 프레임들을 따로 링크한 칸. 자기 그림이 있는 부위가 하나도 없는 프레임은 빼고,
    /// 색은 그 묶음 첫 부위 그림의 색(없으면 그 행에서 쓰지 않은 첫 색).
    /// </summary>
    private CelLinkMark[] GroupLinkMarks(Layer group, CelLinkMark[] marks)
    {
        var rows = ChildrenOf(group).Select(part => (Part: part, Skin: ActiveKey(part))).ToList();
        if (rows.Count == 0) return marks;
        var groups = new List<(PixelImage?[] Cels, List<int> Frames)>();
        for (int f = 0; f < _frames.Count; f++)
        {
            var cels = rows.Select(r => _cels.GetValueOrDefault(new CelKey(r.Part, _frames[f], r.Skin))).ToArray();
            if (cels.All(c => c is null)) continue;
            int i = groups.FindIndex(g => g.Cels.Zip(cels).All(p => ReferenceEquals(p.First, p.Second)));
            if (i < 0) groups.Add((cels, [f]));
            else groups[i].Frames.Add(f);
        }
        groups.RemoveAll(g => g.Frames.Count < 2);
        int ColorOfGroup(int i) => groups[i].Cels.FirstOrDefault(c => c is not null) is { } image ? ColorOf(image) : -1;
        var used = Enumerable.Range(0, groups.Count).Select(ColorOfGroup).Where(c => c >= 0).ToHashSet();
        for (int i = 0; i < groups.Count; i++)
        {
            int color = ColorOfGroup(i);
            if (color < 0)
            {
                color = Enumerable.Range(0, LinkColorNames.Count).FirstOrDefault(c => !used.Contains(c), i % LinkColorNames.Count);
                used.Add(color);
            }
            foreach (var f in groups[i].Frames) marks[f] = new CelLinkMark(i, color);
        }
        return marks;
    }

    /// <summary>레이어의 링크 묶음(프레임 번호들, 색 번호 — 첫 프레임 차례). AI 문서 설명에 쓴다.</summary>
    public IReadOnlyList<(IReadOnlyList<int> Frames, int Color)> LinkGroups(int layerIndex)
    {
        var marks = LinkMarks(layerIndex);
        return [.. Enumerable.Range(0, marks.Length).Where(f => marks[f].IsLinked).GroupBy(f => marks[f].Group)
            .Select(g => ((IReadOnlyList<int>)g.ToList(), marks[g.First()].Color))];
    }

    /// <summary>
    /// 셀 링크(실행취소 한 단계): 고른 셀(레이어 번호, 프레임 번호 — 떨어진 칸이어도 됨)을 레이어마다 묶어 그림 하나를 같이 쓰게 한다.
    /// 같이 쓸 그림은 기준 프레임 <paramref name="mainFrame"/>(타임라인에서 지금 누른 칸)의 셀이 고른 칸이고 그림이 있으면 그 셀의 것 — 그 셀이 이미 링크돼 있으면
    /// 고른 칸이 그 링크 묶음(과 색)에 들어간다 —. 아니면 고른 셀 중 이미 링크된(다른 칸과 그림을 같이 쓰는) 그림 있는 첫 프레임 셀의 것(그 묶음에 들어감),
    /// 없으면 그림이 있는 첫 프레임 셀(지금 스킨 자신의 셀)의 것으로 새 묶음을 만든다(색 = 그 행에서 아직 쓰지 않은 첫 색).
    /// 나머지 셀의 그림(빈 셀 포함)은 그 그림으로 바뀌고, 일반 레이어면 캔버스 밖 그림도 같아진다.
    /// <para>
    /// 그룹 행을 고르면 그 그룹의 부위마다(잠긴 부위 빼고) 고른 프레임들을 새 묶음 하나로 묶는다(부위들이 같은 색): 부위마다 누른 칸(없으면 그림이 보이는 첫 칸)의
    /// 보이는 그림 사본을 같이 쓰고, 그 칸들이 들어 있던 다른 묶음에서는 빠진다 — 그룹 링크가 부위 링크보다 먼저. 같은 그룹의 부위 행을 함께 골랐으면 그 부위 행은
    /// 그룹 링크를 따른다. 이미 고른 프레임들만으로 묶여 있는 부위는 그대로.
    /// </para>
    /// 잠긴 레이어·그림이 보이는 셀이 없는 레이어는 건너뛴다. 새로 링크한 셀 수를 돌려준다(0이면 기록 없음).
    /// </summary>
    public int LinkCels(IEnumerable<(int Layer, int Frame)> cels, int mainFrame = -1)
    {
        var plan = new List<(CelKey Source, bool Join, List<CelKey> Keys, int Color)>();
        int count = 0;
        var rows = cels.Where(c => c.Frame >= 0 && c.Frame < _frames.Count && c.Layer >= 0 && c.Layer < _layers.Count).Distinct().GroupBy(c => c.Layer).ToList();
        var groupRows = rows.Where(r => _layers[r.Key].IsGroup && !IsLayerLocked(r.Key)).Select(r => _layers[r.Key]).ToHashSet();
        int MainIndex(List<CelKey> keys) => mainFrame >= 0 && mainFrame < _frames.Count ? keys.FindIndex(k => ReferenceEquals(k.Frame, _frames[mainFrame])) : -1;
        foreach (var row in rows)
        {
            int li = row.Key;
            var frames = row.Select(c => c.Frame).Order().ToList();
            if (IsLayerLocked(li)) continue;
            if (_layers[li].IsGroup)
            {
                // 그룹 링크: 부위마다 고른 프레임들이 새 묶음 하나(부위들이 같은 색)
                int color = -1;
                foreach (var part in ChildrenOf(_layers[li]))
                {
                    if (IsLayerLocked(IndexOf(part))) continue;
                    var partSkin = ActiveKey(part);
                    var partKeys = frames.Select(f => new CelKey(part, _frames[f], partSkin)).ToList();
                    bool Shown(CelKey k) => HasContent(_cels.GetValueOrDefault(k));
                    int pm = MainIndex(partKeys);
                    int from = pm >= 0 && Shown(partKeys[pm]) ? pm : partKeys.FindIndex(Shown);
                    if (from < 0) continue;
                    var own = _cels.GetValueOrDefault(partKeys[0]);
                    if (own is not null && partKeys.All(k => ReferenceEquals(_cels.GetValueOrDefault(k), own)) && LinkedKeys(partKeys[0]).Count == partKeys.Count) continue;
                    if (color < 0) color = NextLinkColor(part, partSkin);
                    plan.Add((partKeys[from], false, partKeys, color));
                    count += partKeys.Count;
                }
                continue;
            }
            var layer = _layers[li];
            if (layer.Parent is { } parent && groupRows.Contains(parent)) continue; // 그룹 링크가 먼저
            var skin = ActiveKey(layer);
            var keys = frames.Select(f => new CelKey(layer, _frames[f], skin)).ToList();
            int main = MainIndex(keys);
            int source = main >= 0 && HasContent(_cels.GetValueOrDefault(keys[main])) ? main : -1;
            if (source < 0) source = keys.FindIndex(k => HasContent(_cels.GetValueOrDefault(k)) && LinkedKeys(k).Count > 1);
            bool join = source >= 0 && LinkedKeys(keys[source]).Count > 1;
            if (source < 0) source = keys.FindIndex(k => HasContent(_cels.GetValueOrDefault(k)));
            if (source < 0) continue;
            int added = keys.Count(k => !ReferenceEquals(_cels.GetValueOrDefault(k), _cels[keys[source]]));
            if (added == 0) continue;
            plan.Add((keys[source], join, keys, -1));
            count += added;
        }
        if (count == 0) return 0;
        Edit(DomainStrings.Project_LinkCels, ProjectChange.Pixels, () =>
        {
            foreach (var (source, join, keys, color) in plan)
            {
                // 새 묶음은 그림 사본에 새 색을 붙여 같이 쓴다(이미 셀에 있던 그림의 색 표를 고치지 않게 — 실행취소가 예전 색을 되돌림)
                var image = join ? _cels[source] : Tagged(_cels[source].Clone(), color >= 0 ? color : NextLinkColor(source.Layer, source.Skin));
                CelOverflow? overflow = _overflow.TryGetValue(source, out var o) ? o : null;
                foreach (var key in keys)
                {
                    if (ReferenceEquals(_cels.GetValueOrDefault(key), image)) continue;
                    _cels[key] = image;
                    if (key.Layer.Kind == LayerKind.Normal) SetOverflow(key, overflow);
                }
            }
        });
        return count;
    }

    /// <summary>
    /// 링크 묶음 색 바꾸기(실행취소 한 단계): 프레임 <paramref name="frameIndex"/>의 셀(지금 스킨 자신의 셀)이 든 링크 묶음의 색을 <paramref name="color"/>
    /// (<see cref="LinkColorNames"/> 번호)로. 그룹이면 그 프레임 셀이 링크된 부위마다. 링크된 셀이 없으면 false(기록 없음).
    /// </summary>
    public bool SetLinkColor(int layerIndex, int frameIndex, int color)
    {
        if ((uint)color >= (uint)LinkColorNames.Count) throw new ArgumentOutOfRangeException(nameof(color));
        if (_layers[layerIndex].IsGroup)
        {
            var parts = ChildrenOf(_layers[layerIndex]).Select(IndexOf).Where(pi => IsCelLinked(pi, frameIndex)).ToList();
            if (parts.Count == 0) return false;
            Edit(DomainStrings.Project_LinkColor, ProjectChange.Pixels, () =>
            {
                foreach (int pi in parts) SetLinkColor(pi, frameIndex, color);
            });
            return true;
        }
        var key = CelKeyFor(layerIndex, frameIndex, null);
        if (!_cels.TryGetValue(key, out var image) || LinkedKeys(key).Count < 2) return false;
        if (ColorOf(image) == color) return true;
        Edit(DomainStrings.Project_LinkColor, ProjectChange.Pixels, () => SetLinked(key, Tagged(image.Clone(), color)));
        return true;
    }

    /// <summary>
    /// 셀 링크 풀기(실행취소 한 단계): 고른 셀(지금 스킨 자신의 셀) 중 다른 셀과 링크된 것이 저마다 그림 사본을 갖게 한다. 그룹 행의 칸이면 그 그룹의 부위마다
    /// 그 프레임 셀을 푼다. 잠긴 레이어·빈 칸은 건너뛰고, 묶음의 마지막 셀은 혼자 남으므로 사본이 필요 없다.
    /// <para>
    /// 링크된 칸의 효과 끝 모습(<see cref="AnimatedEffect.HoldEnd"/>)은 링크로 보이던 것이라 풀면 사라진다(옮긴 거리를 알리는 효과면 옮긴 자리만 남는다). 그래서 풀기 전에
    /// 그 모습을 잃고 이어 가는 끝 모습이 하나도 남지 않는 칸 — 떼어 낸 칸, 그리고 범위 끝 프레임 칸을 떼어 내 끝 모습을 잃는 남은 칸 — 은 지금 보이는 그대로
    /// (그 레이어의 켜진 효과와 끝 모습, 셀 크기로 잘림, 레이어 보이기·불투명도 없이) 그림에 넣는다. 그 프레임 범위 안인 켜진 효과가 있거나 풀고 나서도 이어 가는 끝 모습
    /// (남은 링크로 끝 모습 그대로, 또는 링크 없이 옮긴 자리만)이 있으면(효과가 그 위에 계속 입혀지므로) 그림 그대로 사본을 둔다.
    /// 남은 칸들이 같은 모습을 잃으면 묶음 그림을 그 모습으로 바꿔 링크를 지키고, 모습이 칸마다 다르면 그 칸들도 떼어 낸다.
    /// </para>
    /// 링크를 푼 셀 수와 그중 보이는 그대로 넣은 셀 수를 돌려준다(푼 셀이 0이면 기록 없음).
    /// </summary>
    public UnlinkResult UnlinkCels(IEnumerable<(int Layer, int Frame)> cels)
    {
        var keys = new List<CelKey>();
        foreach (var (li, fi) in cels.Distinct())
        {
            if (li < 0 || li >= _layers.Count || fi < 0 || fi >= _frames.Count || IsLayerLocked(li)) continue;
            // 그룹 행이면 그 그룹의 부위마다(잠긴 부위 빼고)
            var rowLayers = _layers[li].IsGroup ? ChildrenOf(_layers[li]).Where(part => !IsLayerLocked(IndexOf(part))) : [_layers[li]];
            foreach (var layer in rowLayers)
            {
                var key = new CelKey(layer, _frames[fi], ActiveKey(layer));
                if (_cels.ContainsKey(key) && LinkedKeys(key).Count > 1 && !keys.Contains(key)) keys.Add(key);
            }
        }
        if (keys.Count == 0) return default;

        // 풀고 난 뒤 같은 그림을 쓸 칸 표(셀 키 → 묶음 표): 떼어 낼 칸은 저마다 새 표, 남는 칸은 원래 그림. 손대는 묶음의 칸은 모두 넣는다.
        var after = new Dictionary<CelKey, object>();
        var members = new List<CelKey>();
        var detached = new List<CelKey>();
        foreach (var key in keys)
        {
            var group = LinkedKeys(key);
            foreach (var k in group)
                if (after.TryAdd(k, _cels[k])) members.Add(k);
            // 하나씩 떼어 내면 묶음의 마지막 셀은 혼자 남으므로 사본이 필요 없다
            if (group.Count(k => ReferenceEquals(after[k], _cels[k])) < 2) continue;
            after[key] = new object();
            detached.Add(key);
        }
        if (detached.Count == 0) return default;

        // 끝 모습은 지금 링크에 달려 있으므로 바꾸기 전에 보이는 그대로 그려 둔다
        var shown = new Dictionary<CelKey, PixelImage>();
        foreach (var k in members)
        {
            int f = _frames.IndexOf(k.Frame);
            var effects = k.Layer.Effects;
            if (effects.Any(e => e.Enabled && e.AppliesTo(f))) continue; // 범위 안 효과는 풀어도 계속 입혀진다
            if (LayerEffects.HeldEnds(effects, f, (a, b) => ShareDrawing(k.Layer, k.Skin, a, b))?.Exists(h => h.Linked) != true) continue;
            // 풀고 난 뒤에도 이어 가는 끝 모습이 있으면(남은 칸이 범위 끝 프레임과 계속 링크, 또는 링크 없이 옮긴 자리만) 그 위에 계속 입혀지므로 그림 그대로
            if (LayerEffects.HeldEnds(effects, f, (a, b) => SharesAfter(k, a, b)) is not null) continue;
            shown[k] = ClippedCelPlan(k.Layer, f, RenderCel(k.Layer, f) ?? _cels[k], withEffects: true).Render();
        }

        bool SharesAfter(CelKey k, int a, int b)
        {
            if ((uint)a >= (uint)_frames.Count || (uint)b >= (uint)_frames.Count) return false;
            object? Token(int f) => after.TryGetValue(k with { Frame = _frames[f] }, out var t) ? t : _cels.GetValueOrDefault(k with { Frame = _frames[f] });
            return Token(a) is { } ta && ReferenceEquals(ta, Token(b));
        }

        int unlinked = detached.Count, baked = 0;
        Edit(DomainStrings.Project_UnlinkCels, ProjectChange.Pixels, () =>
        {
            foreach (var key in detached)
            {
                if (shown.TryGetValue(key, out var image)) baked++;
                _cels[key] = image ?? _cels[key].Clone();
            }
            // 남은 칸(원래 그림을 계속 쓰는 칸)이 끝 모습을 잃었으면: 모두 같은 모습이면 묶음 그림을 그 모습으로, 아니면 그 칸들도 떼어 낸다
            foreach (var rest in members.Where(k => !detached.Contains(k)).GroupBy(k => _cels[k], ReferenceEqualityComparer.Instance))
            {
                var keep = rest.ToList();
                var lost = keep.Where(shown.ContainsKey).ToList();
                if (lost.Count == 0) continue;
                var first = shown[lost[0]];
                if (lost.Count == keep.Count && lost.All(k => shown[k].ContentEquals(first)))
                {
                    InheritLinkTag(_cels[lost[0]], first);
                    foreach (var k in keep) _cels[k] = first;
                }
                else
                {
                    foreach (var k in lost) _cels[k] = shown[k];
                    unlinked += lost.Count == keep.Count ? lost.Count - 1 : lost.Count;
                }
                baked += lost.Count;
            }
        });
        return new UnlinkResult(unlinked, baked);
    }

    /// <summary>
    /// 고른 셀(지금 스킨 자신의 셀)의 그림을 지운다(실행취소 한 단계). 링크된 셀은 링크를 남긴 채 묶음의 그림을 빈 그림으로 바꾸고(링크된 칸이 모두 비고 색도 그대로),
    /// 링크되지 않은 셀은 비운다(<see cref="SetCel"/>에 null). 그룹·잠긴 레이어·빈 칸은 건너뛴다. 지운 셀 수를 돌려준다(0이면 기록 없음).
    /// </summary>
    public int EraseCels(IEnumerable<(int Layer, int Frame)> cels, string? label = null)
    {
        var keys = new List<(CelKey Key, bool Linked)>();
        foreach (var (li, fi) in cels.Distinct())
        {
            if (li < 0 || li >= _layers.Count || fi < 0 || fi >= _frames.Count || _layers[li].IsGroup || IsLayerLocked(li)) continue;
            var layer = _layers[li];
            var key = new CelKey(layer, _frames[fi], ActiveKey(layer));
            if (!HasContent(_cels.GetValueOrDefault(key))) continue;
            keys.Add((key, LinkedKeys(key).Count > 1));
        }
        if (keys.Count == 0) return 0;
        Edit(label ?? DomainStrings.Project_ChangeCel, ProjectChange.Pixels, () =>
        {
            foreach (var (key, linked) in keys)
            {
                if (!HasContent(_cels.GetValueOrDefault(key))) continue; // 같은 묶음을 이미 지움
                if (linked)
                {
                    var size = CelSize(key.Layer);
                    SetLinked(key, new PixelImage(size.Width, size.Height));
                }
                else _cels.Remove(key);
            }
        });
        return keys.Count;
    }

    /// <summary>
    /// 지금 스킨의 셀 하나만 새 그림으로 갈아 끼운다(null = 비우기, 실행취소됨): 링크된 셀이면 이 셀만 링크에서 빠지고 다른 셀은 그대로다
    /// (앞 프레임 그림 복사처럼 셀 자체를 바꿀 때 — 셀 사본을 링크째 붙이려면 <see cref="PasteCels"/>). 셀 내용을 고쳐 링크된 셀도 함께 바꾸려면 <see cref="SetCel"/>.
    /// </summary>
    public void PlaceCel(int layerIndex, int frameIndex, PixelImage? image, string? label = null)
    {
        var key = CelKeyFor(layerIndex, frameIndex, image);
        Edit(label ?? DomainStrings.Project_ChangeCel, ProjectChange.Pixels, () =>
        {
            if (image is null) _cels.Remove(key);
            else _cels[key] = image;
        });
    }

    /// <summary>
    /// 레이어 <paramref name="layer"/>의 프레임 <paramref name="frames"/> 셀(지금 스킨 자신의 셀, 빈 셀 = null)을 붙여넣기용으로 복사한다(<see cref="PasteCels"/>).
    /// <paramref name="links"/>면 링크된 셀은 그 묶음·색도 담고, 아니면 그림만. 같은 그림을 쓰던 셀은 사본 하나를 같이 쓴다. 그룹이면 모두 null.
    /// </summary>
    public CelCopy?[] CopyCels(Layer layer, IReadOnlyList<int> frames, bool links = true)
    {
        var result = new CelCopy?[frames.Count];
        if (layer.IsGroup) return result;
        var skin = ActiveKey(layer);
        var marks = links ? LinkMarks(_layers.IndexOf(layer)) : null;
        var clones = new Dictionary<PixelImage, PixelImage>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < frames.Count; i++)
        {
            int f = frames[i];
            if (!_cels.TryGetValue(new(layer, _frames[f], skin), out var image)) continue;
            if (!clones.TryGetValue(image, out var clone)) clones[image] = clone = image.Clone();
            var mark = marks?[f] ?? CelLinkMark.None;
            result[i] = new CelCopy(clone, mark.IsLinked ? image : null, mark.Color);
        }
        return result;
    }

    /// <summary>
    /// 셀 사본(<see cref="CopyCels"/>)을 칸(레이어 번호, 프레임 번호 — 지금 스킨의 셀)에 놓는다(실행취소 한 단계, <see cref="Edit(string, ProjectChange, Action)"/> 안이면 그 단계에 든다).
    /// <see cref="PlaceCel"/>처럼 그 셀만 갈아 끼우므로 링크된 칸이면 그 칸만 원래 묶음에서 빠진다. null = 비움.
    /// <para>
    /// 링크된 셀이었던 사본은 링크를 지킨다: 놓을 행(레이어·지금 스킨)에 그 묶음 그림이 (붙이기 전에) 있으면 그 묶음에 들어가고(같은 그림·색, 일반 레이어면
    /// 캔버스 밖 그림도 묶음과 같게), 없으면(다른 레이어·다른 문서, 묶음이 사라짐) 같은 묶음에서 온 사본이 한 행에 둘 이상일 때 그 칸들이 새 묶음 하나를
    /// 같이 쓴다(복사할 때 그림, 색 = 원래 색 — 그 행의 다른 묶음이 쓰는 색이면 쓰지 않은 첫 색, 캔버스 밖 그림 없음). 하나뿐이면 따로 둔다.
    /// </para>
    /// 그룹이면 <see cref="InvalidOperationException"/>, 그림 크기가 셀 크기와 다르면 <see cref="ArgumentException"/>.
    /// </summary>
    public void PasteCels(IReadOnlyList<(int Layer, int Frame, CelCopy? Cel)> cels, string? label = null)
    {
        // 놓을 행에 남아 있는 묶음 칸(붙이기 전 모습으로 본다 — 없으면 null)
        var members = new Dictionary<(Layer, Skin?, PixelImage), CelKey?>();
        CelKey? MemberOf(CelKey key, PixelImage link)
        {
            var row = (key.Layer, key.Skin, link);
            if (members.TryGetValue(row, out var member)) return member;
            member = null;
            foreach (var frame in _frames)
            {
                var k = key with { Frame = frame };
                if (_cels.TryGetValue(k, out var image) && ReferenceEquals(image, link)) { member = k; break; }
            }
            return members[row] = member;
        }
        var plan = new List<(CelKey Key, CelCopy? Cel, bool Join, CelOverflow? Overflow)>();
        var regroup = new Dictionary<(Layer, Skin?, PixelImage), List<CelKey>>();
        foreach (var (li, fi, cel) in cels)
        {
            // 타일맵 레이어에는 셀이 없다(붙이지 않음)
            if (_layers[li].IsTilemap) continue;
            var key = CelKeyFor(li, fi, cel?.Image);
            if (cel?.Link is { } link)
            {
                if (MemberOf(key, link) is { } member)
                {
                    plan.Add((key, cel, true, _overflow.TryGetValue(member, out var o) ? o : null));
                    continue;
                }
                var row = (key.Layer, key.Skin, link);
                if (!regroup.TryGetValue(row, out var keys)) regroup[row] = keys = [];
                keys.Add(key);
            }
            plan.Add((key, cel, false, null));
        }
        foreach (var (row, keys) in regroup.Where(r => r.Value.Count < 2).ToList()) regroup.Remove(row);
        var grouped = regroup.Values.SelectMany(k => k).ToHashSet();
        Edit(label ?? DomainStrings.Project_ChangeCel, ProjectChange.Pixels, () =>
        {
            foreach (var (key, cel, join, overflow) in plan)
            {
                // 새 묶음 칸은 아래에서 넣는다(색을 고를 때 그 칸의 예전 그림을 세지 않게 먼저 비움)
                if (cel is null || grouped.Contains(key)) _cels.Remove(key);
                else if (!join) _cels[key] = cel.Image.Clone();
                else
                {
                    _cels[key] = cel.Link!;
                    if (key.Layer.Kind == LayerKind.Normal) SetOverflow(key, overflow);
                }
            }
            foreach (var (row, keys) in regroup)
            {
                var (layer, skin, _) = row;
                var copy = plan.First(s => s.Key == keys[0]).Cel!;
                bool taken = LinkGroupsOf(layer, skin).Any(g => ColorOf(g.Image) == copy.LinkColor);
                var image = Tagged(copy.Image.Clone(), copy.LinkColor >= 0 && !taken ? copy.LinkColor : NextLinkColor(layer, skin));
                foreach (var key in keys)
                {
                    _cels[key] = image;
                    if (layer.Kind == LayerKind.Normal) SetOverflow(key, null);
                }
            }
        });
    }

    // ───────────── 파일 읽기·쓰기 ─────────────

    /// <summary>파일 쓰기용: 링크 묶음 그림의 색(없으면 -1).</summary>
    internal int LinkColorRaw(PixelImage image) => ColorOf(image);

    /// <summary>파일 읽기·저장 사본용: 링크 묶음 그림에 색을 붙인다(셀에 넣기 전, 실행취소 기록 밖).</summary>
    internal void SetLinkColorRaw(PixelImage image, int color)
    {
        if ((uint)color < (uint)LinkColorNames.Count) Tagged(image, color);
    }
}
