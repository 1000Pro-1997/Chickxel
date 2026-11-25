using Chickxel.Core.Localization;

namespace Chickxel.Core.Model;

// 스킨(레이어마다)과 스킨 프리셋(여러 레이어의 스킨 묶음).
// 레이어마다 스킨 목록(첫 스킨 = 기본)과 지금 스킨(보기 상태 — 실행취소 없음, 파일에 저장, 수정됨 표시)을 갖는다. 셀 키의 스킨은 첫 스킨이면 null이다.
public sealed partial class SpriteProject
{
    // ───────────── 레이어 스킨 ─────────────

    private void RequireSkinLayer(Layer layer)
    {
        if (IndexOfReference(_layers, layer) < 0) throw new ArgumentException(DomainStrings.Project_LayerNotInProject, nameof(layer));
        if (layer.IsTilemap) throw new ArgumentException(DomainStrings.Project_TilemapHasNoSkins, nameof(layer));
    }

    private int RequireSkin(Layer layer, Skin skin)
    {
        RequireSkinLayer(layer);
        int index = layer.SkinList.IndexOf(skin);
        return index >= 0 ? index : throw new ArgumentException(DomainStrings.Project_SkinNotInLayer, nameof(skin));
    }

    /// <summary>레이어 안에서 겹치지 않는 스킨 이름(겹치면 " 2", " 3" …). 비면 "스킨".</summary>
    public string UniqueSkinName(Layer layer, string baseName, Skin? self = null)
    {
        baseName = baseName.Trim();
        if (baseName.Length == 0) baseName = DomainStrings.Project_DefaultSkinName;
        bool Taken(string n) => layer.SkinList.Any(s => !ReferenceEquals(s, self) && s.Name == n);
        if (!Taken(baseName)) return baseName;
        for (int i = 2; ; i++)
            if (!Taken($"{baseName} {i}")) return $"{baseName} {i}";
    }

