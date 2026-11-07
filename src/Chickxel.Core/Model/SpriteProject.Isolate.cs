using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 레이어 하나만 보이기·풀어 두기(타임라인의 Alt+눈·Alt+자물쇠).
public sealed partial class SpriteProject
{
    /// <summary>
    /// 레이어 <paramref name="index"/>만 남기고 나머지를 모두 숨긴다(<paramref name="lockToggle"/>면 잠근다). 그 레이어가 든 그룹과, 그룹이면 그 부위들도 함께 남긴다
    /// (부위만 보이려면 그룹도 보여야 하므로). 이미 그 상태(남길 레이어는 모두 보이고·풀려 있고 나머지는 모두 숨김·잠김)면 모든 레이어를 보이게(풀게) 한다.
    /// 실행취소 한 단계, 바뀐 것이 없으면 기록하지 않는다. 나머지를 숨겼으면(잠갔으면) true, 모두 보이게(풀게) 했으면 false.
    /// </summary>
    public bool IsolateLayer(int index, bool lockToggle)
    {
        var layer = _layers[index];
        var keep = new HashSet<Layer>(ReferenceEqualityComparer.Instance) { layer };
        for (var parent = layer.Parent; parent is not null; parent = parent.Parent) keep.Add(parent);
        if (layer.IsGroup) keep.UnionWith(ChildrenOf(layer));
        bool Off(Layer l) => lockToggle ? l.Locked : !l.Visible;
        bool isolated = _layers.All(l => Off(l) != keep.Contains(l));
        var changes = _layers.Select(l => (Layer: l, Off: !isolated && !keep.Contains(l))).Where(c => Off(c.Layer) != c.Off).ToList();
        if (changes.Count > 0)
        {
            string label = lockToggle
                ? isolated ? DomainStrings.Project_UnlockAllLayers : DomainStrings.Project_LockOtherLayers
                : isolated ? DomainStrings.Project_ShowAllLayers : DomainStrings.Project_HideOtherLayers;
            Edit(label, ProjectChange.Layers, () =>
            {
                foreach (var (l, off) in changes)
                {
                    if (lockToggle) l.Locked = off;
                    else l.Visible = !off;
                }
            });
        }
        return !isolated;
    }
}
