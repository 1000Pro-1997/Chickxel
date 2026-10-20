using System.Drawing;
using Gpixel.Core.Model;

namespace Gpixel.Core.IO;

/// <summary>여러 파일을 프로젝트 하나로 합치는 방식.</summary>
public enum MergeMode
{
    /// <summary>파일마다 일반 레이어 1개(캔버스 크기, 기준점에 배치).</summary>
    Layers,
    /// <summary>그룹 1개 안에 파일마다 부위 1개(파일 크기 그대로, 기준점 위치).</summary>
    Group,
    /// <summary>파일마다 프레임(파일의 프레임을 차례로 이어 붙임).</summary>
    Frames,
    /// <summary>그룹 1개 안에 부위 1개, 파일마다 그 그룹의 스킨 1개(부위 크기 = 가장 큰 파일, 기준점 위치).</summary>
    Skins,
}

/// <summary>
/// 합칠 파일 하나: 이름(확장자 뺀 파일 이름)과 프레임마다 보이는 레이어를 합성한 그림·길이·태그.
/// PNG는 그림 전체가 프레임 1개, <c>.gpixel</c>은 프레임마다 합성한 그림(태그·Unity 설정 포함).
/// </summary>
public sealed class MergeSource
{
    public MergeSource(string name, IReadOnlyList<PixelImage> frames, IReadOnlyList<int> durations,
        IReadOnlyList<AnimationTag>? tags = null, SpriteSettings? sprite = null)
    {
        if (frames.Count == 0) throw new ArgumentException("프레임이 없습니다.", nameof(frames));
        if (durations.Count != frames.Count) throw new ArgumentException("프레임 길이 수가 프레임 수와 다릅니다.", nameof(durations));
        Name = name;
        Frames = frames;
        Durations = durations;
        Tags = tags ?? [];
        Sprite = sprite;
    }

    public string Name { get; }
    public IReadOnlyList<PixelImage> Frames { get; }
    public IReadOnlyList<int> Durations { get; }
    public IReadOnlyList<AnimationTag> Tags { get; }
    /// <summary>프로젝트였으면 그 Unity 설정(PNG면 null).</summary>
    public SpriteSettings? Sprite { get; }
    /// <summary>그림 크기(모든 프레임 같음).</summary>
    public Size Size => Frames[0].Size;

    /// <summary>PNG 또는 <c>.gpixel</c>을 읽는다.</summary>
    public static MergeSource Load(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (!path.EndsWith(SpriteProject.FileExtension, StringComparison.OrdinalIgnoreCase))
            return new MergeSource(name, [PngIO.Load(path)], [Frame.DefaultDurationMs]);
        var p = ProjectIO.Load(path);
        var frames = new PixelImage[p.Frames.Count];
        for (int f = 0; f < frames.Length; f++) frames[f] = p.Flatten(f);
        return new MergeSource(name, frames, p.Frames.Select(f => f.DurationMs).ToArray(),
            p.Tags.Select(t => t.Clone()).ToArray(), p.Sprite.Clone());
    }
}

/// <summary>합치기 설정.</summary>
/// <param name="Mode">합치는 방식.</param>
/// <param name="Width">결과 캔버스 가로.</param>
/// <param name="Height">결과 캔버스 세로.</param>
/// <param name="Anchor">파일 그림을 캔버스에 놓을 기준점.</param>
/// <param name="Name">결과 이름(그룹·스킨 방식이면 그룹 이름).</param>
/// <param name="EmptyFirst">그룹·스킨 방식에서 맨 앞에 빈 항목(<see cref="ProjectMerge.EmptyName"/>)을 넣는다: 그룹이면 맨 위 빈 부위, 스킨이면 첫(기본·지금) 스킨이 빈 스킨.
/// 합친 파일들은 그 뒤의 대안이 된다. 레이어·프레임 방식에서는 쓰지 않는다.</param>
public sealed record MergeOptions(MergeMode Mode, int Width, int Height, Anchor Anchor, string Name, bool EmptyFirst = false);

