using System.Text.Json;
using Chickxel.Core.Model;

namespace Chickxel.Core.IO;

/// <summary>
/// 즐겨찾기한 Lospec 팔레트 하나(<see cref="LospecFavoriteStore"/> 파일에 담는 모양). 인터넷 없이 보이고 쓰는 데 필요한 정보를 모두 담는다:
/// 색은 "#RRGGBB", 예시 그림은 원래 주소(내려받은 그림은 <see cref="LospecFavoriteStore.ExamplePath"/>), 즐겨찾기한 때(UTC).
/// </summary>
public sealed class LospecFavorite
{
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";
    public List<string> Colors { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int Likes { get; set; }
    public int Downloads { get; set; }
    public List<string> Examples { get; set; } = new();
    public DateTime AddedUtc { get; set; }

    public static LospecFavorite From(LospecPalette p, DateTime addedUtc) => new()
    {
        Slug = p.Slug,
        Title = p.Title,
        Author = p.Author,
        Colors = [.. p.Colors.Select(c => c.ToHex())],
        Tags = [.. p.Tags],
        Likes = p.Likes,
        Downloads = p.Downloads,
        Examples = [.. p.Examples.Select(u => u.AbsoluteUri)],
        AddedUtc = addedUtc,
    };

    /// <summary>목록·고른 팔레트 칸에 쓰는 팔레트(읽을 수 없는 색·주소는 건너뜀).</summary>
    public LospecPalette ToPalette()
    {
        var colors = new List<Color32>();
        foreach (var hex in Colors)
            if (Color32.TryParseHex(hex, out var c)) colors.Add(c);
        var examples = new List<Uri>();
        foreach (var url in Examples)
            if (Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") examples.Add(u);
        return new LospecPalette(Title.Length > 0 ? Title : Slug, Slug, Author, colors, Likes, Downloads, [.. Tags], examples);
    }
}

/// <summary>
/// Lospec 즐겨찾기 보관소(<c>&lt;앱 설정 폴더&gt;/Chickxel/Lospec</c>): 목록 <c>favorites.json</c>(<see cref="LospecFavorite"/> 배열, 임시 파일에 쓴 뒤 바꿔치기)과
/// 즐겨찾기할 때 한 번 내려받는 예시 그림 <c>examples/&lt;slug&gt;/&lt;n&gt;.png</c>(n = 예시 순번, 0부터, <see cref="MaxExamples"/>장까지).
/// 목록은 늘 최근에 즐겨찾기한 것부터. 파일이 없거나 깨졌으면 빈 목록으로 시작한다. 한 스레드(UI)에서만 쓴다.
/// </summary>
public sealed class LospecFavoriteStore
{
    public const string FileName = "favorites.json";
    public const string ExamplesFolder = "examples";
    /// <summary>팔레트 하나에 내려받아 두는 예시 그림 수(창에 보이는 수와 같음).</summary>
    public const int MaxExamples = 6;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private List<LospecFavorite> _items;

    private LospecFavoriteStore(string root, List<LospecFavorite> items)
    {
        Root = root;
        _items = items;
    }

    public string Root { get; }
    public string FilePath => Path.Combine(Root, FileName);

    /// <summary>즐겨찾기(최근에 더한 것부터).</summary>
    public IReadOnlyList<LospecFavorite> Items => _items;

    /// <summary>
    /// <paramref name="root"/>의 목록을 읽는다. 없거나 깨졌으면 빈 목록, 슬러그나 색이 없는 항목은 건너뛰고 같은 슬러그는 최근 것 하나만 둔다.
    /// </summary>
    public static LospecFavoriteStore Load(string root)
    {
        List<LospecFavorite>? items = null;
        try
        {
            var file = Path.Combine(root, FileName);
            if (File.Exists(file)) items = JsonSerializer.Deserialize<List<LospecFavorite>>(File.ReadAllText(file), Options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            items = null;
        }
        var valid = (items ?? []).Where(f => f is not null && f.Slug is { Length: > 0 } && f.Colors is { Count: > 0 })
            .Select(f =>
            {
                f.Title ??= "";
                f.Author ??= "";
                f.Tags ??= [];
                f.Examples ??= [];
                return f;
            });
        var ordered = Order(valid);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return new LospecFavoriteStore(root, [.. ordered.Where(f => seen.Add(f.Slug))]);
    }

    /// <summary>목록 순서: 최근에 즐겨찾기한 것부터(같으면 슬러그 순).</summary>
    public static List<LospecFavorite> Order(IEnumerable<LospecFavorite> items)
        => [.. items.OrderByDescending(f => f.AddedUtc).ThenBy(f => f.Slug, StringComparer.Ordinal)];

    /// <summary>
    /// 색 수(<paramref name="filter"/>·<paramref name="count"/>)와 글자(<paramref name="text"/>: 제목이나 태그에 들어 있음, 대소문자 무시, 비었으면 모두)로 거른다.
    /// 순서는 그대로.
    /// </summary>
    public static IEnumerable<LospecFavorite> Filter(IEnumerable<LospecFavorite> items, LospecColorFilter filter, int count, string? text)
    {
        var needle = (text ?? "").Trim();
        foreach (var f in items)
        {
            int n = f.Colors.Count;
            bool colorsOk = filter switch
            {
                LospecColorFilter.Max => n <= count,
                LospecColorFilter.Min => n >= count,
                LospecColorFilter.Exact => n == count,
                _ => true,
            };
            if (!colorsOk) continue;
            if (needle.Length > 0
                && !f.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !f.Tags.Any(t => t.Contains(needle, StringComparison.OrdinalIgnoreCase))) continue;
            yield return f;
        }
    }

    public bool Contains(string slug) => Find(slug) is not null;

    public LospecFavorite? Find(string slug) => _items.Find(f => f.Slug == slug);

    /// <summary>
    /// 즐겨찾기를 켜고 끄고 목록을 쓴다. 켜면 맨 앞(<paramref name="nowUtc"/>), 끄면 내려받은 예시 그림도 지운다. 바뀐 뒤의 상태(true = 즐겨찾기).
    /// 목록을 쓰지 못하면 바꾸지 않은 채 예외(IOException 등).
    /// </summary>
    public bool Toggle(LospecPalette p, DateTime nowUtc)
    {
        if (Contains(p.Slug))
        {
            Remove(p.Slug);
            return false;
        }
        Add(p, nowUtc);
        return true;
    }

    /// <summary>즐겨찾기에 더하고 목록을 쓴다(이미 있으면 그대로). 쓰지 못하면 바꾸지 않은 채 예외.</summary>
    public LospecFavorite Add(LospecPalette p, DateTime nowUtc)
    {
        if (Find(p.Slug) is { } existing) return existing;
        var entry = LospecFavorite.From(p, nowUtc);
        Commit(Order(_items.Append(entry)));
        return entry;
    }

    /// <summary>즐겨찾기에서 빼고 목록을 쓴 뒤 예시 그림 폴더를 지운다. 없었으면 false. 쓰지 못하면 바꾸지 않은 채 예외.</summary>
    public bool Remove(string slug)
    {
        if (!Contains(slug)) return false;
        Commit([.. _items.Where(f => f.Slug != slug)]);
        if (ExamplesDirectory(slug) is { } dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 남은 그림은 다시 즐겨찾기하면 그대로 쓴다.
            }
        }
        return true;
    }

    private void Commit(List<LospecFavorite> items)
    {
        Directory.CreateDirectory(Root);
        UndoHistoryIO.WriteAtomic(FilePath, JsonSerializer.SerializeToUtf8Bytes(items, Options));
        _items = items;
    }

    /// <summary>폴더 이름으로 쓸 수 있는 슬러그(영문 소문자·숫자·'-'·'_'만, 경로가 밖으로 새지 않게).</summary>
    public static bool IsSafeSlug(string? slug)
        => slug is { Length: > 0 and <= 128 } && slug.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    /// <summary>팔레트의 예시 그림 폴더. 폴더 이름으로 쓸 수 없는 슬러그면 null(그림을 남기지 않음).</summary>
    public string? ExamplesDirectory(string slug) => IsSafeSlug(slug) ? Path.Combine(Root, ExamplesFolder, slug) : null;

    /// <summary><paramref name="index"/>번째 예시 그림 파일(<c>examples/&lt;slug&gt;/&lt;n&gt;.png</c>). 남기지 않는 그림이면 null.</summary>
    public string? ExamplePath(string slug, int index)
        => index is >= 0 and < MaxExamples && ExamplesDirectory(slug) is { } dir ? Path.Combine(dir, index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png") : null;

    /// <summary>남겨 둔 예시 그림 바이트. 없거나 읽지 못하면 null.</summary>
    public byte[]? ReadExample(string slug, int index)
    {
        if (ExamplePath(slug, index) is not { } path) return null;
        try
        {
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>즐겨찾기한 팔레트의 예시 그림을 남긴다(즐겨찾기가 아니면 남기지 않음). 남겼으면 true, 쓰지 못하면 false.</summary>
    public bool WriteExample(string slug, int index, byte[] data)
    {
        if (!Contains(slug) || ExamplePath(slug, index) is not { } path) return false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            UndoHistoryIO.WriteAtomic(path, data);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>즐겨찾기한 팔레트의 예시 그림 중 아직 남기지 않은 순번(<see cref="MaxExamples"/>장까지).</summary>
    public IEnumerable<int> MissingExamples(string slug)
    {
        if (Find(slug) is not { } f) yield break;
        for (int i = 0; i < Math.Min(f.Examples.Count, MaxExamples); i++)
            if (ExamplePath(slug, i) is { } path && !File.Exists(path)) yield return i;
    }
}
