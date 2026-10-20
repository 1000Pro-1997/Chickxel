using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>
/// 여러 파일을 프로젝트 하나로 합치는 방식. 어느 방식이든 <c>.gpixel</c>의 구조(레이어·그룹·부위·스킨·부위 전용 스킨·착장·효과·자동 배치·
/// 태그·프레임 길이·이름·피벗)는 버리지 않고 옮긴다 — 자세한 규칙은 <see cref="ProjectMerge"/>.
/// </summary>
public enum MergeMode
{
    /// <summary>그림 파일마다 일반 레이어 1개(캔버스 크기, 기준점에 배치), 프로젝트는 레이어·그룹을 통째로. 첫 파일이 맨 위.</summary>
    Layers,
    /// <summary>그룹 1개에 그림 파일마다 부위 1개(파일 크기 그대로, 기준점 위치). 프로젝트에 그룹이 있으면 첫 그룹에 부위를 더한다.</summary>
    Group,
    /// <summary>파일마다 프레임(파일의 프레임을 차례로 이어 붙임). 프로젝트는 레이어 구조를 그대로 가져온다.</summary>
    Frames,
    /// <summary>그룹 1개, 그림 파일마다 그 그룹의 스킨 1개. 프로젝트에 그룹이 있으면(스킨이 여럿인 그룹 먼저) 그 그룹에 스킨을 더한다.</summary>
    Skins,
}

/// <summary>
/// 합칠 파일 하나. 모든 파일을 프로젝트로 다룬다: PNG는 레이어 1개(이름 = 파일 이름)짜리 프로젝트,
/// <c>.gpixel</c>은 그 프로젝트 그대로(읽기만 — 합치기는 늘 복사본을 만들므로 열려 있는 문서를 넘겨도 된다).
/// </summary>
public sealed class MergeSource
{
    /// <summary>그림 파일: 프레임마다 그림 한 장(모두 같은 크기)과 길이.</summary>
    public MergeSource(string name, IReadOnlyList<PixelImage> frames, IReadOnlyList<int> durations)
    {
        if (frames.Count == 0) throw new ArgumentException("프레임이 없습니다.", nameof(frames));
        if (durations.Count != frames.Count) throw new ArgumentException("프레임 길이 수가 프레임 수와 다릅니다.", nameof(durations));
        var size = frames[0].Size;
        if (frames.Any(f => f.Size != size)) throw new ArgumentException("프레임 그림 크기가 서로 다릅니다.", nameof(frames));
        var p = SpriteProject.CreateBlank(size.Width, size.Height);
        var layer = new Layer(name);
        p.AddLayerRaw(layer);
        for (int i = 0; i < frames.Count; i++)
        {
            var frame = new Frame { DurationMs = durations[i] };
            p.AddFrameRaw(frame);
            p.SetCelRaw(layer, frame, frames[i]);
        }
        Name = name;
        FileName = name + ".png";
        Project = p;
    }

    /// <summary>프로젝트(<c>.gpixel</c> 또는 열려 있는 문서). 바꾸지 않고 읽기만 한다.</summary>
    public MergeSource(string name, SpriteProject project)
    {
        if (project.Frames.Count == 0) throw new ArgumentException("프레임이 없습니다.", nameof(project));
        Name = name;
        FileName = name + SpriteProject.FileExtension;
        Project = project;
        IsProject = true;
    }

    /// <summary>확장자 뺀 파일 이름(레이어·부위·스킨·태그 이름에 쓴다).</summary>
    public string Name { get; }
    /// <summary>화면에 보일 파일 이름(확장자 포함).</summary>
    public string FileName { get; init; }
    /// <summary>내용(그림 파일이면 레이어 1개짜리 프로젝트). 읽기만 한다.</summary>
    public SpriteProject Project { get; }
    /// <summary>프로젝트 파일(또는 열린 문서)인지 — Unity 설정을 가져오고 착장을 옮긴다. 그림 파일이면 false.</summary>
    public bool IsProject { get; }
    /// <summary>캔버스 크기.</summary>
    public Size Size => Project.Size;
    public int FrameCount => Project.Frames.Count;

    /// <summary>
    /// 그림 한 장처럼 다룰 수 있으면 그 유일한 레이어(그룹 없음, 일반 레이어 1개, 효과 없음, 보임, 불투명) — 그림 파일은 늘 그렇다.
    /// null이면 지킬 구조가 있는 프로젝트다(<see cref="HasStructure"/>).
    /// </summary>
    internal Layer? ImageLayer
        => Project.Layers is [{ Kind: LayerKind.Normal, Visible: true, Opacity: 255 } l] && l.Effects.Count == 0 ? l : null;

    /// <summary>그림 한 장으로 볼 수 없는 구조(그룹·여러 레이어·효과·숨김·불투명도)가 있는 프로젝트인지 — 합칠 때 그 구조를 그대로 옮긴다.</summary>
    public bool HasStructure => ImageLayer is null;

    /// <summary>그룹이 있는 프로젝트인지.</summary>
    public bool HasGroups => Project.Layers.Any(l => l.IsGroup);

    /// <summary>
    /// PNG 또는 <c>.gpixel</c>을 읽는다. <paramref name="openDocument"/>(그 파일로 열려 있는 문서 — 저장하지 않은 편집 포함)가 있으면
    /// 파일 대신 그것을 쓴다(바꾸지 않음).
    /// </summary>
    public static MergeSource Load(string path, SpriteProject? openDocument = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var fileName = Path.GetFileName(path);
        if (openDocument is not null) return new MergeSource(name, openDocument) { FileName = fileName };
        if (!path.EndsWith(SpriteProject.FileExtension, StringComparison.OrdinalIgnoreCase))
            return new MergeSource(name, [PngIO.Load(path)], [Frame.DefaultDurationMs]) { FileName = fileName };
        return new MergeSource(name, ProjectIO.Load(path)) { FileName = fileName };
    }
}

