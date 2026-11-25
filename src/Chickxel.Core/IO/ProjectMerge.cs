using System.Drawing;
using Chickxel.Core.Localization;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 여러 파일을 프로젝트 하나로 합치는 방식. 어느 방식이든 <c>.chickxel</c>의 구조(레이어 나무·자리 있는 레이어·레이어마다 스킨·스킨 프리셋·효과·자동 배치·
/// 태그·프레임 길이·이름·피벗)는 버리지 않고 옮긴다 — 자세한 규칙은 <see cref="ProjectMerge"/>.
/// </summary>
public enum MergeMode
{
    /// <summary>그림 파일마다 일반 레이어 1개(캔버스 크기, 기준점에 배치), 프로젝트는 레이어 나무를 통째로. 첫 파일이 맨 위.</summary>
    Layers,
    /// <summary>
    /// 빈 부모 레이어 1개(결과 캔버스 크기, (0,0))에 그림 파일마다 자식 레이어 1개(파일 크기 그대로, 기준점 위치).
    /// 프로젝트에 자식 있는 레이어가 있으면 그 레이어에 자식을 더한다.
    /// </summary>
    Parent,
    /// <summary>파일마다 프레임(파일의 프레임을 차례로 이어 붙임). 프로젝트는 레이어 나무를 그대로 가져온다.</summary>
    Frames,
    /// <summary>레이어 1개, 그림 파일마다 그 레이어의 스킨 1개. 프로젝트에 스킨이 여럿인 레이어가 있으면 그 레이어에 스킨을 더한다.</summary>
    Skins,
}

/// <summary>
/// 합칠 파일 하나. 모든 파일을 프로젝트로 다룬다: PNG는 레이어 1개(이름 = 파일 이름)짜리 프로젝트,
/// <c>.chickxel</c>은 그 프로젝트 그대로(읽기만 — 합치기는 늘 복사본을 만들므로 열려 있는 문서를 넘겨도 된다).
/// </summary>
public sealed class MergeSource
{
    /// <summary>그림 파일: 프레임마다 그림 한 장(모두 같은 크기)과 길이.</summary>
    public MergeSource(string name, IReadOnlyList<PixelImage> frames, IReadOnlyList<int> durations)
    {
        if (frames.Count == 0) throw new ArgumentException(DomainStrings.Merge_NoFrames, nameof(frames));
        if (durations.Count != frames.Count) throw new ArgumentException(DomainStrings.Merge_DurationCountMismatch, nameof(durations));
        var size = frames[0].Size;
        if (frames.Any(f => f.Size != size)) throw new ArgumentException(DomainStrings.Merge_FrameSizeMismatch, nameof(frames));
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

    /// <summary>프로젝트(<c>.chickxel</c> 또는 열려 있는 문서). 바꾸지 않고 읽기만 한다.</summary>
    public MergeSource(string name, SpriteProject project)
    {
        if (project.Frames.Count == 0) throw new ArgumentException(DomainStrings.Merge_NoFrames, nameof(project));
        Name = name;
        FileName = name + SpriteProject.FileExtension;
        Project = project;
        IsProject = true;
    }

    /// <summary>확장자 뺀 파일 이름(레이어·스킨·태그 이름에 쓴다).</summary>
    public string Name { get; }
    /// <summary>화면에 보일 파일 이름(확장자 포함).</summary>
    public string FileName { get; init; }
    /// <summary>내용(그림 파일이면 레이어 1개짜리 프로젝트). 읽기만 한다.</summary>
    public SpriteProject Project { get; }
    /// <summary>프로젝트 파일(또는 열린 문서)인지 — Unity 설정을 가져오고 스킨 프리셋을 옮긴다. 그림 파일이면 false.</summary>
    public bool IsProject { get; }
    /// <summary>캔버스 크기.</summary>
    public Size Size => Project.Size;
    public int FrameCount => Project.Frames.Count;

    /// <summary>
    /// 그림 한 장처럼 다룰 수 있으면 그 유일한 레이어(일반 레이어 1개, 스킨 1개, 효과 없음, 보임, 불투명) — 그림 파일은 늘 그렇다.
    /// null이면 지킬 구조가 있는 프로젝트다(<see cref="HasStructure"/>).
    /// </summary>
    internal Layer? ImageLayer
        => Project.Layers is [{ Kind: LayerKind.Normal, Visible: true, Opacity: 255 } l] && l.Effects.Count == 0 && l.Skins.Count == 1 ? l : null;

    /// <summary>그림 한 장으로 볼 수 없는 구조(여러 레이어·자리 있는 레이어·스킨·효과·숨김·불투명도)가 있는 프로젝트인지 — 합칠 때 그 구조를 그대로 옮긴다.</summary>
    public bool HasStructure => ImageLayer is null;

    /// <summary>레이어 나무(자식 있는 레이어)가 있는 프로젝트인지.</summary>
    public bool HasTree => Project.Layers.Any(l => l.Parent is not null);

    /// <summary>
    /// PNG 또는 <c>.chickxel</c>을 읽는다. <paramref name="openDocument"/>(그 파일로 열려 있는 문서 — 저장하지 않은 편집 포함)가 있으면
    /// 파일 대신 그것을 쓴다(바꾸지 않음).
    /// </summary>
    public static MergeSource Load(string path, SpriteProject? openDocument = null)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var fileName = Path.GetFileName(path);
        if (openDocument is not null) return new MergeSource(name, openDocument) { FileName = fileName };
        if (!SpriteProject.IsProjectFile(path))
            return new MergeSource(name, [PngIO.Load(path)], [Frame.DefaultDurationMs]) { FileName = fileName };
        return new MergeSource(name, ProjectIO.Load(path)) { FileName = fileName };
    }
}

/// <summary>합치기 설정.</summary>
/// <param name="Mode">합치는 방식.</param>
/// <param name="Width">결과 캔버스 가로.</param>
/// <param name="Height">결과 캔버스 세로.</param>
/// <param name="Anchor">파일 그림(프로젝트면 그 캔버스)을 결과 캔버스에 놓을 기준점. 자리 있는 레이어의 위치·피벗도 같은 만큼 옮긴다.</param>
/// <param name="Name">결과 이름(부모·스킨 방식에서 새로 만드는 레이어의 이름 — 프로젝트의 레이어에 더하면 그 레이어 이름 그대로).</param>
/// <param name="BlankCover">부모 방식에서 빈 대표 이미지: 그 부모의 자식 레이어를 모두(원래 있던 자식 포함) 숨겨 부모(타임라인 접은 줄·프레임 시트)가 비어 보이게 한다.
/// 자식 그림은 그대로라 레이어 아틀라스에는 모두 나가고, 눈을 켜면 보인다. 다른 방식에서는 쓰지 않는다.</param>
public sealed record MergeOptions(MergeMode Mode, int Width, int Height, Anchor Anchor, string Name, bool BlankCover = false);

/// <summary>
/// 파일 여러 개 → 새 프로젝트(기록 없음, 수정됨 아님). 원본 프로젝트는 바꾸지 않는다.
/// <para>
/// "그림 같은" 파일 = PNG, 또는 일반 레이어 하나뿐(스킨 1개, 효과·숨김·불투명도 없음)인 <c>.chickxel</c>(<see cref="MergeSource.ImageLayer"/>).
/// 그 밖의 프로젝트는 구조를 그대로 복사한다: 레이어 나무·자리 있는 레이어(크기·위치·피벗·테두리)·레이어마다 스킨과 지금 스킨·효과·자동 배치·
/// 스킨 프리셋(복사한 레이어·스킨을 가리키도록 바꿈). 캔버스가 바뀌면 일반 레이어 셀은 기준점으로 옮기고, 자리 있는 레이어 위치·프레임 피벗·기본 피벗은 같은 만큼 옮겨 그림 자리를 지킨다.
/// </para>
/// <list type="bullet">
/// <item>레이어: 첫 파일이 맨 위. 그림 같은 파일은 레이어 1개(이름 = 파일 이름), 프로젝트는 레이어 나무를 통째로.</item>
/// <item>부모: 레이어 나무가 있는 첫 프로젝트의 맨 위 "자식 있는 최상위 레이어"에 그림 같은 파일마다 자식 레이어 1개(파일 크기, 기준점 위치)를 더한다 —
/// 목록에서 그 프로젝트보다 앞 파일은 원래 자식 위, 뒤 파일은 아래. 나무 없는 여러 레이어 프로젝트는 레이어마다 자식 1개(타일맵은 옆에 그대로).
/// 나무가 있는 다른 프로젝트는 옆에 그대로. 나무 있는 프로젝트가 없으면 새 빈 부모 레이어(<see cref="MergeOptions.Name"/>, 결과 캔버스 크기, (0,0),
/// 첫 파일이 맨 위 자식). <see cref="MergeOptions.BlankCover"/>면 그 부모의 자식을 모두 숨긴다.</item>
/// <item>스킨: 스킨이 여럿인 레이어가 있는 첫 프로젝트(없으면 나무가 있는 첫 프로젝트의 맨 위 나무의 잎 레이어들)에서, 그림 같은 파일마다
/// 크기가 같은 레이어(없으면 가장 큰 레이어)에 새 스킨(이름 = 파일 이름)을 더한다. 그림이 자리 있는 레이어보다 크면 그 레이어를 기준점으로 키운다(원래 그림 자리 그대로).
/// 그런 프로젝트가 없으면 새 레이어 1개(<see cref="MergeOptions.Name"/>)에 그림 파일마다 스킨 1개(첫 파일 = 첫 스킨·지금 스킨): 그림이 모두 결과 캔버스 크기면 일반 레이어,
/// 아니면 가장 큰 그림 크기의 자리 있는 레이어(기준점 위치, 최상위).
/// 다른 프로젝트의 최상위 덩어리는 대상 프로젝트에 같은 덩어리가 있으면 레이어마다 스킨을 더하고, 없으면 옆에 그대로. 같은 덩어리 = 이름이 같은 최상위 레이어 중
/// 자손까지 레이어 수가 같고 원본의 레이어마다 나무 경로(최상위부터 이름을 이은 것)가 같은 레이어가 하나씩 있으며, 그 짝이 모두 종류·불투명도·효과·자동 배치가 같고
/// 자리 있는 레이어면 크기·(옮긴) 위치·피벗·테두리까지 같은 것. 짝마다 원본 스킨을 모두 새 스킨(레이어 안에서 겹치지 않는 이름)으로 더하되, 첫 스킨은 그림이
/// 대상 레이어의 첫 스킨과 모든 프레임에서 같으면 더하지 않는다(빈 부모 레이어·같은 기본 그림이 늘지 않게).</item>
/// <item>프레임: 파일 순서대로 프레임을 이어 붙인다(태그·효과 범위도 그만큼 민다). 프로젝트의 최상위 덩어리는 이미 있는 덩어리와 나무 모양·이름·종류·설정이 같으면
/// 그 덩어리에 이어 붙이고(첫 스킨끼리, 나머지 스킨은 이름으로 합침), 아니면 새 레이어로 넣는다. 그림 같은 파일은 같은 이름의 레이어, 없으면 맨 위의 그릴 수 있는 일반 레이어,
/// 그것도 없으면 새 레이어. 프레임이 하나인 파일은 칸 이름이 없으면 파일 이름, 여러 개인 파일은 태그가 없으면 파일 이름으로 태그 하나.</item>
/// </list>
/// 레이어·부모·스킨 방식은 프레임 수 = 가장 많은 파일의 프레임 수, 프레임 길이는 그 프레임이 있는 첫 파일의 것, 칸 이름·피벗은 그것이 있는 첫 파일의 것.
/// 태그는 모두 옮기고 이름이 겹치면(같은 범위면 하나로) 이름을 바꾼다. 레이어 이름은 겹치지 않게(얕은 레이어부터, 같은 깊이는 위 레이어부터 원래 이름) 바꾼다.
/// Unity 설정은 첫 프로젝트 파일의 것(없으면 <c>spriteDefaults</c>), 내보내기 설정은 새 프로젝트 기본값.
/// </summary>
public static class ProjectMerge
{
    /// <param name="notes">있으면 구조를 어떻게 옮겼는지(유지한 나무·스킨, 새 스킨·자식 자리, 따로 넣은 레이어, 바뀐 태그 이름) 설명을 더한다.</param>
    public static SpriteProject Merge(IReadOnlyList<MergeSource> sources, MergeOptions options, SpriteSettings? spriteDefaults = null,
        List<string>? notes = null)
    {
        if (sources.Count == 0) throw new ArgumentException(DomainStrings.Merge_NoSources, nameof(sources));
        var first = sources.FirstOrDefault(s => s.IsProject);
        // 결과 캔버스 기본 피벗: 첫 프로젝트 파일의 것(결과 캔버스로 옮김), 없으면 기본 설정. 새로 만드는 캔버스 크기 레이어의 피벗도 이것.
        var defaultPivot = first is not null ? Builder.ShiftPivot(first.Project.Sprite.DefaultPivot, first, options) : spriteDefaults?.DefaultPivot ?? Pivot.Center;
        var p = new Builder(sources, options, notes, defaultPivot).Build();
        if (first is not null)
        {
            p.Sprite.CopyFrom(first.Project.Sprite);
            p.Sprite.DefaultPivot = defaultPivot;
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

    /// <summary>
    /// 합치기 한 번의 작업 상태. 레이어는 <see cref="_stack"/>에 덩어리(자손 + 자신, 목록 순서)째 모았다가(덩어리끼리는 아래 → 위)
    /// 마지막에 나무 순서·이름을 맞춰 넣는다.
    /// </summary>
    /// <param name="canvasPivot">새로 만드는 캔버스 크기 레이어(빈 부모·일반 레이어)의 피벗(결과 캔버스 기본 피벗).</param>
    private sealed class Builder(IReadOnlyList<MergeSource> sources, MergeOptions options, List<string>? notes, Pivot canvasPivot)
    {
        private readonly SpriteProject _p = SpriteProject.CreateBlank(options.Width, options.Height);
        private readonly List<Layer> _stack = new();
        private readonly Dictionary<MergeSource, Maps> _maps = new();
        /// <summary>레이어·부모·스킨 방식: 결과 프레임(모든 파일의 프레임 번호가 같음).</summary>
        private Frame[] _frames = [];

        /// <summary>원본 레이어·스킨 → 결과 레이어·스킨(스킨 프리셋 옮기기용).</summary>
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
                    case MergeMode.Parent: BuildParent(); break;
                    case MergeMode.Skins: BuildSkins(); break;
                    default: BuildLayers(); break;
                }
                foreach (var s in sources)
                    foreach (var t in s.Project.Tags) AddTag(t.Clone());
            }
            FinishLayers();
            CopySkinPresets();
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
                    var layer = CopyLayer(s, l, null, _frames, 0);
                    layer.Name = s.Name;
                    list.Add(layer);
                }
                else
                {
                    list.AddRange(CopyAll(s));
                    Note(DomainStrings.FormatMerge_Keep(name: s.FileName, kept: Kept(s)));
                }
                perSource.Add(list);
            }
            for (int i = perSource.Count - 1; i >= 0; i--) _stack.AddRange(perSource[i]);
        }

        private void BuildParent()
        {
            var target = FindParentTarget();
            var perSource = new List<Layer>[sources.Count];
            var newChildren = new List<Layer>?[sources.Count]; // 파일마다 새 자식(아래 → 위)
            Layer parent;
            if (target is { } t)
            {
                perSource[t.Index] = CopyAll(t.Source);
                parent = MapsOf(t.Source).Layers[t.Root];
                Note(DomainStrings.FormatMerge_KeepAsChildren(name: t.Source.FileName, kept: Kept(t.Source), parent: t.Root.Name));
            }
            else parent = new Layer(NewLayerName()) { Kind = LayerKind.Part, PartSize = new Size(W, H), Pivot = canvasPivot };
            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (target is { } tt && i == tt.Index) continue;
                perSource[i] = [];
                if (s.HasTree)
                {
                    perSource[i] = CopyAll(s);
                    Note(DomainStrings.FormatMerge_KeepAside(name: s.FileName, kept: Kept(s)));
                    continue;
                }
                // 나무 없는 프로젝트: 최상위 레이어마다 자식 1개(일반 레이어 → 파일 크기 자리 있는 레이어, 자리 있는 레이어 → 그대로). 타일맵은 자식이 될 수 없어 옆에.
                var layers = TopLevel(s).Where(l => !l.IsTilemap).ToList();
                var kids = new List<Layer>(layers.Count);
                foreach (var l in layers)
                {
                    var name = layers.Count == 1 ? s.Name : $"{s.Name} {l.Name}";
                    if (l.IsPart)
                    {
                        var copy = CopyLayer(s, l, parent, _frames, 0);
                        copy.Name = name;
                        kids.Add(copy);
                    }
                    else kids.Add(ImagePart(s, l, name, parent));
                }
                newChildren[i] = kids;
                foreach (var l in TopLevel(s).Where(l => l.IsTilemap)) perSource[i].Add(CopyLayer(s, l, null, _frames, 0));
                if (layers.Count > 1) Note(DomainStrings.FormatMerge_LayersToChildren(name: s.FileName, count: layers.Count));
            }

            // 목록 순서 = 위 → 아래: 대상 파일보다 앞 파일의 자식은 원래 자식 위, 뒤 파일은 아래.
            int split = target?.Index ?? sources.Count;
            var below = new List<Layer>();
            var above = new List<Layer>();
            for (int i = sources.Count - 1; i >= 0; i--)
                if (newChildren[i] is { } kids) (i > split ? below : above).AddRange(kids);
            if (target is { } tg)
            {
                var list = perSource[tg.Index];
                int at = list.IndexOf(parent);
                int start = at - list.Count(l => IsUnder(l, parent));
                list.InsertRange(at, above);
                list.InsertRange(start, below);
            }
            else perSource[0] = [.. perSource[0], .. above, parent];
            if (options.BlankCover)
            {
                foreach (var child in perSource.SelectMany(l => l).Where(l => ReferenceEquals(l.Parent, parent))) child.Visible = false;
                if (target is not null) Note(DomainStrings.FormatMerge_BlankCover(parent: parent.Name));
            }
            for (int i = sources.Count - 1; i >= 0; i--) _stack.AddRange(perSource[i]);
        }

        private void BuildSkins()
        {
            var target = FindSkinTarget();
            var perSource = new List<Layer>[sources.Count];
            List<Layer> candidates = []; // 그림 파일 스킨을 받을 결과 레이어(아래 → 위)
            Skin? firstSkin = null; // 새 레이어면 첫 그림 파일의 스킨(= 첫 스킨·지금 스킨)

            if (target is { } t)
            {
                perSource[t.Index] = CopyAll(t.Source);
                var maps = MapsOf(t.Source);
                candidates = t.Candidates.Select(l => maps.Layers[l]).ToList();
                Note(DomainStrings.FormatMerge_KeepAsSkins(name: t.Source.FileName, kept: Kept(t.Source)));
            }
            else if (FirstImageIndex() is { } firstImage)
            {
                // 스킨 받을 프로젝트가 없으면: 새 레이어 1개에 그림 파일마다 스킨 1개. 모두 결과 캔버스 크기면 일반 레이어,
                // 아니면 가장 큰 그림 크기의 자리 있는 레이어(기준점 위치).
                var images = sources.Where(s => s.ImageLayer is not null).ToList();
                var layer = new Layer(NewLayerName()) { Pivot = canvasPivot };
                if (images.Any(s => s.Size != new Size(W, H)))
                {
                    var size = new Size(images.Max(s => s.Size.Width), images.Max(s => s.Size.Height));
                    var (x, y) = PixelImage.AnchorOffset(size.Width, size.Height, W, H, options.Anchor);
                    layer.Kind = LayerKind.Part;
                    layer.PartSize = size;
                    layer.PartOffset = new Point(x, y);
                    layer.Pivot = Pivot.Center;
                }
                layer.ReplaceSkinsRaw([firstSkin = new Skin(sources[firstImage].Name)]);
                candidates = [layer];
                perSource[firstImage] = [layer];
            }

            for (int i = 0; i < sources.Count; i++)
            {
                var s = sources[i];
                if (target is { } tt && i == tt.Index) continue;
                perSource[i] ??= [];
                if (s.ImageLayer is { } l && candidates.Count > 0)
                {
                    AddImageSkin(s, l, candidates, target is not null, firstSkin);
                    firstSkin = null;
                    continue;
                }
                bool aside = false;
                foreach (var top in TopLevel(s))
                {
                    if (target is { } tg && MatchBlock(s, top, perSource[tg.Index]) is { } pairs)
                    {
                        int count = AppendSkins(s, pairs);
                        Note(DomainStrings.FormatMerge_AppendSkins(name: s.FileName, source: top.Name, count: count, target: tg.Source.FileName));
                        continue;
                    }
                    perSource[i].AddRange(CopyBlock(s, top, _frames, 0));
                    aside = true;
                    if (target is { } td && perSource[td.Index].Any(x => x.Parent is null && x.Name == top.Name))
                        Note(DomainStrings.FormatMerge_LayerAside(name: s.FileName, source: top.Name, target: td.Source.FileName));
                }
                if (aside) Note(DomainStrings.FormatMerge_KeepAside(name: s.FileName, kept: Kept(s)));
            }
            for (int i = sources.Count - 1; i >= 0; i--) _stack.AddRange(perSource[i]);
        }

        /// <summary>
        /// 그림 같은 파일을 레이어의 새 스킨으로: 후보 중 크기가 같은 레이어(자리 있는 레이어는 그 크기, 일반 레이어는 결과 캔버스 크기 — 같으면 위 레이어),
        /// 없으면 가장 큰 레이어에. 자리 있는 레이어보다 크면 그 레이어를 키운다. <paramref name="skin"/>이 있으면 새로 만들지 않고 그 스킨(새 레이어의 첫 스킨)에 넣는다.
        /// </summary>
        private void AddImageSkin(MergeSource s, Layer image, List<Layer> candidates, bool noteIt, Skin? skin)
        {
            var size = s.Size;
            Size SizeOf(Layer l) => l.IsPart ? l.PartSize : new Size(W, H);
            var layer = candidates.LastOrDefault(l => SizeOf(l) == size)
                        ?? candidates.OrderByDescending(l => SizeOf(l).Width * SizeOf(l).Height).ThenByDescending(l => candidates.IndexOf(l)).First();
            string grown = "";
            if (layer.IsPart && (size.Width > layer.PartSize.Width || size.Height > layer.PartSize.Height))
            {
                GrowPart(layer, Math.Max(size.Width, layer.PartSize.Width), Math.Max(size.Height, layer.PartSize.Height));
                grown = DomainStrings.FormatMerge_GrewLayer(width: layer.PartSize.Width, height: layer.PartSize.Height);
            }
            if (skin is null)
            {
                skin = new Skin(_p.UniqueSkinName(layer, s.Name));
                layer.SkinList.Add(skin);
            }
            var cell = SizeOf(layer);
            for (int i = 0; i < s.FrameCount; i++)
            {
                if (s.Project.GetCelRaw(image, s.Project.Frames[i], null) is not { } cel || !HasContent(cel)) continue;
                var img = cel.ResizeCanvas(cell.Width, cell.Height, options.Anchor);
                if (HasContent(img)) _p.SetCelRaw(layer, _frames[i], img, skin);
            }
            if (noteIt) Note(DomainStrings.FormatMerge_NewSkin(name: s.FileName, layer: layer.Name, skin: skin.Name, grown: grown));
        }

        /// <summary>
        /// 자리 있는 레이어를 기준점으로 키운다: 모든 스킨 셀을 옮기고, 그림이 캔버스에서 같은 자리에 남도록 위치를, 같은 지점을 가리키도록 피벗을 맞춘다.
        /// 자식은 옮기지 않는다(자리가 캔버스 기준이라 그대로).
        /// </summary>
        private void GrowPart(Layer part, int width, int height)
        {
            var old = part.PartSize;
            var (ox, oy) = PixelImage.AnchorOffset(old.Width, old.Height, width, height, options.Anchor);
            foreach (var frame in _frames)
                foreach (var skin in part.SkinList)
                    if (_p.GetCelRaw(part, frame, skin) is { } cel)
                        _p.SetCelRaw(part, frame, cel.ResizeCanvas(width, height, options.Anchor), skin);
            Pivot Moved(Pivot pivot)
            {
                var (px, py) = pivot.ToLocalPixel(old.Width, old.Height);
                return new Pivot((px + ox) / width, 1f - (py + oy) / height);
            }
            part.Pivot = Moved(part.Pivot);
            foreach (var frame in _frames)
                if (frame.LayerPivots.TryGetValue(part, out var own)) frame.LayerPivots[part] = Moved(own);
            part.PartOffset = new Point(part.PartOffset.X - ox, part.PartOffset.Y - oy);
            part.PartSize = new Size(width, height);
        }

        /// <summary>
        /// 스킨 방식에서 다른 프로젝트의 최상위 덩어리(<paramref name="top"/>과 자손)를 대상 프로젝트(<paramref name="targetLayers"/> = 그 복사본)의 같은 덩어리에 맞춘다.
        /// 같은 덩어리 = 이름이 같은 최상위 레이어 중, 자손까지 레이어 수가 같고 원본의 레이어마다 "나무 경로"(최상위부터 이름을 이은 것)가 같은 레이어가
        /// 하나씩(경로가 겹치면 맞추지 않음) 있으며, 그 짝이 모두 종류·불투명도·효과·자동 배치가 같고 자리 있는 레이어면 크기·(옮긴) 위치·피벗·테두리까지 같은 것.
        /// 맞으면 (원본, 결과) 짝들, 아니면 null(→ 옆에 그대로 넣음). 타일맵은 맞추지 않는다.
        /// </summary>
        private List<(Layer Source, Layer Target)>? MatchBlock(MergeSource s, Layer top, List<Layer> targetLayers)
        {
            if (top.IsTilemap) return null;
            var srcBlock = BlockOf(s.Project, top);
            foreach (var root in targetLayers.Where(l => l.Parent is null && l.Name == top.Name))
            {
                var dstBlock = targetLayers.Where(l => ReferenceEquals(l, root) || IsUnder(l, root)).ToList();
                if (dstBlock.Count != srcBlock.Count) continue;
                var byPath = new Dictionary<string, Layer>();
                if (!dstBlock.All(l => byPath.TryAdd(PathOf(l), l))) continue;
                var used = new HashSet<Layer>();
                var pairs = new List<(Layer, Layer)>(srcBlock.Count);
                foreach (var a in srcBlock)
                {
                    if (!byPath.TryGetValue(PathOf(a), out var b) || !used.Add(b) || !SkinsFit(s, a, b)) break;
                    pairs.Add((a, b));
                }
                if (pairs.Count == srcBlock.Count) return pairs;
            }
            return null;
        }

        /// <summary>나무 경로: 최상위부터 이 레이어까지 이름을 이은 것.</summary>
        private static string PathOf(Layer layer) => layer.Parent is { } p ? PathOf(p) + "\n" + layer.Name : layer.Name;

        /// <summary>다른 프로젝트 레이어의 스킨을 대상 레이어에 그대로 더할 수 있는지: 종류·불투명도·효과·자동 배치, 자리 있는 레이어면 크기·(옮긴) 위치·피벗·테두리가 같다.</summary>
        private bool SkinsFit(MergeSource s, Layer source, Layer target)
        {
            if (source.Kind != target.Kind || source.Opacity != target.Opacity || source.AutoLayout != target.AutoLayout
                || !source.Effects.SequenceEqual(target.Effects)) return false;
            if (!source.IsPart) return true;
            var shift = Shift(s);
            return source.PartSize == target.PartSize && new Point(source.PartOffset.X + shift.X, source.PartOffset.Y + shift.Y) == target.PartOffset
                   && source.Pivot == target.Pivot && source.PartBorder == target.PartBorder;
        }

        /// <summary>
        /// 맞춘 덩어리(<see cref="MatchBlock"/>)의 레이어마다 원본 스킨을 대상 레이어의 새 스킨(목록 끝, 레이어 안에서 겹치지 않는 이름)으로 더한다(스킨마다 자신의 셀).
        /// 첫 스킨은 그림이 대상 레이어의 첫 스킨과 모든 프레임에서 같으면 더하지 않고 그 첫 스킨으로 친다(빈 부모 레이어·같은 기본 그림이 "기본 2"로 늘지 않게).
        /// 더한 스킨 수를 돌려준다.
        /// </summary>
        private int AppendSkins(MergeSource s, List<(Layer Source, Layer Target)> pairs)
        {
            var maps = MapsOf(s);
            int count = 0;
            foreach (var (src, dst) in pairs)
            {
                maps.Layers[src] = dst;
                var skins = src.Skins;
                for (int k = 0; k < skins.Count; k++)
                {
                    var skin = skins[k];
                    if (k == 0 && SameBaseCels(s, src, dst))
                    {
                        maps.Skins[skin] = dst.SkinList[0];
                        continue;
                    }
                    var copy = new Skin(_p.UniqueSkinName(dst, skin.Name));
                    dst.SkinList.Add(copy);
                    maps.Skins[skin] = copy;
                    CopySkinCels(s, src, skin, dst, copy, _frames);
                    count++;
                }
                CopyFramePivots(s, src, dst, _frames);
            }
            return count;
        }

        /// <summary>원본 레이어 첫 스킨의 그림(결과 캔버스에 놓은 것)이 결과 레이어 첫 스킨의 그림과 모든 결과 프레임에서 같은지(둘 다 비면 같음).</summary>
        private bool SameBaseCels(MergeSource s, Layer src, Layer dst)
        {
            for (int i = 0; i < _frames.Length; i++)
            {
                var mine = i < s.FrameCount && s.Project.GetCelRaw(src, s.Project.Frames[i], null) is { } cel && HasContent(cel) ? Placed(cel, dst) : null;
                var theirs = _p.GetCelRaw(dst, _frames[i], null);
                if (HasContent(mine) != HasContent(theirs)) return false;
                if (HasContent(mine) && !mine!.ContentEquals(theirs!)) return false;
            }
            return true;
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
                        target = CopyLayer(s, image, null, frameMap, start);
                        if (!s.IsProject) target.Name = DomainStrings.FormatProject_DefaultLayerName(number: 1);
                        _stack.Add(target);
                    }
                    else JoinLayer(s, image, target, frameMap);
                }
                else
                {
                    MergeFramesStructure(s, frameMap, start);
                    Note(DomainStrings.FormatMerge_KeepFrameStructure(name: s.FileName, kept: Kept(s)));
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
        /// 프레임 방식: 프로젝트의 최상위 덩어리(아래 → 위)마다, 앞서 놓은 덩어리보다 위에 있는 같은 덩어리(<see cref="FramesFit"/>)에 이어 붙이고,
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
                var srcBlock = BlockOf(s.Project, top);
                var dstBlock = BlockInStack(target);
                for (int k = 0; k < srcBlock.Count; k++) JoinLayer(s, srcBlock[k], dstBlock[k], frameMap);
            }
        }

        /// <summary>
        /// 프레임 방식에서 원본 최상위 덩어리를 결과 덩어리에 이어 붙여도 되는지: 덩어리의 레이어(목록 순서)마다 부모 자리·이름·종류·보임·불투명도·자동 배치가 같고,
        /// 효과가 같으며 모두 모든 프레임용(범위 없음), 자리 있는 레이어면 크기·옮긴 위치·피벗·테두리까지 같다. 타일맵은 잇지 않는다.
        /// </summary>
        private bool FramesFit(MergeSource s, Layer top, Layer target)
        {
            static bool SameEffects(Layer a, Layer b) => a.Effects.SequenceEqual(b.Effects) && a.Effects.All(e => e.Frames is null);
            static int ParentAt(List<Layer> block, Layer l) => l.Parent is { } p ? block.IndexOf(p) : -1;
            if (target.Parent is not null || top.IsTilemap) return false;
            var src = BlockOf(s.Project, top);
            var dst = BlockInStack(target);
            if (src.Count != dst.Count) return false;
            var shift = Shift(s);
            for (int k = 0; k < src.Count; k++)
            {
                var (a, b) = (src[k], dst[k]);
                if (a.Kind != b.Kind || a.Name != b.Name || a.Visible != b.Visible || a.Opacity != b.Opacity || a.AutoLayout != b.AutoLayout
                    || !SameEffects(a, b) || ParentAt(src, a) != ParentAt(dst, b))
                    return false;
                if (a.IsPart && (a.PartSize != b.PartSize || new Point(a.PartOffset.X + shift.X, a.PartOffset.Y + shift.Y) != b.PartOffset
                                 || a.Pivot != b.Pivot || a.PartBorder != b.PartBorder))
                    return false;
            }
            return true;
        }

        /// <summary>원본 레이어를 이미 있는 결과 레이어에 이어 붙인다: 스킨을 맞추고(<see cref="JoinSkins"/>) 셀·프레임 피벗을 복사한다.</summary>
        private void JoinLayer(MergeSource s, Layer src, Layer dst, IReadOnlyList<Frame> frameMap)
        {
            MapsOf(s).Layers[src] = dst;
            JoinSkins(s, src, dst);
            CopyCels(s, src, dst, frameMap);
        }

        /// <summary>원본 스킨 → 결과 스킨: 첫 스킨끼리, 나머지는 첫 스킨이 아닌 같은 이름 스킨(없으면 목록 끝에 새로, 겹치지 않는 이름).</summary>
        private void JoinSkins(MergeSource s, Layer src, Layer dst)
        {
            var maps = MapsOf(s);
            var skins = src.Skins;
            for (int k = 0; k < skins.Count; k++)
            {
                if (k == 0)
                {
                    maps.Skins[skins[0]] = dst.SkinList[0];
                    continue;
                }
                var match = dst.SkinList.Skip(1).FirstOrDefault(x => x.Name == skins[k].Name);
                if (match is null)
                {
                    match = new Skin(_p.UniqueSkinName(dst, skins[k].Name));
                    dst.SkinList.Add(match);
                }
                maps.Skins[skins[k]] = match;
            }
        }

        // ───────────── 복사 ─────────────

        /// <summary>부모 방식 대상: 레이어 나무가 있는 첫 파일의 맨 위 "자식 있는 최상위 레이어".</summary>
        private (int Index, MergeSource Source, Layer Root)? FindParentTarget()
        {
            for (int i = 0; i < sources.Count; i++)
                if (TopLevel(sources[i]).Reverse().FirstOrDefault(l => sources[i].Project.HasChildren(l)) is { } root)
                    return (i, sources[i], root);
            return null;
        }

        /// <summary>
        /// 스킨 방식 대상: 스킨이 여럿인 레이어가 있는 첫 파일(후보 = 그 레이어들), 없으면 레이어 나무가 있는 첫 파일(후보 = 맨 위 "자식 있는 최상위 레이어"의
        /// 자식 없는 자손들). 후보는 원본 레이어(아래 → 위).
        /// </summary>
        private (int Index, MergeSource Source, List<Layer> Candidates)? FindSkinTarget()
        {
            for (int i = 0; i < sources.Count; i++)
            {
                var multi = sources[i].Project.Layers.Where(l => l.Skins.Count > 1).ToList();
                if (multi.Count > 0) return (i, sources[i], multi);
            }
            for (int i = 0; i < sources.Count; i++)
            {
                var p = sources[i].Project;
                if (TopLevel(sources[i]).Reverse().FirstOrDefault(l => p.HasChildren(l)) is not { } root) continue;
                var leaves = p.DescendantsOf(root).Where(l => !p.HasChildren(l)).ToList();
                if (leaves.Count > 0) return (i, sources[i], leaves);
            }
            return null;
        }

        private int? FirstImageIndex()
        {
            for (int i = 0; i < sources.Count; i++)
                if (sources[i].ImageLayer is not null) return i;
            return null;
        }

        private List<Layer> CopyAll(MergeSource s)
        {
            var list = new List<Layer>();
            foreach (var top in TopLevel(s)) list.AddRange(CopyBlock(s, top, _frames, 0));
            return list;
        }

        /// <summary>최상위 레이어(아래 → 위).</summary>
        private static IEnumerable<Layer> TopLevel(MergeSource s) => s.Project.Layers.Where(l => l.Parent is null);

        /// <summary>덩어리(자손 + 자신, 목록 순서).</summary>
        private static List<Layer> BlockOf(SpriteProject project, Layer top) => new(project.DescendantsOf(top)) { top };

        /// <summary>모아 둔 결과 레이어 중 <paramref name="root"/>의 덩어리(자손 + 자신, 모은 순서 = 목록 순서).</summary>
        private List<Layer> BlockInStack(Layer root) => _stack.Where(l => ReferenceEquals(l, root) || IsUnder(l, root)).ToList();

        private static bool IsUnder(Layer layer, Layer ancestor)
        {
            for (var p = layer.Parent; p is not null; p = p.Parent)
                if (ReferenceEquals(p, ancestor)) return true;
            return false;
        }

        /// <summary>
        /// 최상위 덩어리 하나(레이어와 자손 전부)를 스킨·셀까지 복사한다(목록 순서 — 아래 → 위). 원본 프레임 i의 셀은 <paramref name="frameMap"/>[i]로,
        /// 효과 범위는 <paramref name="frameShift"/>만큼 민다.
        /// </summary>
        private List<Layer> CopyBlock(MergeSource s, Layer top, IReadOnlyList<Frame> frameMap, int frameShift)
        {
            var block = BlockOf(s.Project, top);
            var maps = MapsOf(s);
            var copies = new Layer[block.Count];
            // 부모는 자손보다 위(뒤 번호)에 있으므로 위에서부터 복사하면 부모 사본이 먼저 생긴다
            for (int k = block.Count - 1; k >= 0; k--)
                copies[k] = CopyLayer(s, block[k], block[k].Parent is { } parent ? maps.Layers[parent] : null, frameMap, frameShift);
            return [.. copies];
        }

        /// <summary>레이어 하나를 <paramref name="parent"/> 아래(null = 최상위)로 스킨·셀까지 복사한다(타일맵은 타일셋을 같이 담고 격자 원점을 기준점만큼 옮김).</summary>
        private Layer CopyLayer(MergeSource s, Layer src, Layer? parent, IReadOnlyList<Frame> frameMap, int frameShift)
        {
            var copy = CopyProps(s, src, parent, frameShift);
            MapsOf(s).Layers[src] = copy;
            if (src.IsTilemap)
            {
                // 맵·타일셋은 바뀌지 않는 값이라 같이 씀
                foreach (var tileset in s.Project.TilesetSources(src)) _p.PutTilesetRaw(tileset);
                var move = Shift(s);
                if (src.Tiles is { } tiles) copy.Tiles = tiles with { Origin = new Point(tiles.Origin.X + move.X, tiles.Origin.Y + move.Y) };
                CopyCanvasPivots(s, src, copy, frameMap);
                return copy;
            }
            CopySkins(s, src, copy);
            CopyCels(s, src, copy, frameMap);
            return copy;
        }

        /// <summary>그림 같은 레이어(일반 레이어)를 <paramref name="parent"/>의 자식으로(파일 크기 자리 있는 레이어, 기준점 위치, 스킨·프레임 그대로).</summary>
        private Layer ImagePart(MergeSource s, Layer layer, string name, Layer parent)
        {
            var part = CopyProps(s, layer, parent, 0);
            part.Name = name;
            part.Kind = LayerKind.Part;
            part.PartSize = s.Size;
            part.PartOffset = Shift(s);
            // 자리 있는 레이어 = 원본 캔버스 크기·자리이므로 캔버스 기준 피벗 값이 그대로 셀 기준 피벗
            part.Pivot = layer.Pivot;
            var maps = MapsOf(s);
            maps.Layers[layer] = part;
            CopySkins(s, layer, part);
            foreach (var skin in layer.Skins) CopySkinCels(s, layer, skin, part, maps.Skins[skin], _frames);
            for (int i = 0; i < s.FrameCount; i++)
                if (s.Project.Frames[i].LayerPivots.TryGetValue(layer, out var own)) _frames[i].LayerPivots.TryAdd(part, own);
            return part;
        }

        /// <summary>레이어 속성 사본(스킨·셀 없이). 일반 레이어·타일맵의 피벗(캔버스 기준)과 자리 있는 레이어의 위치는 결과 캔버스의 같은 그림 지점으로 옮긴다.</summary>
        private Layer CopyProps(MergeSource s, Layer src, Layer? parent, int frameShift)
        {
            var shift = Shift(s);
            return new Layer(src.Name)
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
                PartOffset = src.IsPart ? new Point(src.PartOffset.X + shift.X, src.PartOffset.Y + shift.Y) : src.PartOffset,
                Pivot = src.IsPart ? src.Pivot : ShiftPivot(src.Pivot, s, options),
                PartBorder = src.PartBorder,
                PartSourceRect = src.PartSourceRect,
                Effects = frameShift == 0 || src.Effects.All(e => e.Frames is null)
                    ? src.Effects
                    : src.Effects.Select(e => e.Frames is { } r ? e with { Frames = new FrameRange(r.From + frameShift, r.To + frameShift) } : e).ToArray(),
            };
        }

        /// <summary>스킨 목록 사본(같은 이름·차례의 새 스킨, 지금 스킨도 그대로).</summary>
        private void CopySkins(MergeSource s, Layer src, Layer dst)
        {
            if (src.IsTilemap) return;
            var maps = MapsOf(s);
            var copies = new List<Skin>(src.Skins.Count);
            foreach (var skin in src.Skins)
            {
                var copy = new Skin(skin.Name);
                maps.Skins[skin] = copy;
                copies.Add(copy);
            }
            dst.ReplaceSkinsRaw(copies);
            dst.SetActiveSkinRaw(maps.Skins[src.ActiveSkin!]);
        }

        /// <summary>모든 스킨의 셀(옮긴 스킨만 — <see cref="Maps.Skins"/>)과 프레임 피벗·옮긴 거리를 복사한다.</summary>
        private void CopyCels(MergeSource s, Layer src, Layer dst, IReadOnlyList<Frame> frameMap)
        {
            var maps = MapsOf(s);
            foreach (var skin in src.Skins)
                if (maps.Skins.TryGetValue(skin, out var dstSkin)) CopySkinCels(s, src, skin, dst, dstSkin, frameMap);
            CopyFramePivots(s, src, dst, frameMap);
        }

        /// <summary>스킨 하나 자신의 셀을 복사한다(일반 레이어는 결과 캔버스에 기준점으로 놓음).</summary>
        private void CopySkinCels(MergeSource s, Layer src, Skin srcSkin, Layer dst, Skin dstSkin, IReadOnlyList<Frame> frameMap)
        {
            for (int i = 0; i < s.FrameCount; i++)
            {
                if (s.Project.GetCelRaw(src, s.Project.Frames[i], srcSkin) is not { } cel || !HasContent(cel)) continue;
                var img = Placed(cel, dst);
                if (HasContent(img)) _p.SetCelRaw(dst, frameMap[i], img, dstSkin);
            }
        }

        /// <summary>원본 셀을 결과 레이어에 놓은 사본: 자리 있는 레이어는 그대로, 일반 레이어는 결과 캔버스에 기준점으로.</summary>
        private PixelImage Placed(PixelImage cel, Layer dst) => dst.IsPart ? cel.Clone() : cel.ResizeCanvas(W, H, options.Anchor);

        /// <summary>프레임마다의 피벗(자리 있는 레이어는 옮긴 거리도) — 이미 둔 프레임은 먼저 넣은 파일의 것.</summary>
        private void CopyFramePivots(MergeSource s, Layer src, Layer dst, IReadOnlyList<Frame> frameMap)
        {
            if (!src.IsPart)
            {
                CopyCanvasPivots(s, src, dst, frameMap);
                return;
            }
            for (int i = 0; i < s.FrameCount; i++)
            {
                if (s.Project.Frames[i].LayerPivots.TryGetValue(src, out var framePivot)) frameMap[i].LayerPivots.TryAdd(dst, framePivot);
                if (s.Project.Frames[i].PartShifts.TryGetValue(src, out var frameShift)) frameMap[i].PartShifts.TryAdd(dst, frameShift);
            }
        }

        /// <summary>
        /// 일반 레이어·타일맵의 프레임마다 피벗(캔버스 기준)을 결과 캔버스의 같은 그림 지점으로 옮겨, 결과 레이어 피벗과 다르면 그 프레임만의 피벗으로 둔다
        /// (이미 둔 프레임은 먼저 넣은 파일의 것).
        /// </summary>
        private void CopyCanvasPivots(MergeSource s, Layer src, Layer dst, IReadOnlyList<Frame> frameMap)
        {
            for (int i = 0; i < s.FrameCount; i++)
            {
                var pivot = ShiftPivot(s.Project.GetLayerPivot(src, i), s, options);
                if (pivot != dst.Pivot) frameMap[i].LayerPivots.TryAdd(dst, pivot);
            }
        }

        // ───────────── 마무리 ─────────────

        /// <summary>레이어·부모·스킨 방식: 프레임 f의 길이 = 그 프레임이 있는 첫 파일의 것, 칸 이름·피벗 = 그것이 있는 첫 파일의 것.</summary>
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
        /// 모은 레이어를 넣는다: 자손을 그 레이어 바로 아래로 모으고(형제끼리는 모은 순서), 이름은 겹치지 않게(얕은 레이어부터, 같은 깊이는 위 레이어부터 원래 이름),
        /// 자동 배치 레이어는 결과 캔버스 기준으로 자식 자리를 다시 계산한다(자식은 자손째 옮겨 감).
        /// </summary>
        private void FinishLayers()
        {
            var children = new Dictionary<Layer, List<Layer>>();
            foreach (var l in _stack)
                if (l.Parent is { } parent)
                {
                    if (!children.TryGetValue(parent, out var kids)) children[parent] = kids = [];
                    kids.Add(l);
                }
            var ordered = new List<Layer>(_stack.Count);
            void Emit(Layer layer)
            {
                if (children.TryGetValue(layer, out var kids))
                    foreach (var k in kids) Emit(k);
                ordered.Add(layer);
            }
            foreach (var l in _stack)
                if (l.Parent is null) Emit(l);

            static int Depth(Layer layer)
            {
                int depth = 0;
                for (var p = layer.Parent; p is not null; p = p.Parent) depth++;
                return depth;
            }
            var used = new HashSet<string>();
            string Unique(string name)
            {
                if (used.Add(name)) return name;
                for (int i = 2; ; i++)
                    if (used.Add($"{name} {i}")) return $"{name} {i}";
            }
            foreach (var l in ordered.Select((l, i) => (Layer: l, Index: i)).OrderBy(x => Depth(x.Layer)).ThenByDescending(x => x.Index).Select(x => x.Layer).ToList())
                l.Name = Unique(l.Name);

            foreach (var l in ordered) _p.AddLayerRaw(l);
            foreach (var parent in ordered.Where(l => l is { IsPart: true, AutoLayout: not null }))
                foreach (var (child, offset) in _p.ComputeChildLayout(parent, parent.AutoLayout!))
                {
                    var delta = new Point(offset.X - child.PartOffset.X, offset.Y - child.PartOffset.Y);
                    child.PartOffset = offset;
                    MoveChildren(child, delta);
                }
        }

        /// <summary>자손을 같은 거리만큼 옮긴다. 자동 배치되는 자손(과 그 자손)은 그 부모의 배치가 자리를 정하므로 건너뛴다.</summary>
        private void MoveChildren(Layer layer, Point delta)
        {
            if (delta.IsEmpty) return;
            foreach (var child in _p.ChildrenOf(layer))
            {
                if (child.IsAutoLaidOut) continue;
                child.PartOffset = new Point(child.PartOffset.X + delta.X, child.PartOffset.Y + delta.Y);
                MoveChildren(child, delta);
            }
        }

        /// <summary>프로젝트 파일의 스킨 프리셋을 복사한 레이어·스킨을 가리키도록 옮긴다(옮길 수 없는 항목은 빼고, 같은 프리셋은 한 번만).</summary>
        private void CopySkinPresets()
        {
            foreach (var s in sources.Where(s => s.IsProject))
            {
                var maps = MapsOf(s);
                foreach (var preset in s.Project.SkinPresets)
                {
                    var entries = new List<SkinPresetEntry>(preset.Entries.Count);
                    foreach (var e in preset.Entries)
                        if (maps.Layers.TryGetValue(e.Layer, out var layer) && maps.Skins.TryGetValue(e.Skin, out var skin) && layer.SkinList.Contains(skin)
                            && entries.All(x => !ReferenceEquals(x.Layer, layer)))
                            entries.Add(new SkinPresetEntry(layer, skin));
                    if (entries.Count == 0) continue;
                    if (_p.SkinPresets.Any(x => x.Name == preset.Name && x.Entries.SequenceEqual(entries))) continue;
                    _p.AddSkinPresetRaw(new SkinPreset(_p.UniqueSkinPresetName(preset.Name), entries));
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
                Note(DomainStrings.FormatMerge_RenameTag(name: tag.Name, renamed: renamed));
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

        private static bool HasContent(PixelImage? image) => image is not null && !image.IsRegionEmpty(image.Bounds);

        /// <summary>새로 만드는 레이어(빈 부모·스킨 레이어) 이름: 결과 이름, 비었으면 "레이어 1".</summary>
        private string NewLayerName() => string.IsNullOrWhiteSpace(options.Name) ? DomainStrings.FormatProject_DefaultLayerName(number: 1) : options.Name.Trim();

        /// <summary>지키는 구조 설명(목적격 조사 포함): "부모·자식 레이어와 스킨을" / "부모·자식 레이어를" / "레이어·스킨을" / "레이어 구조를".</summary>
        private static string Kept(MergeSource s)
        {
            bool tree = s.HasTree;
            bool skins = s.Project.Layers.Any(l => l.Skins.Count > 1);
            return (tree, skins) switch
            {
                (true, true) => DomainStrings.Merge_KeepTreeAndSkins,
                (true, false) => DomainStrings.Merge_KeepTree,
                (false, true) => DomainStrings.Merge_KeepLayersAndSkins,
                _ => DomainStrings.Merge_KeepLayerStructure,
            };
        }

        private void Note(string text)
        {
            if (notes is not null && !notes.Contains(text)) notes.Add(text);
        }
    }
}