    /// <summary>
    /// 레이어에 스킨을 하나 더 넣고(목록 끝) 지금 스킨으로 고른다(실행취소됨). <paramref name="copyFrom"/>(그 레이어의 스킨)이 있으면 그 스킨 자신의 셀(일반 레이어면 캔버스 밖 그림도)을
    /// 복사하고, 없으면 빈 스킨이다. 이름은 레이어 안에서 겹치지 않게 바뀐다(비면 "스킨", 복사면 "… 복사").
    /// 새 스킨은 처음에 링크도 원본(<paramref name="copyFrom"/>, 없으면 첫 스킨)과 같다: 링크 묶음마다 그림 하나(빈 스킨이면 빈 그림)를 같은 색으로 같이 쓴다.
    /// 타일맵·프로젝트에 없는 레이어면 <see cref="ArgumentException"/>.
    /// </summary>
    public Skin AddSkin(Layer layer, string? name = null, Skin? copyFrom = null)
    {
        RequireSkinLayer(layer);
        if (copyFrom is not null) RequireSkin(layer, copyFrom);
        var skin = new Skin(UniqueSkinName(layer, string.IsNullOrWhiteSpace(name)
            ? copyFrom is null ? DomainStrings.Project_DefaultSkinName : DomainStrings.FormatProject_CopiedName(name: copyFrom.Name)
            : name));
        Edit(copyFrom is null ? DomainStrings.Project_AddSkin : DomainStrings.Project_DuplicateSkin, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            layer.SkinList.Add(skin);
            CopySkinCels(layer, copyFrom ?? layer.SkinList[0], skin, pixels: copyFrom is not null);
            layer.SetActiveSkinRaw(skin);
        });
        return skin;
    }

    /// <summary>
    /// 레이어와 모든 자손에 같은 이름의 스킨을 하나씩 넣어 지금 스킨으로 고르고, 그 묶음을 같은 이름의 스킨 프리셋으로 등록한다(실행취소 한 단계 — 예전 "그룹 스킨 추가").
    /// <paramref name="copy"/>면 레이어마다 지금 스킨의 그림을 복사하고, 아니면 빈 스킨이다. 스킨 이름은 그 레이어들 어디에서도 겹치지 않게, 프리셋 이름은 프리셋끼리 겹치지 않게 바뀐다.
    /// 만든 프리셋을 돌려준다(항목 = 레이어 → 새 스킨, 위 레이어부터).
    /// </summary>
    public SkinPreset AddSkinToSubtree(Layer layer, string? name = null, bool copy = false)
    {
        RequireSkinLayer(layer);
        var members = SubtreeOf(layer);
        members.Reverse(); // 위(부모)부터
        var baseName = string.IsNullOrWhiteSpace(name) ? DomainStrings.Project_DefaultSkinName : name.Trim();
        bool Taken(string n) => members.Any(m => m.SkinList.Any(s => s.Name == n));
        var skinName = baseName;
        for (int i = 2; Taken(skinName); i++) skinName = $"{baseName} {i}";
        var preset = new SkinPreset(UniqueSkinPresetName(skinName), []);
        Edit(DomainStrings.Project_AddSkin, ProjectChange.Layers | ProjectChange.Pixels | ProjectChange.Settings, () =>
        {
            var entries = new List<SkinPresetEntry>(members.Count);
            foreach (var m in members)
            {
                var skin = new Skin(skinName);
                var from = copy ? m.ActiveSkin! : m.SkinList[0];
                m.SkinList.Add(skin);
                CopySkinCels(m, from, skin, pixels: copy);
                m.SetActiveSkinRaw(skin);
                entries.Add(new SkinPresetEntry(m, skin));
            }
            preset.Entries = entries;
            _presets.Add(preset);
        });
        return preset;
    }

    /// <summary>
    /// 레이어의 <paramref name="from"/> 스킨 자신의 셀을 <paramref name="to"/> 스킨 셀로 가져온다(<see cref="Edit(string, ProjectChange, Action)"/> 안에서):
    /// <paramref name="pixels"/>면 그림이 있는 셀(일반 레이어면 캔버스 밖 그림도)을 복사하고, 링크 묶음은 그림이 없어도(<paramref name="pixels"/>가 false면 늘) 빈 그림 하나를 같이 써
    /// 새 스킨에서도 같은 프레임끼리 같은 색으로 링크된다. 빈 셀은 빈 칸으로 보이고, 링크된 칸에 그리면 링크된 칸이 함께 바뀐다.
    /// </summary>
    private void CopySkinCels(Layer layer, Skin from, Skin to, bool pixels)
    {
        var fromKey = SkinKey(layer, from);
        var toKey = SkinKey(layer, to);
        var size = CelSize(layer);
        var take = OncePerImage(cel => pixels && HasContent(cel) ? cel.Clone() : new PixelImage(size.Width, size.Height));
        var linked = new HashSet<int>(LinkGroupsOf(layer, fromKey).SelectMany(g => g.Frames));
        for (int f = 0; f < _frames.Count; f++)
        {
            var key = new CelKey(layer, _frames[f], fromKey);
            if (_cels.TryGetValue(key, out var cel) && (linked.Contains(f) || pixels && HasContent(cel)))
                _cels[key with { Skin = toKey }] = take(cel);
            if (pixels && _overflow.TryGetValue(key, out var outside)) _overflow[key with { Skin = toKey }] = outside;
        }
    }

    /// <summary>레이어의 셀·캔버스 밖 그림 키의 스킨을 <paramref name="map"/>으로 바꾼다(첫 스킨이 바뀔 때 — null 키 맞바꾸기). Edit 안에서.</summary>
    private void RekeySkins(Layer layer, Func<Skin?, Skin?> map)
    {
        var cels = CelsOf(layer);
        foreach (var (key, _) in cels) _cels.Remove(key);
        foreach (var (key, cel) in cels) _cels[key with { Skin = map(key.Skin) }] = cel;
        var outside = _overflow.Where(kv => ReferenceEquals(kv.Key.Layer, layer)).ToList();
        foreach (var (key, _) in outside) _overflow.Remove(key);
        foreach (var (key, o) in outside) _overflow[key with { Skin = map(key.Skin) }] = o;
    }

    /// <summary>스킨 이름 바꾸기. 비었거나 그 레이어에 같은 이름이 있으면 <see cref="ArgumentException"/>.</summary>
    public void RenameSkin(Layer layer, Skin skin, string name)
    {
        RequireSkin(layer, skin);
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException(DomainStrings.Project_EmptySkinName, nameof(name));
        if (layer.SkinList.Any(s => !ReferenceEquals(s, skin) && s.Name == name))
            throw new ArgumentException(DomainStrings.FormatProject_DuplicateSkinName(name: name), nameof(name));
        if (skin.Name == name) return;
        Edit(DomainStrings.Project_RenameSkin, ProjectChange.Layers, () => skin.Name = name);
    }

    /// <summary>
    /// 스킨 삭제(그 스킨의 셀·캔버스 밖 그림도 지움, 실행취소됨). 첫 스킨을 지우면 다음 스킨이 첫 스킨이 된다. 지금 스킨이었으면 바로 앞(없으면 새 첫) 스킨을 고른다.
    /// 그 스킨을 가리키던 스킨 프리셋 항목은 쓰이지 않게 된다. 레이어의 마지막 스킨은 지울 수 없다(<see cref="InvalidOperationException"/>).
    /// </summary>
    public void DeleteSkin(Layer layer, Skin skin)
    {
        int index = RequireSkin(layer, skin);
        var skins = layer.SkinList;
        if (skins.Count <= 1) throw new InvalidOperationException(DomainStrings.Project_CannotDeleteLastSkin);
        bool wasActive = ReferenceEquals(layer.ActiveSkin, skin);
        Edit(DomainStrings.Project_DeleteSkin, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var gone = index == 0 ? null : skin;
            foreach (var (key, _) in CelsOf(layer))
                if (ReferenceEquals(key.Skin, gone)) _cels.Remove(key);
            foreach (var key in _overflow.Keys.Where(k => ReferenceEquals(k.Layer, layer) && ReferenceEquals(k.Skin, gone)).ToList()) _overflow.Remove(key);
            if (index == 0)
            {
                var next = skins[1];
                RekeySkins(layer, s => ReferenceEquals(s, next) ? null : s);
            }
            skins.RemoveAt(index);
            if (wasActive) layer.SetActiveSkinRaw(skins[Math.Max(0, index - 1)]);
        });
    }

    /// <summary>
    /// 스킨 순서 바꾸기(<see cref="Layer.Skins"/> 안 번호, <paramref name="to"/> = 옮긴 뒤 위치, 범위로 자름). 첫 스킨이 바뀌면 새 첫 스킨이 기본 스킨이 된다(셀은 그대로 보임).
    /// </summary>
    public void MoveSkin(Layer layer, int from, int to)
    {
        RequireSkinLayer(layer);
        var skins = layer.SkinList;
        if (from < 0 || from >= skins.Count) throw new ArgumentOutOfRangeException(nameof(from));
        to = Math.Clamp(to, 0, skins.Count - 1);
        if (from == to) return;
        Edit(DomainStrings.Project_ReorderSkin, ProjectChange.Layers | ProjectChange.Pixels, () =>
        {
            var oldBase = skins[0];
            var skin = skins[from];
            skins.RemoveAt(from);
            skins.Insert(to, skin);
            var newBase = skins[0];
            if (ReferenceEquals(oldBase, newBase)) return;
            // 첫 스킨 셀은 null 키라 키를 맞바꾼다: 옛 첫 스킨 셀 → 옛 첫 스킨 키, 새 첫 스킨 셀 → null 키.
            RekeySkins(layer, s => s is null ? oldBase : ReferenceEquals(s, newBase) ? null : s);
        });
    }

    /// <summary>스킨 하나를 앞(−)/뒤(+)로(<see cref="MoveSkin(Layer, int, int)"/>). 끝이면 그대로. 실행취소됨.</summary>
    public void MoveSkin(Layer layer, Skin skin, int delta)
    {
        int i = RequireSkin(layer, skin);
        MoveSkin(layer, i, i + delta);
    }

    /// <summary>
    /// 레이어의 지금 스킨 고르기. 보기 전환이라 실행취소 기록에는 남기지 않지만 파일에 저장되고 내보내기 결과가 바뀌므로 수정됨 표시를 켠다. 열린 스트로크는 먼저 확정한다.
    /// </summary>
    public void SetActiveSkin(Layer layer, Skin skin)
    {
        RequireSkin(layer, skin);
        if (ReferenceEquals(layer.ActiveSkin, skin)) return;
        CommitOpenStrokes();
        layer.SetActiveSkinRaw(skin);
        NotifyChanged(ProjectChange.Layers | ProjectChange.Pixels);
    }

    // ───────────── 스킨 프리셋 ─────────────

    private void RequirePreset(SkinPreset preset)
    {
        if (!_presets.Contains(preset)) throw new ArgumentException(DomainStrings.Project_PresetNotInProject, nameof(preset));
    }

    /// <summary>겹치지 않는 스킨 프리셋 이름(겹치면 " 2", " 3" …). 비면 "프리셋".</summary>
    public string UniqueSkinPresetName(string baseName, SkinPreset? self = null)
    {
        baseName = baseName.Trim();
        if (baseName.Length == 0) baseName = DomainStrings.Project_DefaultPresetName;
        bool Taken(string n) => _presets.Any(o => !ReferenceEquals(o, self) && o.Name == n);
        if (!Taken(baseName)) return baseName;
        for (int i = 2; ; i++)
            if (!Taken($"{baseName} {i}")) return $"{baseName} {i}";
    }

    /// <summary>지금 착장: 스킨이 둘 이상인 레이어마다(위 레이어부터) 지금 스킨.</summary>
    public IReadOnlyList<SkinPresetEntry> CurrentSkinEntries()
    {
        var entries = new List<SkinPresetEntry>();
        for (int i = _layers.Count - 1; i >= 0; i--)
            if (_layers[i] is { IsTilemap: false } l && l.SkinList.Count > 1) entries.Add(new SkinPresetEntry(l, l.ActiveSkin!));
        return entries;
    }

    /// <summary>항목 확인: 프로젝트의 레이어와 그 레이어의 스킨이어야 하고, 레이어마다 하나(뒤 항목이 앞 항목을 덮음 — 자리는 앞 항목).</summary>
    private SkinPresetEntry[] CleanEntries(IReadOnlyList<SkinPresetEntry> entries)
    {
        var list = new List<SkinPresetEntry>();
        foreach (var e in entries)
        {
            if (IndexOfReference(_layers, e.Layer) < 0 || e.Layer.IsTilemap || !e.Layer.SkinList.Contains(e.Skin))
                throw new ArgumentException(DomainStrings.FormatProject_InvalidPresetEntry(layer: e.Layer.Name, skin: e.Skin.Name), nameof(entries));
            int at = list.FindIndex(x => ReferenceEquals(x.Layer, e.Layer));
            if (at >= 0) list[at] = e;
            else list.Add(e);
        }
        return [.. list];
    }

    /// <summary>
    /// 스킨 프리셋을 새로 등록한다(맨 끝, 이름은 겹치지 않게, 실행취소됨). <paramref name="entries"/>가 null이면 지금 착장(<see cref="CurrentSkinEntries"/>).
    /// 프로젝트에 없는 레이어·그 레이어에 없는 스킨이 끼면 <see cref="ArgumentException"/>.
    /// </summary>
    public SkinPreset SaveSkinPreset(string? name = null, IReadOnlyList<SkinPresetEntry>? entries = null)
    {
        var preset = new SkinPreset(UniqueSkinPresetName(name ?? ""), CleanEntries(entries ?? CurrentSkinEntries()));
        Edit(DomainStrings.Project_SavePreset, ProjectChange.Layers | ProjectChange.Settings, () => _presets.Add(preset));
        return preset;
    }

    /// <summary>
    /// 스킨 프리셋 내용을 바꾼다(이름은 그대로, 실행취소됨). <paramref name="entries"/>가 null이면 그 프리셋에 든 레이어(아직 있는 것)의 지금 스킨으로 덮어쓴다
    /// (담는 레이어는 그대로 — 레이어를 바꾸려면 항목을 준다).
    /// </summary>
    public void OverwriteSkinPreset(SkinPreset preset, IReadOnlyList<SkinPresetEntry>? entries = null)
    {
        RequirePreset(preset);
        var list = CleanEntries(entries ?? preset.Entries.Where(e => IndexOfReference(_layers, e.Layer) >= 0 && !e.Layer.IsTilemap)
            .Select(e => new SkinPresetEntry(e.Layer, e.Layer.ActiveSkin!)).ToList());
        Edit(DomainStrings.Project_OverwritePreset, ProjectChange.Layers | ProjectChange.Settings, () => preset.Entries = list);
    }

    /// <summary>스킨 프리셋 이름 바꾸기. 비었거나 같은 이름이 있으면 <see cref="ArgumentException"/>.</summary>
    public void RenameSkinPreset(SkinPreset preset, string name)
    {
        RequirePreset(preset);
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException(DomainStrings.Project_EmptyPresetName, nameof(name));
        if (_presets.Any(o => !ReferenceEquals(o, preset) && o.Name == name))
            throw new ArgumentException(DomainStrings.FormatProject_DuplicatePresetName(name: name), nameof(name));
        if (preset.Name == name) return;
        Edit(DomainStrings.Project_RenamePreset, ProjectChange.Layers | ProjectChange.Settings, () => preset.Name = name);
    }

    /// <summary>스킨 프리셋 삭제(내보내기 시트의 프리셋 목록에서도 뺀다, 실행취소됨).</summary>
    public void DeleteSkinPreset(SkinPreset preset)
    {
        RequirePreset(preset);
        Edit(DomainStrings.Project_DeletePreset, ProjectChange.Layers | ProjectChange.Settings, () =>
        {
            _presets.Remove(preset);
            foreach (var o in Export.AllSheets.SelectMany(s => (SheetOptions?[])[s.Common, s.PartsOverride]))
                if (o is not null && o.Presets.Contains(preset)) o.Presets = o.Presets.Where(x => !ReferenceEquals(x, preset)).ToArray();
        });
    }

    /// <summary>스킨 프리셋 순서 바꾸기(<paramref name="to"/> = 옮긴 뒤 위치, 실행취소됨).</summary>
    public void MoveSkinPreset(int from, int to)
    {
        var preset = _presets[from];
        to = Math.Clamp(to, 0, _presets.Count - 1);
        if (from == to) return;
        Edit(DomainStrings.Project_ReorderPreset, ProjectChange.Layers | ProjectChange.Settings, () =>
        {
            _presets.RemoveAt(from);
            _presets.Insert(to, preset);
        });
    }

    /// <summary>프리셋 항목 중 지금 프로젝트에서 쓸 수 없는 것(지운 레이어·스킨)의 설명. 비면 모두 쓸 수 있다.</summary>
    public IReadOnlyList<string> MissingSkinPresetEntries(SkinPreset preset)
    {
        var missing = new List<string>();
        foreach (var (layer, skin) in preset.Entries)
        {
            if (IndexOfReference(_layers, layer) < 0 || layer.IsTilemap) missing.Add(DomainStrings.FormatProject_MissingPresetLayer(name: layer.Name));
            else if (!layer.SkinList.Contains(skin)) missing.Add(DomainStrings.FormatProject_MissingPresetSkin(layer: layer.Name, skin: skin.Name));
        }
        return missing;
    }

    /// <summary>항목이 지금 쓸 수 있는지(레이어가 프로젝트에 있고 그 스킨이 아직 있음).</summary>
    private bool Usable(SkinPresetEntry e) => !e.Layer.IsTilemap && e.Layer.SkinList.Contains(e.Skin) && IndexOfReference(_layers, e.Layer) >= 0;

    /// <summary>
    /// 스킨 프리셋을 입힌다: 프리셋에 든 레이어의 지금 스킨만 바꾸고 나머지 레이어는 그대로다(프리셋 겹쳐 입히기). 쓸 수 없는 항목(지운 레이어·스킨)은 건너뛰고
    /// 그 설명을 돌려준다. 스킨 고르기와 같이 실행취소 기록 없이 수정됨 표시를 켠다.
    /// </summary>
    public IReadOnlyList<string> ApplySkinPreset(SkinPreset preset)
    {
        RequirePreset(preset);
        CommitOpenStrokes();
        bool changed = false;
        foreach (var e in preset.Entries)
        {
            if (!Usable(e) || ReferenceEquals(e.Layer.ActiveSkin, e.Skin)) continue;
            e.Layer.SetActiveSkinRaw(e.Skin);
            changed = true;
        }
        if (changed) NotifyChanged(ProjectChange.Layers | ProjectChange.Pixels);
        return MissingSkinPresetEntries(preset);
    }

    /// <summary>스킨 프리셋을 입히면(<see cref="ApplySkinPreset"/>) 지금 스킨이 바뀔 잠긴 레이어들(조상이 잠긴 레이어 포함). 비면 잠긴 레이어는 그대로다.</summary>
    public IReadOnlyList<Layer> LockedLayersChangedBy(SkinPreset preset)
    {
        RequirePreset(preset);
        return preset.Entries.Where(e => Usable(e) && IsLayerLocked(e.Layer) && !ReferenceEquals(e.Layer.ActiveSkin, e.Skin)).Select(e => e.Layer).ToList();
    }

    /// <summary>
    /// 프리셋을 입혔을 때 레이어가 보일 스킨: 프리셋의 그 레이어 항목(쓸 수 있을 때), 없으면 지금 스킨. <paramref name="preset"/>이 null이면 지금 스킨. 타일맵이면 null.
    /// </summary>
    public static Skin? SkinUnder(Layer layer, SkinPreset? preset)
    {
        if (preset is not null && !layer.IsTilemap)
            foreach (var e in preset.Entries)
                if (ReferenceEquals(e.Layer, layer) && layer.SkinList.Contains(e.Skin)) return e.Skin;
        return layer.ActiveSkin;
    }

    /// <summary>프리셋의 쓸 수 있는 항목을 레이어 → 스킨 표로(<see cref="FlattenDressed"/>·<see cref="PlanSubtree"/>에 넘김). null이면 빈 표.</summary>
    public IReadOnlyDictionary<Layer, Skin> DressOf(SkinPreset? preset)
    {
        var dress = new Dictionary<Layer, Skin>();
        if (preset is null) return dress;
        foreach (var e in preset.Entries)
            if (Usable(e)) dress[e.Layer] = e.Skin;
        return dress;
    }
}
