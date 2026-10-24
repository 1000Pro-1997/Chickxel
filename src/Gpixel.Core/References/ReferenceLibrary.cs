using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Gpixel.Core.IO;
using Gpixel.Core.Model;

namespace Gpixel.Core.References;

/// <summary><see cref="ReferenceLibrary.Open"/>의 선택 사항.</summary>
public sealed class ReferenceLibraryOptions
{
    /// <summary>PNG·.gpixel 말고 다른 그림(.gif)을 프레임으로 푼다. 못 풀면 null. 없으면 그런 파일은 건너뛴다.</summary>
    public Func<string, ReferenceFrames?>? DecodeOther { get; init; }

    /// <summary>DecodeOther가 푸는 확장자(소문자, 점 포함).</summary>
    public IReadOnlyList<string> OtherImageExtensions { get; init; } = [];

    /// <summary>기본 예시 그림(읽기 전용).</summary>
    public IReadOnlyList<BuiltInReference> BuiltIns { get; init; } = [];

    /// <summary>기본 규칙·규칙 틀을 한국어로(false = 영어).</summary>
    public bool Korean { get; init; }

    /// <summary>지금 시각(UTC). 시험용.</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.UtcNow;
}

/// <summary>다시 살펴본 결과: 새로 넣은·바뀐·없어진 파일 수.</summary>
public readonly record struct ReferenceRescanResult(int Added, int Changed, int Removed)
{
    public bool Any => Added + Changed + Removed > 0;
}

/// <summary>
/// AI 참고 자료 보관소: 한 폴더(기본 <c>&lt;설정 폴더&gt;/Gpixel/References</c>) 안의 그림(.png·.gif·.gpixel)과 글 메모(.md·.txt)를
/// <c>index.json</c>(임시 파일에 쓴 뒤 바꿔치기)에 색인한다. 앱 밖에서 넣은 파일도 <see cref="Rescan"/>이 찾아 정보를 저절로 채우고,
/// 없어진 파일은 빼고(같은 내용이 다시 나타나면 제목·설명·태그를 되살림), 내용이 바뀐 파일은 그림 정보만 다시 읽는다.
/// 폴더 바로 아래 <c>rules.md</c>는 사용자 그림 규칙(목록에 넣지 않음). 기본 예시·기본 규칙은 읽기 전용으로 함께 보인다.
/// 한 스레드(UI)에서만 쓴다.
/// </summary>
public sealed partial class ReferenceLibrary
{
    public const string IndexFileName = "index.json";
    public const string RulesFileName = "rules.md";
    /// <summary>글 메모에서 읽는 글자 수 상한.</summary>
    public const int MaxNoteLength = 64 * 1024;
    /// <summary>없어진 항목 정보를 기억해 두는 수(같은 파일이 돌아오면 되살림).</summary>
    private const int MaxRemembered = 100;

    public static IReadOnlyList<string> NoteExtensions { get; } = [".md", ".txt"];

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private sealed class IndexDto
    {
        public int Version { get; set; } = 1;
        public List<ReferenceItem> Items { get; set; } = new();
        public List<ReferenceItem> Removed { get; set; } = new();
    }