/// <summary>합치기 설정.</summary>
/// <param name="Mode">합치는 방식.</param>
/// <param name="Width">결과 캔버스 가로.</param>
/// <param name="Height">결과 캔버스 세로.</param>
/// <param name="Anchor">파일 그림(프로젝트면 그 캔버스)을 결과 캔버스에 놓을 기준점. 부위 위치·피벗도 같은 만큼 옮긴다.</param>
/// <param name="Name">결과 이름(그룹·스킨 방식에서 새로 만드는 그룹의 이름 — 프로젝트의 그룹에 더하면 그 그룹 이름 그대로).</param>
/// <param name="BlankCover">그룹 방식에서 빈 대표 이미지: 그 그룹의 부위를 모두(원래 있던 부위 포함) 숨겨 그룹(타임라인 그룹 행·프레임 시트)이 비어 보이게 한다.
/// 부위 그림은 그대로라 부위 아틀라스에는 모두 나가고, 눈을 켜면 보인다. 다른 방식에서는 쓰지 않는다.</param>
public sealed record MergeOptions(MergeMode Mode, int Width, int Height, Anchor Anchor, string Name, bool BlankCover = false);

/// <summary>
/// 파일 여러 개 → 새 프로젝트(기록 없음, 수정됨 아님). 원본 프로젝트는 바꾸지 않는다.
/// <para>
/// "그림 같은" 파일 = PNG, 또는 일반 레이어 하나뿐(효과·숨김·불투명도 없음)인 <c>.gpixel</c>(<see cref="MergeSource.ImageLayer"/>).
/// 그 밖의 프로젝트는 구조를 그대로 복사한다: 레이어·그룹·부위(크기·위치·피벗·테두리)·스킨(부위 전용 포함)·지금 스킨·부위 재정의·효과·자동 배치·
/// 착장(복사한 그룹·스킨을 가리키도록 바꿈). 캔버스가 바뀌면 일반 레이어 셀은 기준점으로 옮기고, 부위 위치·프레임 피벗·기본 피벗은 같은 만큼 옮겨 그림 자리를 지킨다.
/// </para>
/// <list type="bullet">
/// <item>레이어: 첫 파일이 맨 위. 그림 같은 파일은 레이어 1개(이름 = 파일 이름), 프로젝트는 레이어·그룹을 통째로.</item>
/// <item>그룹: 그룹이 있는 첫 프로젝트의 맨 위 그룹에 그림 같은 파일마다 부위 1개(파일 크기, 기준점 위치)를 더한다 — 목록에서 그 프로젝트보다 앞 파일은 원래 부위 위, 뒤 파일은 아래.
/// 그룹 없는 여러 레이어 프로젝트는 레이어마다 부위 1개. 그룹이 있는 다른 프로젝트는 옆에 그대로. 그룹 있는 프로젝트가 없으면 새 그룹(<see cref="MergeOptions.Name"/>, 첫 파일이 맨 위 부위).
/// <see cref="MergeOptions.BlankCover"/>면 그 그룹의 부위를 모두 숨긴다.</item>
/// <item>스킨: 스킨이 여럿인 첫 그룹(없으면 첫 그룹)에 그림 같은 파일마다 새 전체 스킨(이름 = 파일 이름)을 더한다. 그림은 크기가 같은 부위(없으면 가장 큰 부위)에,
/// 부위보다 크면 부위를 기준점으로 키운다(원래 그림 자리 그대로). 다른 프로젝트의 그룹은 부위(이름·크기·위치·피벗·테두리·효과)가 같으면 그 스킨을 모두 더하고
/// (빈 칸은 그 그룹 첫 스킨 그림으로 채워 보이는 그림 그대로), 다르면 옆에 그대로. 그룹 있는 프로젝트가 없으면 새 그룹에 부위 "부위" 하나(가장 큰 파일 크기), 파일마다 스킨 하나.
/// 그림 같지 않은 그룹 없는 프로젝트는 옆에 레이어 그대로.</item>
/// <item>프레임: 파일 순서대로 프레임을 이어 붙인다(태그·효과 범위도 그만큼 민다). 프로젝트의 레이어는 이미 있는 레이어와 이름·종류·설정(그룹이면 부위·첫 스킨까지)이 같으면
/// 그 레이어에 이어 붙이고(스킨은 이름으로 합침), 아니면 새 레이어로 넣는다. 그림 같은 파일은 같은 이름의 레이어, 없으면 맨 위의 그릴 수 있는 일반 레이어, 그것도 없으면 새 레이어.
/// 프레임이 하나인 파일은 칸 이름이 없으면 파일 이름, 여러 개인 파일은 태그가 없으면 파일 이름으로 태그 하나.</item>
/// </list>
/// 레이어·그룹·스킨 방식은 프레임 수 = 가장 많은 파일의 프레임 수, 프레임 길이는 그 프레임이 있는 첫 파일의 것, 칸 이름·피벗은 그것이 있는 첫 파일의 것.
/// 태그는 모두 옮기고 이름이 겹치면(같은 범위면 하나로) 이름을 바꾼다. 레이어 이름은 겹치지 않게(앞 파일이 원래 이름) 바꾼다.
/// Unity 설정은 첫 프로젝트 파일의 것(없으면 <c>spriteDefaults</c>), 내보내기 설정은 새 프로젝트 기본값.
/// </summary>
public static class ProjectMerge
{
    /// <param name="notes">있으면 구조를 어떻게 옮겼는지(유지한 그룹·스킨, 새 스킨·부위 자리, 따로 넣은 그룹, 바뀐 태그 이름) 설명을 더한다.</param>
    public static SpriteProject Merge(IReadOnlyList<MergeSource> sources, MergeOptions options, SpriteSettings? spriteDefaults = null,
        List<string>? notes = null)
    {
        if (sources.Count == 0) throw new ArgumentException("합칠 파일이 없습니다.", nameof(sources));
        var p = new Builder(sources, options, notes).Build();
        if (sources.FirstOrDefault(s => s.IsProject) is { } first)
        {
            p.Sprite.CopyFrom(first.Project.Sprite);
            p.Sprite.DefaultPivot = Builder.ShiftPivot(first.Project.Sprite.DefaultPivot, first, options);
        }
        else if (spriteDefaults is not null) p.Sprite.CopyFrom(spriteDefaults);
        return p;
    }

