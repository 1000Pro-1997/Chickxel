using System.IO.Compression;
using Chickxel.Core.Localization;
using SkiaSharp;

namespace Chickxel.Core.Text;

/// <summary>글꼴 종류: 도트(BDF — 정수 배로 키움) / 벡터(TTF·OTF·TTC — SkiaSharp로 그림).</summary>
public enum FontKind
{
    Bitmap,
    Vector,
}

/// <summary>
/// 글꼴 하나. <see cref="Name"/> = 프로젝트에 저장하는 ID(기본 글꼴은 "Galmuri11" 같은 이름 — 예전 글꼴 열거 이름과 같음, 사용자 글꼴은 글꼴 폴더 안 파일 이름).
/// <see cref="DisplayName"/> = 글꼴에서 읽은 이름(가족 + 굵기·기울기) 또는 파일 이름. <see cref="NativeSize"/> = 도트 글꼴의 원래 줄 높이(px, ascent + descent), 벡터는 0.
/// </summary>
public sealed record FontInfo(string Name, string DisplayName, FontKind Kind, int NativeSize, bool BuiltIn);

/// <summary>
/// 글씨 효과의 글꼴 목록: 앱에 든 Galmuri BDF(어셈블리 리소스) + 사용자 글꼴 폴더(<see cref="UserFolder"/>)의 .ttf .otf .ttc .bdf 파일.
/// 사용자 글꼴은 그 폴더에만 두고 프로젝트에는 이름만 저장한다 — 없는 글꼴은 <see cref="DefaultName"/>로 그린다.
/// 폴더가 바뀌었는지는 <see cref="List"/>는 부를 때마다, 그리기(<see cref="Find"/> 등)는 1초에 한 번까지 살핀다. 여러 스레드에서 불러도 된다.
/// </summary>
public static class FontLibrary
{
    public const string DefaultName = "Galmuri11";

    /// <summary>사용자 글꼴로 받는 확장자(소문자).</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".ttf", ".otf", ".ttc", ".bdf"];

    /// <summary>그리기 쪽 폴더 확인 간격(ms).</summary>
    private const long RescanInterval = 1000;

    /// <summary>앱에 든 글꼴: 저장 이름, 표시 이름, 리소스 이름(<c>Chickxel.Fonts.{리소스}.bdf.gz</c>).</summary>
    private static readonly (string Name, string Display, string Resource)[] BuiltInFonts =
    [
        ("Galmuri7", "Galmuri 7", "Galmuri7"),
        ("Galmuri9", "Galmuri 9", "Galmuri9"),
        ("Galmuri11", "Galmuri 11", "Galmuri11"),
        ("Galmuri11Bold", "Galmuri 11 Bold", "Galmuri11-Bold"),
        ("Galmuri14", "Galmuri 14", "Galmuri14"),
    ];

    /// <summary>앱에 든 글꼴의 목록 칸(머리만 읽음)과 글꼴 원본(처음 쓸 때 한 번 다 읽음).</summary>
    private static readonly Lazy<FontInfo[]> BuiltInInfos = new(() =>
        [.. BuiltInFonts.Select(f => new FontInfo(f.Name, f.Display, FontKind.Bitmap, ReadBuiltIn(f.Resource, PixelFont.ReadHeader).LineHeight, true))]);

    private static readonly Lazy<BitmapSource>[] BuiltInSources =
        [.. BuiltInFonts.Select(f => new Lazy<BitmapSource>(() => new BitmapSource(ReadBuiltIn(f.Resource, PixelFont.Parse), "builtin:" + f.Name)))];

    private static readonly object Gate = new();
    private static string? _folder;
    private static UserFont[] _user = [];
    private static long _checkedAt = long.MinValue;

    /// <summary>읽어 둔 사용자 글꼴 원본(파일 경로 → 크기·수정 시각이 같을 때만 다시 씀).</summary>
    private static readonly Dictionary<string, UserSource> Sources = new(StringComparer.Ordinal);