/// <summary>
/// 파일 여러 개 → 새 프로젝트(기록 없음, 수정됨 아님).
/// <list type="bullet">
/// <item>레이어: 첫 파일이 맨 위 레이어(레이어 이름 = 파일 이름). 프레임 수 = 가장 많은 파일의 프레임 수.</item>
/// <item>그룹: 그룹 하나(<see cref="MergeOptions.Name"/>)에 첫 파일이 맨 위 부위. 부위 크기 = 파일 크기, 위치 = 기준점.
/// <see cref="MergeOptions.EmptyFirst"/>면 그 위에 빈 부위 "없음"(크기 = 가장 큰 파일, 기준점 위치).</item>
/// <item>스킨: 그룹 하나(<see cref="MergeOptions.Name"/>)에 부위 "부위" 하나, 파일마다 전체 스킨 하나(스킨 이름 = 파일 이름, 첫 파일 = 첫 스킨·지금 스킨).
/// 부위 크기 = 가장 큰 파일 크기(기준점 위치), 작은 파일은 부위 안 기준점에 놓는다. 프레임이 모자란 스킨은 빈 칸이라 첫 스킨 셀을 물려받는다.
/// <see cref="MergeOptions.EmptyFirst"/>면 첫(기본·지금) 스킨이 그림 없는 "없음"이고 파일 스킨은 그 뒤(모자란 프레임은 비어 보임).</item>
/// <item>프레임: 파일 순서대로 프레임을 이어 붙인다. 프레임이 하나인 파일은 그 프레임의 칸 이름 = 파일 이름,
/// 여러 개인 파일은 자기 태그를 옮겨 오고 태그가 없으면 파일 이름으로 태그 하나.</item>
/// </list>
/// 레이어·그룹·스킨 방식의 프레임 길이는 그 프레임이 있는 첫 파일의 길이, 태그는 이름이 겹치지 않는 것만 옮긴다.
/// Unity 설정은 첫 프로젝트 파일의 것(없으면 <paramref name="spriteDefaults"/>).
/// </summary>
public static class ProjectMerge
{
    /// <summary>빈 첫 항목(<see cref="MergeOptions.EmptyFirst"/>)의 이름.</summary>
    public const string EmptyName = "없음";