    /// <summary>
    /// 결과 이름 제안: 모든 이름에 공통인 앞부분(구분자 <c>_ - 공백 . @</c> 경계까지, 예: potato_growing·potato_mature → potato),
    /// 없으면 공통 뒷부분, 그것도 없으면 첫 이름.
    /// </summary>
    public static string SuggestName(IReadOnlyList<string> names)
    {
        if (names.Count == 0) return "merged";
        if (names.Count == 1) return names[0];
        var prefix = CommonAffix(names, fromEnd: false);
        if (prefix.Length > 0) return prefix;
        var suffix = CommonAffix(names, fromEnd: true);
        return suffix.Length > 0 ? suffix : names[0];
    }

    private static bool IsSeparator(char c) => c is '_' or '-' or ' ' or '.' or '@';

    private static string CommonAffix(IReadOnlyList<string> names, bool fromEnd)
    {
        string At(string s, int len) => fromEnd ? s[^len..] : s[..len];
        char CharAt(string s, int k) => fromEnd ? s[s.Length - 1 - k] : s[k];
        int n = names.Min(s => s.Length);
        int common = 0;
        while (common < n && names.All(s => char.ToLowerInvariant(CharAt(s, common)) == char.ToLowerInvariant(CharAt(names[0], common)))) common++;
        // 단어 경계까지 줄인다: 모든 이름에서 다음 글자가 구분자이거나 이름 끝이어야 한다.
        while (common > 0 && !names.All(s => s.Length == common || IsSeparator(CharAt(s, common)) || IsSeparator(CharAt(s, common - 1))))
            common--;
        var affix = At(names[0], common);
        return fromEnd ? affix.TrimStart('_', '-', ' ', '.', '@') : affix.TrimEnd('_', '-', ' ', '.', '@');
    }

    /// <summary>합치기 한 번의 작업 상태. 레이어는 <see cref="_stack"/>(아래 → 위)에 모았다가 마지막에 이름을 맞춰 넣는다.</summary>
    private sealed class Builder(IReadOnlyList<MergeSource> sources, MergeOptions options, List<string>? notes)
    {
        private readonly SpriteProject _p = SpriteProject.CreateBlank(options.Width, options.Height);
        private readonly List<Layer> _stack = new();
        private readonly Dictionary<MergeSource, Maps> _maps = new();
        /// <summary>레이어·그룹·스킨 방식: 결과 프레임(모든 파일의 프레임 번호가 같음).</summary>
        private Frame[] _frames = [];

        /// <summary>원본 레이어·스킨 → 결과 레이어·스킨(착장 옮기기용).</summary>
        private sealed class Maps
        {
            public readonly Dictionary<Layer, Layer> Layers = new();
            public readonly Dictionary<Skin, Skin> Skins = new();
        }

        private int W => options.Width;
        private int H => options.Height;

        public SpriteProject Build()
        {
            if (options.Mode == MergeMode.Frames) BuildFrames();
            else
            {
                BuildSharedFrames();
                switch (options.Mode)
                {
                    case MergeMode.Group: BuildGroup(); break;
                    case MergeMode.Skins: BuildSkins(); break;
                    default: BuildLayers(); break;
                }
                foreach (var s in sources)
                    foreach (var t in s.Project.Tags) AddTag(t.Clone());
            }
            FinishLayers();
            CopyOutfits();
            return _p;
        }

        // ───────────── 방식별 ─────────────

        private void BuildLayers()
        {
            var perSource = new List<List<Layer>>();
            foreach (var s in sources)
            {
                var list = new List<Layer>();
                if (s.ImageLayer is { } l)
                {
                    var layer = CopyProps(l, null, 0);
                    layer.Name = s.Name;
                    MapsOf(s).Layers[l] = layer;
                    CopyCanvasCels(s, l, layer, _frames);
                    list.Add(layer);
                }
                else
                {
                    foreach (var top in TopLevel(s)) list.AddRange(CopyBlock(s, top, _frames, 0));
                    Note($"{s.FileName}의 {Kept(s)} 유지합니다.");
                }
                perSource.Add(list);
            }
            for (int i = perSource.Count - 1; i >= 0; i--) _stack.AddRange(perSource[i]);
        }

        private void BuildGroup()
        {
            var target = FindTarget(preferSkins: false);
            var perSource = new List<Layer>[sources.Count];
            var newParts = new List<Layer>[sources.Count]; // 파일마다 새 부위(아래 → 위)
            Layer? group = null;
            if (target is { } t)
            {
                perSource[t.Index] = CopyAll(t.Source);
                group = MapsOf(t.Source).Layers[t.Group];
                Note($"{t.Source.FileName}의 {Kept(t.Source)} 유지합니다 — 그림은 \"{t.Group.Name}\" 그룹의 새 부위로 넣습니다.");
            }
            group ??= new Layer(NewGroupName()) { Kind = LayerKind.Group };
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (target is { } tt && i == tt.Index) continue;
                if (s.HasGroups)
                {
                    perSource[i] = CopyAll(s);
                    Note($"{s.FileName}의 {Kept(s)} 그대로 옆에 넣습니다.");
                    continue;
                }
                perSource[i] = [];
                var layers = TopLevel(s).ToList();
                newParts[i] = layers.Select(l => ImagePart(s, l, layers.Count == 1 ? s.Name : $"{s.Name} {l.Name}", group)).ToList();
                if (layers.Count > 1) Note($"{s.FileName}의 레이어 {layers.Count}개를 부위 {layers.Count}개로 넣습니다.");
            }