    /// <summary>
    /// 사용자 글꼴 폴더(앱은 시작할 때 <c>Path.Combine(Platform.AppFolder, "Fonts")</c>로 정함). null = 기본 글꼴만.
    /// 바꾸면 바로 다시 살피고, 목록이 달라졌으면 <see cref="Changed"/>를 알린다.
    /// </summary>
    public static string? UserFolder
    {
        get { lock (Gate) return _folder; }
        set
        {
            lock (Gate)
            {
                _folder = string.IsNullOrWhiteSpace(value) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
                _checkedAt = long.MinValue;
            }
            Rescan(force: true);
        }
    }

    /// <summary>글꼴을 더하거나 지웠거나 폴더를 다시 살펴 목록이 달라졌을 때(아무 스레드에서나).</summary>
    public static event Action? Changed;

    /// <summary>기본 글꼴 다음에 사용자 글꼴(표시 이름 차례). 부를 때마다 폴더가 바뀌었는지 살핀다.</summary>
    public static IReadOnlyList<FontInfo> List()
    {
        Rescan(force: true);
        lock (Gate) return [.. BuiltInInfos.Value, .. _user.Select(u => u.Info)];
    }

    /// <summary>이름이 <paramref name="name"/>인 글꼴(대소문자 무시). 이 컴퓨터에 없으면 null.</summary>
    public static FontInfo? Find(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (BuiltInIndex(name) is int b and >= 0) return BuiltInInfos.Value[b];
        Rescan(force: false);
        lock (Gate) return UserIndex(name) is int u and >= 0 ? _user[u].Info : null;
    }

    /// <summary>기본 글꼴도 아니고 사용자 글꼴 폴더에도 없는 이름인지(그리기는 <see cref="DefaultName"/>로 대신함 — 앱은 경고를 보임).</summary>
    public static bool IsMissing(string? name) => Find(name) is null;

    /// <summary>
    /// 글꼴 파일(.ttf .otf .ttc .bdf)을 읽어 보고(글자를 불러 봄) 사용자 글꼴 폴더에 복사한다(이름이 겹치면 "이름 (2).ttf"처럼).
    /// 이미 폴더 안의 파일이면 복사하지 않는다. 읽을 수 없으면 <see cref="InvalidDataException"/>(사용자에게 보일 문구).
    /// </summary>
    public static FontInfo Add(string path)
    {
        var folder = UserFolder ?? throw new InvalidOperationException("FontLibrary.UserFolder is not set.");
        var fileName = Path.GetFileName(path);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!Extensions.Contains(ext)) throw new InvalidDataException(DomainStrings.Font_Unsupported);
        try
        {
            Validate(path, ext);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException(DomainStrings.FormatFont_Unreadable(fileName), ex);
        }