    private readonly ReferenceLibraryOptions _options;
    private readonly Dictionary<string, BuiltInReference> _builtIns;
    private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);
    private List<ReferenceItem> _items = new();
    private List<ReferenceItem> _removed = new();
    private bool _dirty;

    private ReferenceLibrary(string root, ReferenceLibraryOptions options)
    {
        Root = root;
        _options = options;
        _builtIns = options.BuiltIns.ToDictionary(b => b.Item.Id, StringComparer.Ordinal);
    }

    /// <summary>자료 폴더(전체 경로).</summary>
    public string Root { get; }

    public string IndexPath => Path.Combine(Root, IndexFileName);

    /// <summary>사용자 그림 규칙 파일(없을 수 있음 — <see cref="EnsureUserRules"/>).</summary>
    public string UserRulesPath => Path.Combine(Root, RulesFileName);

    /// <summary>기본 예시를 목록·검색에 넣는다(설정 "기본 예시 보이기").</summary>
    public bool ShowBuiltIns { get; set; } = true;

    /// <summary>열 때 색인 파일이 깨져 있어 새로 만들었는지.</summary>
    public bool IndexWasCorrupt { get; private set; }

    /// <summary>자료 폴더의 항목(그림·메모, 최근에 넣은 것부터).</summary>
    public IReadOnlyList<ReferenceItem> Items => _items;

    /// <summary>목록에 보일 항목: 사용자 항목 다음에 기본 예시(<see cref="ShowBuiltIns"/>일 때).</summary>
    public IEnumerable<ReferenceItem> AllItems => ShowBuiltIns ? _items.Concat(_builtIns.Values.Select(b => b.Item)) : _items;

    /// <summary>설명이 아직 없는 그림 수(AI로 설명 붙이기 안내).</summary>
    public int UncaptionedCount => _items.Count(i => i.NeedsCaption);

    /// <summary>
    /// 폴더를 열고(없으면 만듦) 색인을 읽은 뒤 파일과 맞춘다. 색인이 없거나 깨졌으면 파일에서 다시 만든다.
    /// 폴더를 만들 수 없으면 <see cref="IOException"/>·<see cref="UnauthorizedAccessException"/>.
    /// </summary>
    public static ReferenceLibrary Open(string root, ReferenceLibraryOptions? options = null)
    {
        var lib = new ReferenceLibrary(Path.GetFullPath(root), options ?? new ReferenceLibraryOptions());
        Directory.CreateDirectory(lib.Root);
        lib.LoadIndex();
        lib.Rescan();
        return lib;
    }

    /// <summary>id로 찾기(기본 예시는 <see cref="ShowBuiltIns"/>와 관계없이, 규칙은 <see cref="BuiltInReferences.RulesId"/>·<see cref="RulesFileName"/>).</summary>
    public ReferenceItem? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim().Replace('\\', '/');
        if (_builtIns.TryGetValue(id, out var b)) return b.Item;
        if (_items.FirstOrDefault(i => i.Id == id) is { } item) return item;
        return RulesItems().FirstOrDefault(r => r.Id == id)
            ?? _items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>항목 파일의 전체 경로(기본 자료는 null).</summary>
    public string? FullPath(ReferenceItem item)
        => item.BuiltIn ? null : Path.Combine(Root, item.Id.Replace('/', Path.DirectorySeparatorChar));

    // ───────────── 색인 ─────────────

    private void LoadIndex()
    {
        if (!File.Exists(IndexPath))
        {
            _dirty = true;
            return;
        }
        try
        {
            var dto = JsonSerializer.Deserialize<IndexDto>(File.ReadAllBytes(IndexPath), Json) ?? throw new JsonException();
            _items = Clean(dto.Items);
            _removed = Clean(dto.Removed);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException or UnauthorizedAccessException)
        {
            // 깨진 색인: 파일에서 다시 만든다(제목·설명은 잃음).
            _items = new();
            _removed = new();
            IndexWasCorrupt = true;
            _dirty = true;
        }

        static List<ReferenceItem> Clean(List<ReferenceItem>? items)
        {
            var list = new List<ReferenceItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var i in items ?? [])
            {
                if (i is null || !IsSafeId(i.Id) || !seen.Add(i.Id)) continue;
                i.Type = i.Type is ReferenceTypes.Note ? ReferenceTypes.Note : ReferenceTypes.Image;
                i.Title ??= "";
                i.Description ??= "";
                i.Tags = BuiltInReferences.NormalizeTags(i.Tags ?? []);
                i.Palette ??= new();
                i.Silhouette ??= "";
                i.Hash ??= "";
                i.Kind = ReferenceKinds.Parse(i.Kind) ?? ReferenceKinds.Other;
                i.CaptionedBy = CaptionSources.Normalize(i.CaptionedBy);
                list.Add(i);
            }
            return list;
        }
    }

    /// <summary>폴더 안을 가리키는 상대 경로인지('..'·절대 경로·빈 칸 거부).</summary>
    private static bool IsSafeId(string? id)
        => id is { Length: > 0 } && !id.StartsWith('/') && !Path.IsPathRooted(id) && !id.Contains('\\')
           && !id.Split('/').Any(part => part is "" or "." or "..") && !id.StartsWith(BuiltInReferences.IdPrefix, StringComparison.Ordinal);

    /// <summary>색인을 쓰고 <see cref="Changed"/>를 알린다.</summary>
    private void SaveIndex()
    {
        var dto = new IndexDto { Items = _items, Removed = _removed };
        Directory.CreateDirectory(Root);
        UndoHistoryIO.WriteAtomic(IndexPath, JsonSerializer.SerializeToUtf8Bytes(dto, Json));
        _dirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>항목이 더해지거나 빠지거나 고쳐졌다(색인을 쓴 뒤 — AI 도구가 고쳐도 관리 창이 따라감).</summary>
    public event EventHandler? Changed;

    // ───────────── 다시 살펴보기 ─────────────

    /// <summary>
    /// 폴더의 파일과 색인을 맞춘다: 새 파일은 정보를 저절로 채워 넣고, 없어진 파일은 빼고, 크기·시각이 바뀐 파일은 내용 해시를 다시 구해
    /// 달라졌으면 그림 정보만 다시 읽는다(제목·설명·태그·종류는 그대로). 바뀐 것이 있으면 색인을 쓴다. 읽을 수 없는 파일은 건너뛴다.
    /// </summary>
    public ReferenceRescanResult Rescan()
    {
        var files = EnumerateFiles();
        var old = _items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var missing = _items.Where(i => !files.ContainsKey(i.Id)).ToList();
        var next = new List<ReferenceItem>(files.Count);
        int added = 0, changed = 0;
        var now = _options.Clock();

        foreach (var (id, path) in files)
        {
            FileInfo info;
            try { info = new FileInfo(path); _ = info.Length; }
            catch (Exception ex) when (IsReadError(ex)) { continue; }
            old.TryGetValue(id, out var existing);
            if (existing is not null && existing.FileSize == info.Length && existing.FileTimeUtc == info.LastWriteTimeUtc)
            {
                if (existing.Type == ReferenceTypes.Note && !_notes.ContainsKey(id)) ReadNote(existing, path);
                next.Add(existing);
                continue;
            }

            string hash;
            try { hash = HashFile(path); }
            catch (Exception ex) when (IsReadError(ex))
            {
                if (existing is not null) next.Add(existing);
                continue;
            }
            if (existing is not null && existing.Hash == hash)
            {
                existing.FileSize = info.Length;
                existing.FileTimeUtc = info.LastWriteTimeUtc;
                if (existing.Type == ReferenceTypes.Note && !_notes.ContainsKey(id)) ReadNote(existing, path);
                next.Add(existing);
                _dirty = true;
                continue;
            }

            // 새 파일이거나 내용이 바뀐 파일. 같은 내용이 방금 없어졌거나 예전에 지운 것이면(이름 바꿈·휴지통에서 되살림) 정보를 이어받는다.
            var item = existing ?? Recall(missing, hash) ?? NewItem(id, now);
            bool isNew = existing is null;
            item.Id = id;
            item.Type = IsNote(path) ? ReferenceTypes.Note : ReferenceTypes.Image;
            if (item.Type == ReferenceTypes.Image)
            {
                if (TryDecode(path) is not { } frames) continue;
                item.ApplyStats(ReferenceAnalysis.Analyze(frames.Images));
            }
            else
            {
                ReadNote(item, path);
                item.Width = item.Height = item.Frames = item.Colors = 0;
                item.Palette = new();
                item.Silhouette = "";
            }
            item.Hash = hash;
            item.FileSize = info.Length;
            item.FileTimeUtc = info.LastWriteTimeUtc;
            item.UpdatedUtc = now;
            if (item.Title.Trim().Length == 0) item.Title = DefaultTitle(item, path);
            next.Add(item);
            if (isNew) added++;
            else changed++;
            _dirty = true;
        }

        var keptIds = next.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        int removed = 0;
        foreach (var gone in missing)
        {
            if (keptIds.Contains(gone.Id)) continue;
            removed++;
            _notes.Remove(gone.Id);
            if (gone.Hash.Length > 0 && !next.Any(i => ReferenceEquals(i, gone)))
            {
                _removed.RemoveAll(r => r.Hash == gone.Hash);
                _removed.Insert(0, gone);
            }
        }
        if (_removed.Count > MaxRemembered) _removed.RemoveRange(MaxRemembered, _removed.Count - MaxRemembered);
        if (removed > 0) _dirty = true;

        _items = [.. next.OrderByDescending(i => i.AddedUtc).ThenBy(i => i.Id, StringComparer.Ordinal)];
        if (_dirty) TrySaveIndex();
        return new ReferenceRescanResult(added, changed, removed);
    }

    private void TrySaveIndex()
    {
        try { SaveIndex(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 읽기 전용 폴더 등: 다음에 다시 쓴다.
        }
    }

    /// <summary>같은 해시의 없어진 항목(이번에 없어진 것 먼저, 그다음 기억해 둔 것). 꺼내면 기억에서 뺀다.</summary>
    private ReferenceItem? Recall(List<ReferenceItem> missing, string hash)
    {
        if (missing.FirstOrDefault(m => m.Hash == hash) is { } moved)
        {
            missing.Remove(moved);
            return moved;
        }
        int i = _removed.FindIndex(r => r.Hash == hash);
        if (i < 0) return null;
        var item = _removed[i];
        _removed.RemoveAt(i);
        return item;
    }

    private static ReferenceItem NewItem(string id, DateTime now) => new()
    {
        Id = id,
        AddedUtc = now,
        Kind = GuessKind(id),
        CaptionedBy = CaptionSources.None,
    };

    /// <summary>폴더 안의 자료 파일(id → 전체 경로). 색인·규칙·숨김·임시 파일은 뺀다.</summary>
    private SortedDictionary<string, string> EnumerateFiles()
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(Root, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return map;
        }
        foreach (var path in paths)
        {
            var id = Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
            if (!IsSafeId(id) || id.Split('/').Any(p => p.StartsWith('.'))) continue;
            if (id is IndexFileName or RulesFileName) continue;
            if (!IsSupported(path)) continue;
            map[id] = path;
        }
        return map;
    }

    /// <summary>자료로 넣을 수 있는 파일인지(확장자).</summary>
    public bool IsSupported(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" || ext == SpriteProject.FileExtension || NoteExtensions.Contains(ext) || _options.OtherImageExtensions.Contains(ext);
    }

    private static bool IsNote(string path) => NoteExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    private static string HashFile(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(fs))[..16];
    }

    private static bool IsReadError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException
            or ArgumentException or InvalidOperationException or FormatException or OverflowException;

    // ───────────── 그림·글 읽기 ─────────────

    /// <summary>그림의 프레임. 파일을 읽을 수 없으면 <see cref="IOException"/>·<see cref="InvalidDataException"/>.</summary>
    public ReferenceFrames LoadFrames(ReferenceItem item)
    {
        if (!item.IsImage) throw new InvalidOperationException(item.Id);
        if (item.BuiltIn) return _builtIns.TryGetValue(item.Id, out var b) ? b.Frames : throw new InvalidOperationException(item.Id);
        var path = FullPath(item)!;
        return Decode(path) ?? throw new InvalidDataException(Path.GetFileName(path));
    }

    private ReferenceFrames? TryDecode(string path)
    {
        try { return Decode(path); }
        catch (Exception ex) when (IsReadError(ex)) { return null; }
    }

    private ReferenceFrames? Decode(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".png") return ReferenceFrames.Single(PngIO.Load(path));
        if (ext == SpriteProject.FileExtension)
        {
            var p = ProjectIO.Load(path);
            if (p.Frames.Count == 0) return null;
            return new ReferenceFrames([.. Enumerable.Range(0, p.Frames.Count).Select(i => p.Flatten(i))], [.. p.Frames.Select(f => f.DurationMs)]);
        }
        var frames = _options.DecodeOther?.Invoke(path);
        return frames is { Images.Count: > 0 } && frames.Images.All(f => f.Width == frames.Images[0].Width && f.Height == frames.Images[0].Height) ? frames : null;
    }

    private void ReadNote(ReferenceItem item, string path)
    {
        string text;
        try { text = File.ReadAllText(path); }
        catch (Exception ex) when (IsReadError(ex)) { text = ""; }
        if (text.Length > MaxNoteLength) text = text[..MaxNoteLength];
        _notes[item.Id] = text;
    }

    /// <summary>글 항목(메모·규칙)의 본문. 그림이면 null.</summary>
    public string? ReadText(ReferenceItem item)
    {
        if (item.Type == ReferenceTypes.Note) return _notes.GetValueOrDefault(item.Id) ?? "";
        if (item.Type != ReferenceTypes.Rules) return null;
        return item.Id == BuiltInReferences.RulesId ? BuiltInReferences.RulesText(_options.Korean) : ReadUserRules() ?? "";
    }

    /// <summary>파일 이름에서 제목: 확장자를 떼고 '_'·'-'를 빈칸으로. 메모는 첫 "# 제목" 줄이 있으면 그것.</summary>
    private string DefaultTitle(ReferenceItem item, string path)
    {
        if (item.Type == ReferenceTypes.Note && _notes.GetValueOrDefault(item.Id) is { } text && HeadingOf(text) is { } heading) return heading;
        return Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ').Trim();
    }

    private static string? HeadingOf(string text)
    {
        foreach (var raw in text.Split('\n').Take(10))
        {
            var line = raw.Trim();
            if (line.StartsWith('#')) return line.TrimStart('#').Trim() is { Length: > 0 } h ? h : null;
            if (line.Length > 0) return null;
        }
        return null;
    }

    /// <summary>파일 이름으로 종류 짐작(모르면 other).</summary>
    internal static string GuessKind(string id)
    {
        var name = id.ToLowerInvariant();
        foreach (var (kind, words) in KindWords)
            if (words.Any(name.Contains)) return kind;
        return ReferenceKinds.Other;
    }

    private static readonly (string Kind, string[] Words)[] KindWords =
    [
        (ReferenceKinds.Tile, ["tile", "grass", "ground", "floor", "wall", "terrain", "타일", "바닥", "벽", "잔디", "지형"]),
        (ReferenceKinds.Effect, ["effect", "fx", "explosion", "smoke", "spark", "magic", "slash", "이펙트", "효과", "폭발", "연기", "마법"]),
        (ReferenceKinds.Ui, ["ui", "icon", "button", "cursor", "hud", "아이콘", "버튼", "커서"]),
        (ReferenceKinds.Item, ["item", "sword", "weapon", "potion", "coin", "key", "gem", "shield", "bow", "아이템", "칼", "검", "무기", "물약", "동전", "보석", "방패", "활"]),
        (ReferenceKinds.Character, ["char", "hero", "player", "npc", "enemy", "slime", "monster", "walk", "idle", "run", "캐릭터", "주인공", "몬스터", "슬라임", "적", "걷기"]),
    ];

    // ───────────── 규칙 ─────────────

    /// <summary>사용자 규칙 본문(없거나 읽을 수 없으면 null).</summary>
    public string? ReadUserRules()
    {
        try { return File.Exists(UserRulesPath) ? File.ReadAllText(UserRulesPath) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>사용자 rules.md가 없으면 틀로 만든다. 경로를 돌려준다.</summary>
    public string EnsureUserRules()
    {
        if (!File.Exists(UserRulesPath))
        {
            Directory.CreateDirectory(Root);
            UndoHistoryIO.WriteAtomic(UserRulesPath, System.Text.Encoding.UTF8.GetBytes(BuiltInReferences.RulesTemplate(_options.Korean)));
        }
        return UserRulesPath;
    }

    /// <summary>규칙 항목: 기본 규칙, 그리고 rules.md가 있으면 사용자 규칙.</summary>
    private IEnumerable<ReferenceItem> RulesItems()
    {
        yield return _rulesItem ??= BuiltInReferences.Rules();
        if (File.Exists(UserRulesPath))
            yield return new ReferenceItem
            {
                Id = RulesFileName,
                Type = ReferenceTypes.Rules,
                Title = HeadingOf(ReadUserRules() ?? "") ?? RulesFileName,
                CaptionedBy = CaptionSources.User,
            };
    }

    private ReferenceItem? _rulesItem;

    // ───────────── 고치기·넣기 ─────────────

    /// <summary>
    /// 제목·설명·태그·종류를 고친다(null = 그대로, 빈 제목은 무시). 설명·태그를 바꾸면 <paramref name="captionedBy"/>를 남긴다.
    /// 기본 자료나 없는 id면 <see cref="KeyNotFoundException"/>/<see cref="InvalidOperationException"/>.
    /// </summary>
    public ReferenceItem Update(string id, string? title, string? description, IEnumerable<string>? tags, string? kind, string captionedBy)
    {
        var item = Find(id) ?? throw new KeyNotFoundException(id);
        if (item.BuiltIn || item.Type == ReferenceTypes.Rules) throw new InvalidOperationException(id);
        bool caption = false;
        if (title is not null && title.Trim().Length > 0) item.Title = title.Trim();
        if (description is not null && description.Trim() != item.Description) { item.Description = description.Trim(); caption = true; }
        if (tags is not null)
        {
            var list = BuiltInReferences.NormalizeTags(tags);
            if (!list.SequenceEqual(item.Tags)) { item.Tags = list; caption = true; }
        }
        if (kind is not null) item.Kind = ReferenceKinds.Parse(kind) ?? ReferenceKinds.Other;
        if (caption) item.CaptionedBy = CaptionSources.Normalize(captionedBy);
        item.UpdatedUtc = _options.Clock();
        SaveIndex();
        return item;
    }

    /// <summary>
    /// 그림을 새 자료로 저장한다: 한 프레임이면 PNG, 여러 프레임이면 .gpixel(프레임 길이 유지). 파일 이름은 <paramref name="name"/>
    /// (없으면 "reference")에서 만들고 겹치면 "-2"…를 붙인다. 쓰기에 실패하면 IO 예외.
    /// </summary>
    public ReferenceItem AddImage(ReferenceFrames frames, string? name, string? title, string? description, IEnumerable<string>? tags, string? kind, string captionedBy)
    {
        if (frames.Images.Count == 0) throw new ArgumentException("no frames", nameof(frames));
        var baseName = ProjectExport.CleanFileName(string.IsNullOrWhiteSpace(name) ? "reference" : name.Trim());
        if (baseName.Length > 60) baseName = baseName[..60].Trim();
        if (baseName.Length == 0) baseName = "reference";
        bool animated = frames.Images.Count > 1;
        var path = UniquePath(baseName, animated ? SpriteProject.FileExtension : ".png");
        Directory.CreateDirectory(Root);
        if (!animated) PngIO.Save(frames.Images[0], path);
        else
        {
            var p = SpriteProject.FromImage(frames.Images[0]);
            for (int i = 1; i < frames.Images.Count; i++)
            {
                p.AddFrame();
                p.SetCel(0, i, frames.Images[i].Clone());
            }
            for (int i = 0; i < p.Frames.Count && i < frames.DurationsMs.Count; i++) p.Frames[i].DurationMs = Math.Max(1, frames.DurationsMs[i]);
            ProjectIO.Save(p, path);
        }
        Rescan();
        var id = Path.GetRelativePath(Root, path).Replace(Path.DirectorySeparatorChar, '/');
        var item = _items.FirstOrDefault(i => i.Id == id) ?? throw new InvalidDataException(id);
        if (string.IsNullOrWhiteSpace(title) && name is not null) title = name;
        return Update(id, title, description, tags, kind ?? item.Kind, captionedBy);
    }

    /// <summary>
    /// 파일들을 자료 폴더로 복사한다(지원하지 않는 형식·폴더 안에 이미 있는 파일은 건너뛰고, 이름이 겹치면 "-2"…). 새로 생긴 항목을 돌려준다.
    /// </summary>
    public IReadOnlyList<ReferenceItem> ImportFiles(IEnumerable<string> paths)
    {
        var copied = new List<string>();
        Directory.CreateDirectory(Root);
        foreach (var raw in paths)
        {
            var src = Path.GetFullPath(raw);
            if (!File.Exists(src) || !IsSupported(src)) continue;
            if (src.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var dest = UniquePath(Path.GetFileNameWithoutExtension(src), Path.GetExtension(src).ToLowerInvariant());
            File.Copy(src, dest);
            copied.Add(Path.GetRelativePath(Root, dest).Replace(Path.DirectorySeparatorChar, '/'));
        }
        if (copied.Count == 0) return [];
        Rescan();
        return [.. _items.Where(i => copied.Contains(i.Id))];
    }

    /// <summary>폴더 바로 아래의 겹치지 않는 경로(색인·규칙 파일 이름은 피함).</summary>
    private string UniquePath(string baseName, string ext)
    {
        for (int n = 1; ; n++)
        {
            var file = (n == 1 ? baseName : $"{baseName}-{n}") + ext;
            if (file.Equals(IndexFileName, StringComparison.OrdinalIgnoreCase) || file.Equals(RulesFileName, StringComparison.OrdinalIgnoreCase)) continue;
            var path = Path.Combine(Root, file);
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
        }
    }
}