            // 목록 순서 = 위 → 아래: 대상 파일보다 앞 파일의 부위는 원래 부위 위, 뒤 파일은 아래.
            int split = target?.Index ?? sources.Count;
            var below = new List<Layer>();
            var above = new List<Layer>();
            for (int i = sources.Count - 1; i >= 0; i--)
                if (newParts[i] is { } parts) (i > split ? below : above).AddRange(parts);
            if (target is { } tg)
            {
                var list = perSource[tg.Index];
                int at = list.IndexOf(group);
                int start = at - list.Count(l => ReferenceEquals(l.Parent, group));
                list.InsertRange(at, above);
                list.InsertRange(start, below);
            }
            else
            {
                perSource[0] = [.. above, group];
                for (int i = 1; i < sources.Count; i++) perSource[i] = [];
            }
            if (options.BlankCover)
                foreach (var part in perSource.SelectMany(l => l).Where(l => ReferenceEquals(l.Parent, group))) part.Visible = false;
            if (options.BlankCover && target is not null) Note($"빈 대표 이미지: \"{group.Name}\" 그룹의 부위를 모두(원래 부위 포함) 숨깁니다.");
            for (int i = sources.Count - 1; i >= 0; i--) _stack.AddRange(perSource[i]);
        }

        private void BuildSkins()
        {
            var target = FindTarget(preferSkins: true);
            var perSource = new List<Layer>[sources.Count];
            Layer? group = null;
            Skin? firstSkin = null; // 새 그룹이면 첫 그림 파일의 스킨(= 첫 스킨)
            List<Layer> GroupParts() => perSource.Where(l => l is not null).SelectMany(l => l).Where(l => ReferenceEquals(l.Parent, group)).ToList();

            if (target is { } t)
            {
                perSource[t.Index] = CopyAll(t.Source);
                group = MapsOf(t.Source).Layers[t.Group];
                Note($"{t.Source.FileName}의 {Kept(t.Source)} 유지합니다 — 그림은 \"{t.Group.Name}\" 그룹의 새 스킨으로 넣습니다.");
            }
            else if (sources.Any(s => s.ImageLayer is not null))
            {
                // 그룹 있는 프로젝트가 없으면: 새 그룹, 부위 "부위" 하나(가장 큰 그림 파일 크기), 그림 파일마다 스킨 하나(첫 파일 = 첫 스킨·지금 스킨).
                var images = sources.Where(s => s.ImageLayer is not null).ToList();
                group = new Layer(NewGroupName()) { Kind = LayerKind.Group };
                var size = new Size(images.Max(s => s.Size.Width), images.Max(s => s.Size.Height));
                var (x, y) = PixelImage.AnchorOffset(size.Width, size.Height, W, H, options.Anchor);
                var part = new Layer("부위") { Kind = LayerKind.Part, Parent = group, PartSize = size, PartOffset = new Point(x, y) };
                group.ReplaceSkinsRaw([firstSkin = new Skin(images[0].Name)]);
                perSource[sources.ToList().IndexOf(images[0])] = [part, group];
            }

            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (target is { } tt && i == tt.Index) continue;
                perSource[i] ??= [];
                if (s.ImageLayer is { } l && group is not null)
                {
                    AddImageSkin(s, l, group, GroupParts(), perSource[i], target is not null, firstSkin);
                    firstSkin = null;
                    continue;
                }
                foreach (var top in TopLevel(s))
                {
                    if (top.IsGroup && group is not null && target is not null && SkinsFit(s, top, group, GroupParts()))
                    {
                        AppendSkins(s, top, group, GroupParts());
                        Note($"{s.FileName}의 \"{top.Name}\" 그룹 스킨 {top.AllSkins.Count}개를 \"{group.Name}\" 그룹에 더합니다.");
                        continue;
                    }
                    perSource[i].AddRange(CopyBlock(s, top, _frames, 0));
                    if (top.IsGroup && group is not null)
                        Note($"{s.FileName}의 \"{top.Name}\" 그룹은 부위가 \"{group.Name}\" 그룹과 달라 따로 넣습니다.");
                }
                if (!s.HasGroups) Note($"{s.FileName}의 {Kept(s)} 그대로 옆에 넣습니다.");
            }
            for (int i = sources.Count - 1; i >= 0; i--) _stack.AddRange(perSource[i]);
        }

        /// <summary>그림 같은 파일을 그룹의 새 전체 스킨으로: 크기가 같은 부위(없으면 가장 큰 부위, 없으면 새 부위)에, 부위보다 크면 부위를 키운다.</summary>
        private void AddImageSkin(MergeSource s, Layer image, Layer group, List<Layer> parts, List<Layer> ownList, bool noteIt, Skin? skin)
        {
            var size = s.Size;
            Layer? part = parts.LastOrDefault(x => x.PartSize == size)
                          ?? parts.OrderByDescending(x => x.PartSize.Width * x.PartSize.Height).ThenByDescending(x => parts.IndexOf(x)).FirstOrDefault();
            string grown = "";
            if (part is null)
            {
                var (x, y) = PixelImage.AnchorOffset(size.Width, size.Height, W, H, options.Anchor);
                part = new Layer("부위") { Kind = LayerKind.Part, Parent = group, PartSize = size, PartOffset = new Point(x, y) };
                ownList.Add(part); // 그룹 없이 부위만: 아래 FinishLayers 전에 그룹 바로 아래로 옮긴다.
            }
            else if (size.Width > part.PartSize.Width || size.Height > part.PartSize.Height)
            {
                GrowPart(part, group, Math.Max(size.Width, part.PartSize.Width), Math.Max(size.Height, part.PartSize.Height));
                grown = $", 부위를 {part.PartSize.Width}×{part.PartSize.Height}로 키움";
            }
            if (skin is null)
            {
                skin = new Skin(_p.UniqueSkinName(group, s.Name));
                group.SkinList.Insert(WholeSkinCount(group), skin);
            }
            for (int i = 0; i < s.FrameCount; i++)
                if (s.Project.GetCelRaw(image, s.Project.Frames[i], null) is { } cel && HasContent(cel))
                    _p.SetCelRaw(part, _frames[i], cel.ResizeCanvas(part.PartSize.Width, part.PartSize.Height, options.Anchor), skin);
            if (noteIt) Note($"{s.FileName} → \"{group.Name}\" 그룹의 새 스킨 \"{skin.Name}\"(부위 \"{part.Name}\"{grown}).");
        }

        /// <summary>부위를 기준점으로 키운다: 모든 스킨 셀을 옮기고, 그림이 캔버스에서 같은 자리에 남도록 위치를, 같은 지점을 가리키도록 피벗을 맞춘다.</summary>
        private void GrowPart(Layer part, Layer group, int width, int height)
        {
            var old = part.PartSize;
            var (ox, oy) = PixelImage.AnchorOffset(old.Width, old.Height, width, height, options.Anchor);
            foreach (var frame in _frames)
                foreach (var skin in group.SkinList.Where(k => k.Owner is null || ReferenceEquals(k.Owner, part)))
                    if (_p.GetCelRaw(part, frame, skin) is { } cel)
                        _p.SetCelRaw(part, frame, cel.ResizeCanvas(width, height, options.Anchor), skin);
            var (px, py) = part.PartPivot.ToLocalPixel(old.Width, old.Height);
            part.PartPivot = new Pivot((px + ox) / width, 1f - (py + oy) / height);
            part.PartOffset = new Point(part.PartOffset.X - ox, part.PartOffset.Y - oy);
            part.PartSize = new Size(width, height);
        }

        /// <summary>다른 프로젝트의 그룹 스킨을 대상 그룹에 그대로 더할 수 있는지: 부위 이름·크기·(옮긴) 위치·피벗·테두리·불투명도·효과와 그룹 효과·자동 배치가 같다.</summary>
        private bool SkinsFit(MergeSource s, Layer source, Layer group, List<Layer> parts)
        {
            var srcParts = s.Project.ChildrenOf(source);
            if (srcParts.Count == 0 || srcParts.Count != parts.Count || srcParts.Select(l => l.Name).Distinct().Count() != srcParts.Count) return false;
            if (!source.Effects.SequenceEqual(group.Effects) || source.Opacity != group.Opacity || source.AutoLayout != group.AutoLayout) return false;
            var shift = Shift(s);
            foreach (var sp in srcParts)
            {
                if (parts.FirstOrDefault(x => x.Name == sp.Name) is not { } tp) return false;
                if (sp.PartSize != tp.PartSize || new Point(sp.PartOffset.X + shift.X, sp.PartOffset.Y + shift.Y) != tp.PartOffset
                    || sp.PartPivot != tp.PartPivot || sp.PartBorder != tp.PartBorder || sp.Opacity != tp.Opacity || !sp.Effects.SequenceEqual(tp.Effects))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 다른 프로젝트 그룹의 스킨을 모두(부위 전용 포함) 대상 그룹에 새 스킨으로 더한다. 원래 그룹의 첫 스킨이 아니던 스킨의 빈 칸은
        /// 원래 첫 스킨 그림으로 채워(대상 그룹에서는 다른 첫 스킨을 물려받게 되므로) 보이던 그림 그대로 둔다.
        /// </summary>
        private void AppendSkins(MergeSource s, Layer source, Layer group, List<Layer> parts)
        {
            var maps = MapsOf(s);
            var src = s.Project;
            maps.Layers[source] = group;
            var srcParts = src.ChildrenOf(source);
            foreach (var sp in srcParts) maps.Layers[sp] = parts.First(x => x.Name == sp.Name);
            var baseSkin = source.BaseSkin!;
            foreach (var skin in source.AllSkins)
            {
                var owner = skin.Owner is { } o ? maps.Layers.GetValueOrDefault(o) : null;
                var copy = new Skin(_p.UniqueSkinName(group, skin.Name), owner);
                if (owner is null) group.SkinList.Insert(WholeSkinCount(group), copy);
                else group.SkinList.Add(copy);
                maps.Skins[skin] = copy;
                foreach (var sp in srcParts)
                    if (skin.Owner is null || ReferenceEquals(skin.Owner, sp))
                        CopyPartCels(src, sp, skin, maps.Layers[sp], copy, _frames, ReferenceEquals(skin, baseSkin) ? null : baseSkin);
            }
        }

        private void BuildFrames()
        {
            foreach (var s in sources)
            {
                int start = _p.Frames.Count;
                var frameMap = new Frame[s.FrameCount];
                for (int i = 0; i < frameMap.Length; i++)
                {
                    var sf = s.Project.Frames[i];
                    frameMap[i] = new Frame
                    {
                        DurationMs = sf.DurationMs,
                        Name = sf.Name ?? (s.FrameCount == 1 ? s.Name : null),
                        Pivot = sf.Pivot is { } pv ? ShiftPivot(pv, s, options) : null,
                    };
                    _p.AddFrameRaw(frameMap[i]);
                }
                if (s.ImageLayer is { } image)
                {
                    var target = _stack.LastOrDefault(l => l.Parent is null && l.Name == image.Name && FramesFit(s, image, l))
                                 ?? _stack.LastOrDefault(l => l is { Kind: LayerKind.Normal, Visible: true, Opacity: 255 } && l.Effects.All(e => e.Frames is not null));
                    if (target is null)
                    {
                        target = CopyProps(image, null, start);
                        if (!s.IsProject) target.Name = "레이어 1";
                        _stack.Add(target);
                    }
                    MapsOf(s).Layers[image] = target;
                    CopyCanvasCels(s, image, target, frameMap);
                }
                else
                {
                    MergeFramesStructure(s, frameMap, start);
                    Note($"{s.FileName}의 {Kept(s)} 유지합니다(이름·설정이 같은 레이어는 이어 붙임).");
                }
                if (s.FrameCount > 1 && s.Project.Tags.Count == 0)
                    AddTag(new AnimationTag(s.Name, start, _p.Frames.Count - 1) { Color = AnimationTag.DefaultColors[_p.Tags.Count % AnimationTag.DefaultColors.Count] });
                foreach (var t in s.Project.Tags)
                {
                    var copy = t.Clone();
                    copy.From += start;
                    copy.To += start;
                    AddTag(copy);
                }
            }
        }

        /// <summary>
        /// 프레임 방식: 프로젝트의 최상위 덩어리(아래 → 위)마다, 앞서 놓은 덩어리보다 위에 있는 같은 레이어(<see cref="FramesFit"/>)에 이어 붙이고,
        /// 없으면 바로 위에 새로 넣는다(파일 안의 쌓임 순서를 지킴).
        /// </summary>
        private void MergeFramesStructure(MergeSource s, Frame[] frameMap, int start)
        {
            int floor = -1;
            var used = new HashSet<Layer>();
            foreach (var top in TopLevel(s))
            {
                int found = -1;
                for (int i = floor + 1; i < _stack.Count && found < 0; i++)
                    if (_stack[i].Parent is null && !used.Contains(_stack[i]) && FramesFit(s, top, _stack[i])) found = i;
                if (found < 0)
                {
                    var block = CopyBlock(s, top, frameMap, start);
                    _stack.InsertRange(floor + 1, block);
                    floor += block.Count;
                    used.Add(block[^1]);
                    continue;
                }
                var target = _stack[found];
                used.Add(target);
                floor = found;
                var maps = MapsOf(s);
                maps.Layers[top] = target;
                if (!top.IsGroup)
                {
                    CopyCanvasCels(s, top, target, frameMap);
                    continue;
                }
                var srcParts = s.Project.ChildrenOf(top);
                var parts = ChildrenInStack(target);
                for (int k = 0; k < srcParts.Count; k++) maps.Layers[srcParts[k]] = parts[k];
                foreach (var skin in top.AllSkins)
                {
                    var owner = skin.Owner is { } o ? maps.Layers.GetValueOrDefault(o) : null;
                    var match = target.SkinList.FirstOrDefault(x => x.Name == skin.Name && ReferenceEquals(x.Owner, owner));
                    if (match is null)
                    {
                        match = new Skin(_p.UniqueSkinName(target, skin.Name), owner);
                        if (owner is null) target.SkinList.Insert(WholeSkinCount(target), match);
                        else target.SkinList.Add(match);
                    }
                    maps.Skins[skin] = match;
                }
                for (int k = 0; k < srcParts.Count; k++)
                    foreach (var skin in top.AllSkins)
                        if (skin.Owner is null || ReferenceEquals(skin.Owner, srcParts[k]))
                            CopyPartCels(s.Project, srcParts[k], skin, parts[k], maps.Skins[skin], frameMap, null);
            }
        }

        /// <summary>
        /// 프레임 방식에서 원본 최상위 레이어를 결과 레이어에 이어 붙여도 되는지: 이름·종류·보임·불투명도가 같고, 효과가 같으며 모두 모든 프레임용(범위 없음).
        /// 그룹이면 자동 배치·첫 스킨 이름과 부위(차례·이름·크기·옮긴 위치·피벗·테두리·보임·불투명도·효과)까지 같아야 한다.
        /// </summary>
        private bool FramesFit(MergeSource s, Layer top, Layer target)
        {
            static bool SameEffects(Layer a, Layer b) => a.Effects.SequenceEqual(b.Effects) && a.Effects.All(e => e.Frames is null);
            if (target.Parent is not null || top.Kind != target.Kind || top.Name != target.Name || top.Visible != target.Visible
                || top.Opacity != target.Opacity || !SameEffects(top, target)) return false;
            if (!top.IsGroup) return true;
            if (top.AutoLayout != target.AutoLayout || top.BaseSkin!.Name != target.BaseSkin!.Name) return false;
            var srcParts = s.Project.ChildrenOf(top);
            var parts = ChildrenInStack(target);
            if (srcParts.Count != parts.Count) return false;
            var shift = Shift(s);
            for (int k = 0; k < parts.Count; k++)
            {
                var (sp, tp) = (srcParts[k], parts[k]);
                if (sp.Name != tp.Name || sp.PartSize != tp.PartSize || new Point(sp.PartOffset.X + shift.X, sp.PartOffset.Y + shift.Y) != tp.PartOffset
                    || sp.PartPivot != tp.PartPivot || sp.PartBorder != tp.PartBorder || sp.Visible != tp.Visible || sp.Opacity != tp.Opacity
                    || !SameEffects(sp, tp))
                    return false;
            }
            return true;
        }

        // ───────────── 복사 ─────────────

        /// <summary>대상 그룹: 그룹이 있는 첫 파일의 맨 위 그룹(<paramref name="preferSkins"/>면 스킨이 여럿인 그룹이 있는 첫 파일의 그런 그룹 먼저).</summary>
        private (int Index, MergeSource Source, Layer Group)? FindTarget(bool preferSkins)
        {
            if (preferSkins)
                for (int i = 0; i < sources.Count; i++)
                    if (TopLevel(sources[i]).Reverse().FirstOrDefault(l => l.IsGroup && l.AllSkins.Count > 1) is { } g)
                        return (i, sources[i], g);
            for (int i = 0; i < sources.Count; i++)
                if (TopLevel(sources[i]).Reverse().FirstOrDefault(l => l.IsGroup) is { } g)
                    return (i, sources[i], g);
            return null;
        }

        private List<Layer> CopyAll(MergeSource s)
        {
            var list = new List<Layer>();
            foreach (var top in TopLevel(s)) list.AddRange(CopyBlock(s, top, _frames, 0));
            return list;
        }

        /// <summary>최상위 레이어(일반·그룹, 아래 → 위).</summary>
        private static IEnumerable<Layer> TopLevel(MergeSource s) => s.Project.Layers.Where(l => l.Parent is null);

        /// <summary>
        /// 최상위 덩어리 하나(일반 레이어, 또는 그룹과 부위·스킨 전부)를 셀까지 복사한다(아래 → 위 목록). 원본 프레임 i의 셀은 <paramref name="frameMap"/>[i]로,
        /// 효과 범위는 <paramref name="frameShift"/>만큼 민다.
        /// </summary>
        private List<Layer> CopyBlock(MergeSource s, Layer top, IReadOnlyList<Frame> frameMap, int frameShift)
        {
            var src = s.Project;
            var maps = MapsOf(s);
            var copy = CopyProps(top, null, frameShift);
            maps.Layers[top] = copy;
            if (!top.IsGroup)
            {
                CopyCanvasCels(s, top, copy, frameMap);
                return [copy];
            }
            var skins = top.AllSkins;
            foreach (var skin in skins) maps.Skins[skin] = new Skin(skin.Name);
            copy.ReplaceSkinsRaw(skins.Select(k => maps.Skins[k]));
            copy.SetActiveSkinRaw(maps.Skins[top.ActiveSkin!]);
            var shift = Shift(s);
            var children = src.ChildrenOf(top);
            var block = new List<Layer>(children.Count + 1);
            foreach (var part in children)
            {
                var c = CopyProps(part, copy, frameShift);
                c.PartOffset = new Point(part.PartOffset.X + shift.X, part.PartOffset.Y + shift.Y);
                maps.Layers[part] = c;
                block.Add(c);
            }
            foreach (var skin in skins)
                if (skin.Owner is { } owner) maps.Skins[skin].Owner = maps.Layers.GetValueOrDefault(owner);
            foreach (var part in children)
            {
                var c = maps.Layers[part];
                if (part.SkinOverride is { } over) c.SetSkinOverrideRaw(maps.Skins[over]);
                foreach (var skin in skins)
                    if (skin.Owner is null || ReferenceEquals(skin.Owner, part))
                        CopyPartCels(src, part, skin, c, maps.Skins[skin], frameMap, null);
            }
            block.Add(copy);
            return block;
        }

        /// <summary>그림 같은 파일의 레이어를 그룹의 부위로(파일 크기, 기준점 위치, 프레임 그대로).</summary>
        private Layer ImagePart(MergeSource s, Layer layer, string name, Layer group)
        {
            var part = CopyProps(layer, group, 0);
            part.Name = name;
            part.Kind = LayerKind.Part;
            part.PartSize = s.Size;
            part.PartOffset = Shift(s);
            MapsOf(s).Layers[layer] = part;
            for (int i = 0; i < s.FrameCount; i++)
                if (s.Project.GetCelRaw(layer, s.Project.Frames[i], null) is { } cel && HasContent(cel))
                    _p.SetCelRaw(part, _frames[i], cel.Clone());
            return part;
        }

        private static Layer CopyProps(Layer src, Layer? parent, int frameShift) => new(src.Name)
        {
            Visible = src.Visible,
            Locked = src.Locked,
            Opacity = src.Opacity,
            Kind = src.Kind,
            Parent = parent,
            Collapsed = src.Collapsed,
            EffectsCollapsed = src.EffectsCollapsed,
            AutoLayout = src.AutoLayout,
            PartSize = src.PartSize,
            PartOffset = src.PartOffset,
            PartPivot = src.PartPivot,
            PartBorder = src.PartBorder,
            PartSourceRect = src.PartSourceRect,
            Effects = frameShift == 0 || src.Effects.All(e => e.Frames is null)
                ? src.Effects
                : src.Effects.Select(e => e.Frames is { } r ? e with { Frames = new FrameRange(r.From + frameShift, r.To + frameShift) } : e).ToArray(),
        };

        /// <summary>일반 레이어 셀(원본 캔버스 크기)을 결과 캔버스에 기준점으로 놓아 복사한다.</summary>
        private void CopyCanvasCels(MergeSource s, Layer src, Layer dst, IReadOnlyList<Frame> frameMap)
        {
            for (int i = 0; i < s.FrameCount; i++)
            {
                if (s.Project.GetCelRaw(src, s.Project.Frames[i], null) is not { } cel || !HasContent(cel)) continue;
                var img = cel.ResizeCanvas(W, H, options.Anchor);
                if (HasContent(img)) _p.SetCelRaw(dst, frameMap[i], img);
            }
        }

        /// <summary>부위 셀(한 스킨 자신의 것)을 복사한다. <paramref name="fillFrom"/>이 있으면 빈 칸은 그 스킨 셀로 채운다(물려받던 그림 굽기).</summary>
        private void CopyPartCels(SpriteProject src, Layer srcPart, Skin srcSkin, Layer dstPart, Skin dstSkin, IReadOnlyList<Frame> frameMap, Skin? fillFrom)
        {
            for (int i = 0; i < src.Frames.Count; i++)
            {
                var cel = src.GetCelRaw(srcPart, src.Frames[i], srcSkin);
                if (!HasContent(cel) && fillFrom is not null) cel = src.GetCelRaw(srcPart, src.Frames[i], fillFrom);
                if (HasContent(cel)) _p.SetCelRaw(dstPart, frameMap[i], cel!.Clone(), dstSkin);
            }
        }

        // ───────────── 마무리 ─────────────

        /// <summary>레이어·그룹·스킨 방식: 프레임 f의 길이 = 그 프레임이 있는 첫 파일의 것, 칸 이름·피벗 = 그것이 있는 첫 파일의 것.</summary>
        private void BuildSharedFrames()
        {
            _frames = new Frame[sources.Max(s => s.FrameCount)];
            for (int f = 0; f < _frames.Length; f++)
            {
                var having = sources.Where(s => f < s.FrameCount).ToList();
                _frames[f] = new Frame
                {
                    DurationMs = having[0].Project.Frames[f].DurationMs,
                    Name = having.Select(s => s.Project.Frames[f].Name).FirstOrDefault(n => n is not null),
                    Pivot = having.Select(s => s.Project.Frames[f].Pivot is { } pv ? ShiftPivot(pv, s, options) : (Pivot?)null).FirstOrDefault(pv => pv is not null),
                };
                _p.AddFrameRaw(_frames[f]);
            }
        }

        /// <summary>
        /// 모은 레이어를 넣는다: 부위는 그룹 바로 아래로 모으고(새 부위만 있던 그룹 없는 줄 정리), 이름은 위 레이어부터 겹치지 않게(최상위 먼저),
        /// 자동 배치 그룹은 결과 캔버스 기준으로 부위 위치를 다시 계산하고, 스킨 목록을 바로잡는다.
        /// </summary>
        private void FinishLayers()
        {
            var ordered = new List<Layer>(_stack.Count);
            foreach (var l in _stack)
            {
                if (l.Parent is not null) continue;
                if (l.IsGroup) ordered.AddRange(_stack.Where(x => ReferenceEquals(x.Parent, l)));
                ordered.Add(l);
            }
            var used = new HashSet<string>();
            string Unique(string name)
            {
                if (used.Add(name)) return name;
                for (int i = 2; ; i++)
                    if (used.Add($"{name} {i}")) return $"{name} {i}";
            }
            for (int i = ordered.Count - 1; i >= 0; i--)
                if (ordered[i].Parent is null) ordered[i].Name = Unique(ordered[i].Name);
            for (int i = ordered.Count - 1; i >= 0; i--)
                if (ordered[i].Parent is not null) ordered[i].Name = Unique(ordered[i].Name);
            foreach (var l in ordered) _p.AddLayerRaw(l);
            foreach (var g in ordered.Where(l => l is { IsGroup: true, AutoLayout: not null }))
                foreach (var (part, offset) in _p.ComputeGroupLayout(g, g.AutoLayout!))
                    part.PartOffset = offset;
            _p.NormalizeSkins();
        }

        /// <summary>프로젝트 파일의 착장을 복사한 그룹·부위·스킨을 가리키도록 옮긴다(옮길 수 없는 항목은 뺀다, 같은 착장은 한 번만).</summary>
        private void CopyOutfits()
        {
            foreach (var s in sources.Where(s => s.IsProject))
            {
                var maps = MapsOf(s);
                foreach (var o in s.Project.Outfits)
                {
                    var slots = o.Slots
                        .Where(e => maps.Layers.ContainsKey(e.Group) && maps.Skins.ContainsKey(e.Skin))
                        .Select(e => new OutfitSlot(maps.Layers[e.Group], maps.Skins[e.Skin])).ToArray();
                    var parts = o.Parts
                        .Where(e => maps.Layers.ContainsKey(e.Part) && maps.Skins.ContainsKey(e.Skin))
                        .Select(e => new OutfitPart(maps.Layers[e.Part], maps.Skins[e.Skin])).ToArray();
                    if (slots.Length == 0 && parts.Length == 0) continue;
                    if (_p.Outfits.Any(x => x.Name == o.Name && x.Slots.SequenceEqual(slots) && x.Parts.SequenceEqual(parts))) continue;
                    _p.AddOutfitRaw(new Outfit(_p.UniqueOutfitName(o.Name), slots, parts));
                }
            }
        }

        /// <summary>태그를 넣는다. 같은 이름·같은 범위가 이미 있으면 건너뛰고, 이름만 겹치면 이름을 바꾼다.</summary>
        private void AddTag(AnimationTag tag)
        {
            if (tag.From < 0 || tag.To >= _p.Frames.Count || tag.From > tag.To) return;
            if (_p.FindTag(tag.Name) is { } same)
            {
                if (same.From == tag.From && same.To == tag.To) return;
                var renamed = _p.UniqueTagName(tag.Name);
                Note($"태그 \"{tag.Name}\"이(가) 겹쳐 \"{renamed}\"(으)로 바꿉니다.");
                tag.Name = renamed;
            }
            _p.AddTagRaw(tag);
        }

        // ───────────── 작은 도움 ─────────────

        private Maps MapsOf(MergeSource s)
        {
            if (!_maps.TryGetValue(s, out var m)) _maps[s] = m = new Maps();
            return m;
        }

        /// <summary>원본 캔버스 왼쪽 위가 결과 캔버스에 놓이는 자리.</summary>
        private Point Shift(MergeSource s)
        {
            var (x, y) = PixelImage.AnchorOffset(s.Size.Width, s.Size.Height, W, H, options.Anchor);
            return new Point(x, y);
        }

        /// <summary>원본 캔버스 기준 피벗 → 결과 캔버스에서 같은 그림 지점.</summary>
        public static Pivot ShiftPivot(Pivot pivot, MergeSource s, MergeOptions options)
        {
            var (ox, oy) = PixelImage.AnchorOffset(s.Size.Width, s.Size.Height, options.Width, options.Height, options.Anchor);
            var (px, py) = pivot.ToLocalPixel(s.Size.Width, s.Size.Height);
            return new Pivot((px + ox) / options.Width, 1f - (py + oy) / options.Height);
        }

        private List<Layer> ChildrenInStack(Layer group) => _stack.Where(l => ReferenceEquals(l.Parent, group)).ToList();

        private static int WholeSkinCount(Layer group) => group.SkinList.TakeWhile(k => k.Owner is null).Count();

        private static bool HasContent(PixelImage? image) => image is not null && !image.IsRegionEmpty(image.Bounds);

        private string NewGroupName() => string.IsNullOrWhiteSpace(options.Name) ? "그룹" : options.Name.Trim();

        /// <summary>지키는 구조 설명(목적격 조사 포함): "그룹·스킨을" / "그룹을" / "레이어 구조를".</summary>
        private static string Kept(MergeSource s)
        {
            var groups = s.Project.Layers.Where(l => l.IsGroup).ToList();
            if (groups.Count == 0) return "레이어 구조를";
            return groups.Any(g => g.AllSkins.Count > 1) ? "그룹·스킨을" : "그룹을";
        }

        private void Note(string text)
        {
            if (notes is not null && !notes.Contains(text)) notes.Add(text);
        }
    }
}