        string target;
        var source = Path.GetFullPath(path);
        if (string.Equals(Path.GetDirectoryName(source), folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
        {
            target = source;
        }
        else
        {
            Directory.CreateDirectory(folder);
            target = UniquePath(folder, fileName);
            File.Copy(source, target);
        }
        Rescan(force: true);
        return Find(Path.GetFileName(target)) ?? throw new InvalidDataException(DomainStrings.FormatFont_Unreadable(fileName));
    }

    /// <summary>사용자 글꼴 파일을 지운다(없는 이름이면 아무것도 안 함). 그 글꼴을 쓰던 글씨는 <see cref="DefaultName"/>로 그린다. 기본 글꼴은 지울 수 없다.</summary>
    public static void Remove(string name)
    {
        if (BuiltInIndex(name) >= 0) throw new InvalidOperationException($"Built-in font can't be removed: {name}");
        Rescan(force: true);
        string? path;
        lock (Gate) path = UserIndex(name) is int u and >= 0 ? _user[u].Path : null;
        if (path is null) return;
        File.Delete(path);
        Rescan(force: true);
    }

    /// <summary>
    /// 그릴 글꼴 원본: 이름이 가리키는 글꼴, 없거나 읽을 수 없으면 <see cref="DefaultName"/>.
    /// </summary>
    internal static FontSource Source(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Default;
        if (BuiltInIndex(name) is int b and >= 0) return BuiltInSources[b].Value;
        Rescan(force: false);
        UserFont? font;
        lock (Gate) font = UserIndex(name) is int u and >= 0 ? _user[u] : null;
        if (font is null) return Default;
        lock (Gate)
        {
            if (Sources.TryGetValue(font.Path, out var cached) && cached.Length == font.Length && cached.Modified == font.Modified)
                return cached.Source ?? Default;
        }
        FontSource? loaded;
        try
        {
            loaded = Load(font.Path, Path.GetExtension(font.Path).ToLowerInvariant(),
                $"user:{font.Info.Name}:{font.Length}:{font.Modified.Ticks}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            loaded = null;
        }
        lock (Gate) Sources[font.Path] = new UserSource(font.Length, font.Modified, loaded);
        return loaded ?? Default;
    }

    /// <summary>기본 글꼴(Galmuri11) 원본 — 없는 글꼴·사용자 글꼴에 없는 글자를 대신 그림.</summary>
    internal static BitmapSource Default => BuiltInSources[BuiltInIndex(DefaultName)].Value;

    /// <summary>앱에 든 글꼴 이름이면 그 정식 이름(대소문자를 맞춤), 아니면 null.</summary>
    internal static string? BuiltInName(string name) => BuiltInIndex(name) is int i and >= 0 ? BuiltInFonts[i].Name : null;

    private static int BuiltInIndex(string name)
    {
        for (int i = 0; i < BuiltInFonts.Length; i++)
            if (string.Equals(BuiltInFonts[i].Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>사용자 글꼴 목록 안 위치(잠금 안에서 부름).</summary>
    private static int UserIndex(string name)
    {
        for (int i = 0; i < _user.Length; i++)
            if (string.Equals(_user[i].Info.Name, name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>
    /// 폴더를 다시 살핀다(<paramref name="force"/>가 아니면 마지막 확인 뒤 <see cref="RescanInterval"/>이 지났을 때만).
    /// 파일 크기·수정 시각이 같은 글꼴은 다시 읽지 않는다. 목록이 달라졌으면 <see cref="Changed"/>.
    /// </summary>
    private static void Rescan(bool force)
    {
        bool changed;
        lock (Gate)
        {
            long now = Environment.TickCount64;
            if (!force && _checkedAt != long.MinValue && now - _checkedAt < RescanInterval) return;
            _checkedAt = now;
            var previous = _user;
            var next = Scan(_folder, previous);
            changed = next.Length != previous.Length || next.Where((u, i) => u != previous[i]).Any();
            if (!changed) return;
            _user = next;
            var alive = next.Select(u => u.Path).ToHashSet(StringComparer.Ordinal);
            foreach (var path in Sources.Keys.Where(p => !alive.Contains(p)).ToList()) Sources.Remove(path);
        }
        TextFace.ClearCache();
        Changed?.Invoke();
    }

    private static UserFont[] Scan(string? folder, UserFont[] previous)
    {
        if (folder is null || !Directory.Exists(folder)) return [];
        var list = new List<UserFont>();
        IEnumerable<FileInfo> files;
        try
        {
            files = new DirectoryInfo(folder).EnumerateFiles().Where(f => Extensions.Contains(f.Extension.ToLowerInvariant())).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return previous;
        }
        foreach (var file in files)
        {
            var same = previous.FirstOrDefault(u => u.Path == file.FullName && u.Length == file.Length && u.Modified == file.LastWriteTimeUtc);
            if (same is not null)
            {
                list.Add(same);
                continue;
            }
            if (ReadInfo(file) is { } info) list.Add(new UserFont(info, file.FullName, file.Length, file.LastWriteTimeUtc));
        }
        list.Sort((a, b) =>
        {
            int c = string.Compare(a.Info.DisplayName, b.Info.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            return c != 0 ? c : string.Compare(a.Info.Name, b.Info.Name, StringComparison.OrdinalIgnoreCase);
        });
        return [.. list];
    }

    /// <summary>목록 칸 하나(이름·종류·크기만 읽음). 읽을 수 없는 파일은 null(목록에서 뺌).</summary>
    private static FontInfo? ReadInfo(FileInfo file)
    {
        try
        {
            if (file.Extension.Equals(".bdf", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(file.FullName);
                var (lineHeight, display) = PixelFont.ReadHeader(reader);
                return new FontInfo(file.Name, display ?? Path.GetFileNameWithoutExtension(file.Name), FontKind.Bitmap, lineHeight, false);
            }
            using var typeface = LoadTypeface(file.FullName);
            if (typeface is null) return null;
            return new FontInfo(file.Name, VectorDisplayName(typeface) ?? Path.GetFileNameWithoutExtension(file.Name), FontKind.Vector, 0, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>가족 이름 + 굵기(600 이상 "Bold")·기울기("Italic"). 가족 이름이 없으면 null.</summary>
    private static string? VectorDisplayName(SKTypeface typeface)
    {
        if (string.IsNullOrWhiteSpace(typeface.FamilyName)) return null;
        var name = typeface.FamilyName.Trim();
        if (typeface.FontWeight >= (int)SKFontStyleWeight.SemiBold) name += " Bold";
        if (typeface.FontSlant != SKFontStyleSlant.Upright) name += " Italic";
        return name;
    }

    /// <summary>더하기 전 검사: 도트 글꼴은 끝까지 읽어 글자가 있는지, 벡터 글꼴은 열려서 글자가 있는지.</summary>
    private static void Validate(string path, string ext)
    {
        var loaded = Load(path, ext, "check");
        switch (loaded)
        {
            case BitmapSource { Font.GlyphCount: 0 }:
            case VectorSource { Typeface.GlyphCount: <= 1 }:
                throw new InvalidDataException("");
        }
        (loaded as VectorSource)?.Typeface.Dispose();
    }

    /// <summary>글꼴 파일 읽기. 벡터 글꼴은 바이트를 복사해 열어(파일을 잡고 있지 않음 — 지우기·바꾸기 가능) 첫 글꼴(TTC면 0번)을 쓴다.</summary>
    private static FontSource Load(string path, string ext, string key)
    {
        if (ext == ".bdf")
        {
            using var reader = new StreamReader(path);
            return new BitmapSource(PixelFont.Parse(reader), key);
        }
        return new VectorSource(LoadTypeface(path) ?? throw new InvalidDataException(""), key);
    }

    private static SKTypeface? LoadTypeface(string path)
    {
        using var data = SKData.CreateCopy(File.ReadAllBytes(path));
        return data is null ? null : SKTypeface.FromData(data, 0);
    }

    private static T ReadBuiltIn<T>(string resource, Func<TextReader, T> read)
    {
        var name = $"Chickxel.Fonts.{resource}.bdf.gz";
        using var stream = typeof(FontLibrary).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Missing font resource: {name}");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return read(reader);
    }

    /// <summary>폴더 안에서 겹치지 않는 경로("이름.ttf" → "이름 (2).ttf" …).</summary>
    private static string UniquePath(string folder, string fileName)
    {
        var target = Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{stem} ({n}){ext}");
        return target;
    }

    private sealed record UserFont(FontInfo Info, string Path, long Length, DateTime Modified);

    private sealed record UserSource(long Length, DateTime Modified, FontSource? Source);
}

/// <summary>읽어 둔 글꼴 원본. <see cref="Key"/>는 글꼴·판마다 달라 그리기 캐시(<see cref="TextFace"/>)의 키가 된다.</summary>
internal abstract class FontSource(string key)
{
    public string Key { get; } = key;
}

internal sealed class BitmapSource(PixelFont font, string key) : FontSource(key)
{
    public PixelFont Font { get; } = font;
}

internal sealed class VectorSource(SKTypeface typeface, string key) : FontSource(key)
{
    public SKTypeface Typeface { get; } = typeface;
}