    public static SpriteProject Merge(IReadOnlyList<MergeSource> sources, MergeOptions options, SpriteSettings? spriteDefaults = null)
    {
        if (sources.Count == 0) throw new ArgumentException("합칠 파일이 없습니다.", nameof(sources));
        int w = options.Width, h = options.Height;
        var p = SpriteProject.CreateBlank(w, h);
        if ((sources.FirstOrDefault(s => s.Sprite is not null)?.Sprite ?? spriteDefaults) is { } sprite) p.Sprite.CopyFrom(sprite);

        if (options.Mode == MergeMode.Frames)
        {
            var layer = new Layer("레이어 1");
            p.AddLayerRaw(layer);
            foreach (var s in sources)
            {
                int start = p.Frames.Count;
                for (int i = 0; i < s.Frames.Count; i++)
                {
                    var frame = new Frame { DurationMs = s.Durations[i], Name = s.Frames.Count == 1 ? s.Name : null };
                    p.AddFrameRaw(frame);
                    SetCel(p, layer, frame, s.Frames[i].ResizeCanvas(w, h, options.Anchor));
                }
                if (s.Frames.Count == 1) continue;
                if (s.Tags.Count == 0)
                    AddTag(p, new AnimationTag(s.Name, start, p.Frames.Count - 1) { Color = AnimationTag.DefaultColors[p.Tags.Count % AnimationTag.DefaultColors.Count] });
                foreach (var t in s.Tags) AddTag(p, Shifted(t, start));
            }
            return p;
        }

        int frameCount = sources.Max(s => s.Frames.Count);
        var frames = new Frame[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            frames[f] = new Frame { DurationMs = sources.First(s => f < s.Frames.Count).Durations[f] };
            p.AddFrameRaw(frames[f]);
        }

        // 아래 → 위로 넣으므로 마지막 파일부터(첫 파일이 맨 위).
        var ordered = sources.Reverse().ToList();
        if (options.Mode == MergeMode.Group)
        {
            var group = NewGroup(options);
            foreach (var s in ordered)
            {
                var (x, y) = PixelImage.AnchorOffset(s.Size.Width, s.Size.Height, w, h, options.Anchor);
                var part = new Layer(UniqueName(p, s.Name, group.Name))
                {
                    Kind = LayerKind.Part,
                    Parent = group,
                    PartSize = s.Size,
                    PartOffset = new Point(x, y),
                };
                p.AddLayerRaw(part);
                for (int f = 0; f < s.Frames.Count; f++) SetCel(p, part, frames[f], s.Frames[f].Clone());
            }
            if (options.EmptyFirst)
            {
                // 맨 위 빈 부위(가장 큰 파일 크기, 기준점 위치): 부위를 하나씩 켜 보는 대안 묶음의 "아무것도 없음".
                var size = new Size(sources.Max(s => s.Size.Width), sources.Max(s => s.Size.Height));
                var (x, y) = PixelImage.AnchorOffset(size.Width, size.Height, w, h, options.Anchor);
                p.AddLayerRaw(new Layer(UniqueName(p, EmptyName, group.Name))
                {
                    Kind = LayerKind.Part,
                    Parent = group,
                    PartSize = size,
                    PartOffset = new Point(x, y),
                });
            }
            p.AddLayerRaw(group);
        }
        else if (options.Mode == MergeMode.Skins)
        {
            var group = NewGroup(options);
            var size = new Size(sources.Max(s => s.Size.Width), sources.Max(s => s.Size.Height));
            var (x, y) = PixelImage.AnchorOffset(size.Width, size.Height, w, h, options.Anchor);
            var part = new Layer(UniqueName(p, "부위", group.Name))
            {
                Kind = LayerKind.Part,
                Parent = group,
                PartSize = size,
                PartOffset = new Point(x, y),
            };
            var skins = new List<Skin>(sources.Count + 1);
            if (options.EmptyFirst) skins.Add(new Skin(EmptyName));
            int first = skins.Count;
            foreach (var s in sources) skins.Add(new Skin(UniqueSkinName(skins, s.Name)));
            group.ReplaceSkinsRaw(skins);
            p.AddLayerRaw(part);
            p.AddLayerRaw(group);
            for (int i = 0; i < sources.Count; i++)
                for (int f = 0; f < sources[i].Frames.Count; f++)
                    SetCel(p, part, frames[f], sources[i].Frames[f].ResizeCanvas(size.Width, size.Height, options.Anchor), skins[first + i]);
        }
        else
        {
            foreach (var s in ordered)
            {
                var layer = new Layer(UniqueName(p, s.Name));
                p.AddLayerRaw(layer);
                for (int f = 0; f < s.Frames.Count; f++) SetCel(p, layer, frames[f], s.Frames[f].ResizeCanvas(w, h, options.Anchor));
            }
        }
        foreach (var t in sources.SelectMany(s => s.Tags)) AddTag(p, Shifted(t, 0));
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

    private static Layer NewGroup(MergeOptions options)
        => new(string.IsNullOrWhiteSpace(options.Name) ? "그룹" : options.Name.Trim()) { Kind = LayerKind.Group };

    private static void SetCel(SpriteProject p, Layer layer, Frame frame, PixelImage image, Skin? skin = null)
    {
        if (!image.IsRegionEmpty(image.Bounds)) p.SetCelRaw(layer, frame, image, skin);
    }

    private static string UniqueSkinName(List<Skin> taken, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "스킨";
        bool Used(string n) => taken.Any(s => s.Name == n);
        if (!Used(name)) return name;
        for (int i = 2; ; i++)
            if (!Used($"{name} {i}")) return $"{name} {i}";
    }

    private static AnimationTag Shifted(AnimationTag t, int offset)
    {
        var copy = t.Clone();
        copy.From += offset;
        copy.To += offset;
        return copy;
    }

    private static void AddTag(SpriteProject p, AnimationTag tag)
    {
        if (p.HasTag(tag.Name) || tag.From < 0 || tag.To >= p.Frames.Count || tag.From > tag.To) return;
        p.AddTagRaw(tag);
    }

    private static string UniqueName(SpriteProject p, string name, params string[] taken)
    {
        bool Used(string n) => taken.Contains(n) || p.Layers.Any(l => l.Name == n);
        if (!Used(name)) return name;
        for (int i = 2; ; i++)
            if (!Used($"{name} {i}")) return $"{name} {i}";
    }
}
