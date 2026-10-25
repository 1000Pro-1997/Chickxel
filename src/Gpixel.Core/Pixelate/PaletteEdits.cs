using Gpixel.Core.Model;

namespace Gpixel.Core.Pixelate;

/// <summary>
/// 색 빼기·고정 색 목록("#RRGGBB")을 다루는 도움말. 색은 거리로 맞추므로(OKLab 거리 ≤ <see cref="MatchDistance"/>) 설정이 조금 달라 팔레트 색이 살짝 바뀌어도 그대로 맞는다.
/// 색 빼기(<see cref="Apply"/>)는 다 만든 결과의 뒤처리: 뺀 색과 맞는 결과 색을 지우고, 그 픽셀만 원본 칸 색(<see cref="PixelateResult.SourceColors"/>)에
/// 가장 가까운 남은 결과 색으로 바꾼다 — 다른 픽셀·색은 그대로라 결과 색 수가 뺀 만큼 준다.
/// 고정 색과 맞는 색은 빠지지 않고, 모든 색이 빠지게 되면 마지막에 넣은 빼기부터 무시한다 — 늘 한 색은 남는다.
/// </summary>
public static class PaletteEdits
{
    /// <summary>같은 색으로 볼 OKLab 거리(눈에 겨우 보이는 차이 ≈ 0.02보다 조금 큼).</summary>
    public const float MatchDistance = 0.03f;

    /// <summary>두 색이 같은 색으로 맞는지(알파는 보지 않음).</summary>
    public static bool Matches(Color32 a, Color32 b) => Lab.Distance(OkLab.FromColor(a), OkLab.FromColor(b)) <= MatchDistance;

    /// <summary><paramref name="c"/>가 목록(빼기·고정 색)의 어느 색과 맞는지.</summary>
    public static bool MatchesAny(IReadOnlyList<string> list, Color32 c)
    {
        if (list.Count == 0) return false;
        var lab = OkLab.FromColor(c);
        foreach (var e in Parse(list))
            if (Lab.Distance(lab, e) <= MatchDistance) return true;
        return false;
    }

    /// <summary>
    /// 팔레트 색 중 뺄 색(불투명 ARGB). 고정 색과 맞는 색은 빼지 않고, 모두 빠지게 되면 마지막 빼기부터 무시한다. 빼기가 없거나 팔레트가 비면 빈 집합.
    /// </summary>
    public static HashSet<uint> Removed(IEnumerable<Color32> palette, IReadOnlyList<string> excluded, IReadOnlyList<string> pinned)
    {
        var removed = new HashSet<uint>();
        var ex = Parse(excluded);
        if (ex.Count == 0) return removed;
        var colors = palette.Select(c => c.Argb | 0xFF000000u).Distinct().OrderBy(v => v).ToList();
        if (colors.Count == 0) return removed;
        var pins = Parse(pinned);
        // 색마다 맞는 첫 빼기(없거나 고정 색이면 int.MaxValue) — 앞쪽 n개만 쓸 때 빠지는지 바로 안다.
        var first = new int[colors.Count];
        for (int i = 0; i < colors.Count; i++)
        {
            first[i] = int.MaxValue;
            var lab = OkLab.FromColor(new Color32(colors[i]));
            if (pins.Any(p => Lab.Distance(lab, p) <= MatchDistance)) continue;
            for (int e = 0; e < ex.Count; e++)
                if (Lab.Distance(lab, ex[e]) <= MatchDistance) { first[i] = e; break; }
        }
        int n = ex.Count;
        while (n > 0 && first.All(f => f < n)) n = first.Where(f => f < n).Max();
        for (int i = 0; i < colors.Count; i++)
            if (first[i] < n) removed.Add(colors[i]);
        return removed;
    }

    /// <summary>
    /// 색 빼기 뒤처리: 그림들(결과 프레임)에 쓰인 색 중 <see cref="Removed"/>의 픽셀을, 같은 자리 원본 칸 색(<paramref name="sources"/> — 그림과 같은 크기,
    /// 투명이거나 없으면 그 픽셀 색)에 <paramref name="metric"/>(없으면 OKLab)로 가장 가까운 남은 색으로 바꾼다(알파는 그대로). 그 자리에서 바꾸고 바꾼 픽셀 수를 돌려준다.
    /// <paramref name="replaced"/>를 주면(그림 수만큼) 그림마다 바뀐 픽셀의 원래 색을 같은 크기 그림에 담는다(나머지 투명, 바뀐 픽셀이 없으면 null).
    /// </summary>
    public static int Apply(IReadOnlyList<PixelImage> images, IReadOnlyList<PixelImage>? sources, IReadOnlyList<string> excluded, IReadOnlyList<string> pinned,
        PixelImage?[]? replaced = null, ColorMetric? metric = null)
    {
        if (excluded.Count == 0) return 0;
        var used = new HashSet<uint>();
        foreach (var img in images)
            foreach (var c in img.Pixels)
                if (c.A != 0) used.Add(c.Argb | 0xFF000000u);
        var removed = Removed(used.Select(v => new Color32(v)), excluded, pinned);
        if (removed.Count == 0) return 0;
        var kept = used.Where(v => !removed.Contains(v)).Order().Select(v => new Color32(v)).ToArray();
        metric ??= ColorMetric.OkLab;
        var keptPts = kept.Select(metric.Point).ToArray();
        var nearest = new Dictionary<uint, Color32>();
        Color32 Nearest(Color32 c)
        {
            uint key = c.Argb | 0xFF000000u;
            if (nearest.TryGetValue(key, out var hit)) return hit;
            return nearest[key] = kept[metric.Nearest(metric.Point(c), keptPts)];
        }
        int changed = 0;
        for (int n = 0; n < images.Count; n++)
        {
            var img = images[n];
            var px = img.Pixels;
            var src = sources is not null && n < sources.Count && sources[n].Size == img.Size ? sources[n].Pixels : null;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                if (c.A == 0 || !removed.Contains(c.Argb | 0xFF000000u)) continue;
                if (replaced is not null) (replaced[n] ??= new PixelImage(img.Width, img.Height)).Pixels[i] = c;
                var from = src is not null && src[i].A != 0 ? src[i] : c;
                px[i] = Nearest(from).WithAlpha(c.A);
                changed++;
            }
        }
        return changed;
    }

    /// <summary>
    /// 목록(빼기·고정 색)에서 켜고 끄기: <paramref name="c"/>와 맞는 색이 있으면 모두 지우고, 없으면 끝에 더한 새 목록.
    /// </summary>
    public static IReadOnlyList<string> Toggle(IReadOnlyList<string> list, Color32 c)
    {
        var result = list.Where(h => !(Color32.TryParseHex(h, out var e) && Matches(e, c))).ToList();
        if (result.Count == list.Count) result.Add(c.WithAlpha(255).ToHex());
        return result;
    }

    /// <summary>목록의 색(읽을 수 없는 것은 뺌, 불투명으로).</summary>
    public static List<Color32> Colors(IReadOnlyList<string> hex)
    {
        var r = new List<Color32>(hex.Count);
        foreach (var h in hex)
            if (Color32.TryParseHex(h, out var c)) r.Add(c.WithAlpha(255));
        return r;
    }

    private static List<Lab> Parse(IReadOnlyList<string> hex) => Colors(hex).Select(OkLab.FromColor).ToList();
}
